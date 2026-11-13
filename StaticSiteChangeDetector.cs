using System.Diagnostics;

namespace UpdateManager.StaticSiteManagementCoreLibrary;
public static class StaticSiteChangeDetector
{
    public static async Task<StaticSiteChangeResult> InspectAsync(
        string projectPath,
        string lastSubmittedCommit,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);

        projectPath = Path.GetFullPath(projectPath);

        if (!File.Exists(projectPath))
        {
            throw new FileNotFoundException(
                "The static-site project was not found.",
                projectPath);
        }

        string directory = Path.GetDirectoryName(projectPath)!;

        string root = await RunGitAsync(
            directory,
            cancellationToken,
            "rev-parse",
            "--show-toplevel");

        string actualRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(root.Trim()));

        string expectedRoot =
            Path.TrimEndingDirectorySeparator(directory);

        if (!string.Equals(
            actualRoot,
            expectedRoot,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The static site must have its own Git repository.");
        }

        string currentCommit = (
            await RunGitAsync(
                directory,
                cancellationToken,
                "rev-parse",
                "--verify",
                "HEAD")).Trim();

        string status = await RunGitAsync(
            directory,
            cancellationToken,
            "status",
            "--porcelain=v1",
            "--untracked-files=all");

        bool baselineRequired =
            string.IsNullOrWhiteSpace(lastSubmittedCommit);

        return new StaticSiteChangeResult
        {
            CurrentCommit = currentCommit,
            BaselineRequired = baselineRequired,
            HasUncommittedChanges =
                !string.IsNullOrWhiteSpace(status),
            HasCommittedChanges =
                !baselineRequired &&
                !string.Equals(
                    currentCommit,
                    lastSubmittedCommit.Trim(),
                    StringComparison.OrdinalIgnoreCase)
        };
    }

    private static async Task<string> RunGitAsync(
        string directory,
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ProcessStartInfo startInfo = new()
        {
            FileName = "git",
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";

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

            string output = await outputTask;
            string error = await errorTask;

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Git inspection failed in '{directory}'." +
                    Environment.NewLine +
                    error);
            }

            return output;
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