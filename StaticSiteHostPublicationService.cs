using System.Diagnostics;
namespace UpdateManager.StaticSiteManagementCoreLibrary;
public class StaticSiteHostPublicationService(
    StaticSiteHostRegistryWriter registryWriter)
    : IStaticSiteHostPublicationService
{
    public async Task PublishAsync(
        string deploymentGroup,
        CancellationToken cancellationToken = default)
    {
        string registryPath =
            await registryWriter.WriteAsync(deploymentGroup);

        string directory = Path.GetDirectoryName(registryPath)!;

        if (!Directory.Exists(Path.Combine(directory, ".git")))
        {
            throw new InvalidOperationException(
                $"The coordinator must have its own Git repository: {directory}");
        }

        bool succeeded =
            await GitHubCommitter.CommitDirectoryToGitHubAsync(
                directory,
                $"Update static-site registrations for {deploymentGroup}",
                cancellationToken);

        if (!succeeded)
        {
            throw new InvalidOperationException(
                $"Coordinator commit or push failed for '{deploymentGroup}'.");
        }

        // The existing committer skips pushing when nothing changed.
        // Always push here to handle a previously committed but unpushed update.
        await PushAsync(directory, cancellationToken);

        Console.WriteLine(
            $"Coordinator changes pushed for '{deploymentGroup}'.");
    }

    private static async Task PushAsync(
        string directory,
        CancellationToken cancellationToken)
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

        startInfo.ArgumentList.Add("push");
        startInfo.ArgumentList.Add("origin");

        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";

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
                    "Coordinator push failed." +
                    Environment.NewLine +
                    error +
                    Environment.NewLine +
                    output);
            }
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