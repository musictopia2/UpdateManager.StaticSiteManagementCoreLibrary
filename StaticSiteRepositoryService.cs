using System.Diagnostics;
using System.Text.Json;
using UpdateManager.CoreLibrary.Interfaces;

namespace UpdateManager.StaticSiteManagementCoreLibrary;
public class StaticSiteRepositoryService(
    IStaticSiteRepositoryHandler handler)
    : IStaticSiteRepositoryService
{
    public async Task EnsureRepositoryAsync(
        string projectPath,
        CancellationToken cancellationToken = default)
    {
        string directory = GetProjectDirectory(projectPath);
        string repository = GetRepositoryName(projectPath);
        string branch = handler.DefaultBranch;

        ArgumentException.ThrowIfNullOrWhiteSpace(branch);

        await RequireAsync(
            directory, "git", cancellationToken,
            "check-ref-format", "--branch", branch);

        await RequireAsync(
            directory, "gh", cancellationToken,
            "auth", "status", "--hostname", "github.com");

        var root = await RunAsync(
            directory, "git", cancellationToken,
            "rev-parse", "--show-toplevel");

        if (root.ExitCode == 0)
        {
            string actualRoot = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(root.Output.Trim()));

            string expectedRoot =
                Path.TrimEndingDirectorySeparator(directory);

            if (!string.Equals(
                actualRoot,
                expectedRoot,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The project belongs to a parent Git repository. " +
                    "Each static site must have its own repository.");
            }
        }
        else
        {
            await RequireAsync(
                directory, "git", cancellationToken,
                "init", "--initial-branch", branch);
        }

        string currentBranch = await RequireAsync(
            directory, "git", cancellationToken,
            "symbolic-ref", "--short", "HEAD");

        if (currentBranch.Trim() != branch)
        {
            throw new InvalidOperationException(
                $"Expected branch '{branch}', but found " +
                $"'{currentBranch.Trim()}'.");
        }

        string remotes = await RequireAsync(
            directory, "git", cancellationToken,
            "remote");

        bool hasOrigin = remotes.Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries)
            .Contains("origin");

        if (hasOrigin)
        {
            await VerifyOriginAsync(
                directory, repository, cancellationToken);
        }

        var remote = await RunAsync(
            directory, "gh", cancellationToken,
            "repo", "view", repository,
            "--json", "isPrivate,isEmpty");

        if (remote.ExitCode != 0)
        {
            // An authentication or network failure will also cause this
            // command to fail; it will not overwrite an existing repository.
            await RequireAsync(
                directory, "gh", cancellationToken,
                "repo", "create", repository, "--public");

            remote = await RunAsync(
                directory, "gh", cancellationToken,
                "repo", "view", repository,
                "--json", "isPrivate,isEmpty");

            if (remote.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Could not verify repository '{repository}'. " +
                    remote.Error);
            }
        }

        using (JsonDocument document = JsonDocument.Parse(remote.Output))
        {
            JsonElement details = document.RootElement;

            if (details.GetProperty("isPrivate").GetBoolean())
            {
                throw new InvalidOperationException(
                    $"Repository '{repository}' is private. " +
                    "The current shared-host builder requires public source.");
            }

            if (!hasOrigin &&
                !details.GetProperty("isEmpty").GetBoolean())
            {
                throw new InvalidOperationException(
                    $"Repository '{repository}' already contains commits, " +
                    "but this project has no matching origin. " +
                    "It will not be connected automatically.");
            }
        }

        if (!hasOrigin)
        {
            await RequireAsync(
                directory, "git", cancellationToken,
                "remote", "add", "origin",
                $"https://github.com/{repository}.git");
        }

        string ignorePath = Path.Combine(directory, ".gitignore");

        if (!File.Exists(ignorePath))
        {
            await RequireAsync(
                directory, "dotnet", cancellationToken,
                "new", "gitignore", "--output", directory);
        }

        Console.WriteLine($"Repository ready: {repository}");
    }

    public async Task CommitAndPushAsync(
        string projectPath,
        string commitMessage,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commitMessage);

        await EnsureRepositoryAsync(projectPath, cancellationToken);

        string directory = GetProjectDirectory(projectPath);

        await RequireAsync(
            directory, "git", cancellationToken,
            "add", "--all");

        var changes = await RunAsync(
            directory, "git", cancellationToken,
            "diff", "--cached", "--quiet");

        if (changes.ExitCode == 1)
        {
            await RequireAsync(
                directory, "git", cancellationToken,
                "commit", "-m", commitMessage);
        }
        else if (changes.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Could not inspect staged changes: {changes.Error}");
        }

        // Always push: a previous attempt may have committed successfully
        // and then failed during the push.
        await RequireAsync(
            directory, "git", cancellationToken,
            "push", "--set-upstream", "origin",
            handler.DefaultBranch);

        Console.WriteLine("Static-site source pushed successfully.");
    }

    private string GetRepositoryName(string projectPath)
    {
        string owner = handler.Owner;
        string name = handler.GetRepositoryName(projectPath);

        if (!IsValidName(owner) || !IsValidName(name))
        {
            throw new InvalidOperationException(
                "The GitHub owner or repository name is invalid.");
        }

        return $"{owner}/{name}";
    }

    private static bool IsValidName(string value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
            value != "." &&
            value != ".." &&
            !value.StartsWith('-') &&
            value.All(character =>
                char.IsAsciiLetterOrDigit(character) ||
                character is '-' or '_' or '.');
    }

    private static string GetProjectDirectory(string projectPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);

        projectPath = Path.GetFullPath(projectPath);

        if (!File.Exists(projectPath))
        {
            throw new FileNotFoundException(
                "The static-site project was not found.",
                projectPath);
        }

        return Path.GetDirectoryName(projectPath)!;
    }

    private static async Task VerifyOriginAsync(
        string directory,
        string repository,
        CancellationToken cancellationToken)
    {
        string fetchUrls = await RequireAsync(
            directory, "git", cancellationToken,
            "remote", "get-url", "--all", "origin");

        string pushUrls = await RequireAsync(
            directory, "git", cancellationToken,
            "remote", "get-url", "--push", "--all", "origin");

        foreach (string url in (fetchUrls + "\n" + pushUrls).Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries))
        {
            string actual = url.Trim().TrimEnd('/');

            if (actual.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            {
                actual = actual[..^4];
            }

            string https = $"https://github.com/{repository}";
            string ssh = $"git@github.com:{repository}";
            string sshUri = $"ssh://git@github.com/{repository}";

            if (!string.Equals(actual, https, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(actual, ssh, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(actual, sshUri, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The existing origin does not match the configured " +
                    $"GitHub repository '{repository}'.");
            }
        }
    }

    private static async Task<string> RequireAsync(
        string directory,
        string executable,
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        var result = await RunAsync(
            directory, executable, cancellationToken, arguments);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"{executable} failed with exit code {result.ExitCode}." +
                Environment.NewLine +
                result.Error +
                Environment.NewLine +
                result.Output);
        }

        return result.Output;
    }

    private static async Task<(
        int ExitCode,
        string Output,
        string Error)> RunAsync(
        string directory,
        string executable,
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ProcessStartInfo startInfo = new()
        {
            FileName = executable,
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["GH_PROMPT_DISABLED"] = "1";

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = new() { StartInfo = startInfo };

        process.Start();

        Task<string> outputTask =
            process.StandardOutput.ReadToEndAsync();

        Task<string> errorTask =
            process.StandardError.ReadToEndAsync();

        try
        {
            await process.WaitForExitAsync(cancellationToken);

            return (
                process.ExitCode,
                await outputTask,
                await errorTask);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync();
            await Task.WhenAll(outputTask, errorTask);

            throw;
        }
    }
}