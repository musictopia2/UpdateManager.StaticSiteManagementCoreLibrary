using System.Diagnostics;

namespace UpdateManager.StaticSiteManagementCoreLibrary;

public class StaticSiteWorkflowDispatcher(
    IStaticSiteDeploymentGroupProvider groupProvider)
    : IStaticSiteWorkflowDispatcher
{
    public async Task DispatchAsync(
        string deploymentGroup,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deploymentGroup);
        cancellationToken.ThrowIfCancellationRequested();

        PublicStaticSiteDeploymentSettings settings =
            await StaticSiteDeploymentGroupSettings.GetPublicAsync(
                groupProvider, deploymentGroup);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            settings.RepositoryDirectory);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            settings.WorkflowFile);

        string directory =
            Path.GetFullPath(settings.RepositoryDirectory);

        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException(directory);
        }

        string gitPath = Path.Combine(directory, ".git");

        if (!Directory.Exists(gitPath) && !File.Exists(gitPath))
        {
            throw new InvalidOperationException(
                $"The coordinator must have its own repository: {directory}");
        }

        if (Path.GetFileName(settings.WorkflowFile) != settings.WorkflowFile ||
            settings.WorkflowFile.StartsWith('-'))
        {
            throw new InvalidOperationException(
                "WorkflowFile must contain only the workflow filename.");
        }

        string workflowPath = Path.Combine(
            directory,
            ".github",
            "workflows",
            settings.WorkflowFile);

        if (!File.Exists(workflowPath))
        {
            throw new FileNotFoundException(
                "The coordinator workflow was not found.",
                workflowPath);
        }

        ProcessStartInfo startInfo = new()
        {
            FileName = "gh",
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.Environment["GH_PROMPT_DISABLED"] = "1";

        startInfo.ArgumentList.Add("workflow");
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add(settings.WorkflowFile);

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
                    $"Workflow request failed for '{deploymentGroup}'." +
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

        Console.WriteLine(
            $"Coordinator build requested for '{deploymentGroup}'.");
    }
}