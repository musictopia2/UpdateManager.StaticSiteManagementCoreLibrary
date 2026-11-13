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

        var groups = await groupProvider.GetDeploymentGroupsAsync();

        var group = groups.SingleOrDefault(item =>
            string.Equals(
                item.Name,
                deploymentGroup,
                StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"Deployment group '{deploymentGroup}' is not configured.");

        ArgumentException.ThrowIfNullOrWhiteSpace(
            group.RepositoryDirectory);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            group.WorkflowFile);

        string directory =
            Path.GetFullPath(group.RepositoryDirectory);

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

        if (Path.GetFileName(group.WorkflowFile) != group.WorkflowFile ||
            group.WorkflowFile.StartsWith('-'))
        {
            throw new InvalidOperationException(
                "WorkflowFile must contain only the workflow filename.");
        }

        string workflowPath = Path.Combine(
            directory,
            ".github",
            "workflows",
            group.WorkflowFile);

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
        startInfo.ArgumentList.Add(group.WorkflowFile);

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