using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace UpdateManager.StaticSiteManagementCoreLibrary;

public class StaticSiteRenderCleanupService(
    IStaticSiteDeploymentGroupProvider groupProvider)
    : IStaticSiteHostCleanupService
{
    public async Task ClearAsync(
        string deploymentGroup,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deploymentGroup);

        PublicStaticSiteDeploymentSettings settings =
            await StaticSiteDeploymentGroupSettings.GetPublicAsync(
                groupProvider, deploymentGroup);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            settings.RepositoryDirectory);

        ArgumentException.ThrowIfNullOrWhiteSpace(settings.WorkflowFile);

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

        using CancellationTokenSource timeout =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        timeout.CancelAfter(TimeSpan.FromMinutes(50));

        try
        {
            await RequestAndWaitAsync(
                directory,
                settings.WorkflowFile,
                deploymentGroup,
                timeout.Token);
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested &&
                  timeout.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Timed out waiting for Render cleanup for '{deploymentGroup}'.");
        }
    }

    private static async Task RequestAndWaitAsync(
        string directory,
        string workflowFile,
        string deploymentGroup,
        CancellationToken cancellationToken)
    {
        string requestId = Guid.NewGuid().ToString("N");

        await RunGhAsync(
            directory,
            cancellationToken,
            "workflow", "run", workflowFile,
            "-f", "clear_render_cache=true",
            "-f", $"request_id={requestId}");

        Console.WriteLine(
            $"Requested Render cleanup for '{deploymentGroup}'.");
        Console.WriteLine($"Request: {requestId}");

        long? runId = null;
        string previousStatus = "";

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (runId is null)
            {
                string runsJson = await RunGhAsync(
                    directory,
                    cancellationToken,
                    "run", "list",
                    "--workflow", workflowFile,
                    "--event", "workflow_dispatch",
                    "--limit", "100",
                    "--json", "databaseId,displayTitle,url");

                using JsonDocument runs =
                    JsonDocument.Parse(runsJson);

                foreach (JsonElement run in
                    runs.RootElement.EnumerateArray())
                {
                    string title =
                        run.GetProperty("displayTitle").GetString() ?? "";

                    if (!title.EndsWith(
                        requestId,
                        StringComparison.Ordinal))
                    {
                        continue;
                    }

                    runId = run.GetProperty("databaseId").GetInt64();

                    Console.WriteLine(
                        $"Cleanup workflow: " +
                        run.GetProperty("url").GetString());

                    break;
                }
            }
            else
            {
                string runJson = await RunGhAsync(
                    directory,
                    cancellationToken,
                    "run", "view",
                    runId.Value.ToString(CultureInfo.InvariantCulture),
                    "--json", "status,conclusion");

                using JsonDocument run =
                    JsonDocument.Parse(runJson);

                string status =
                    run.RootElement.GetProperty("status").GetString() ?? "";

                string conclusion =
                    run.RootElement.GetProperty("conclusion").GetString() ?? "";

                if (status != previousStatus)
                {
                    Console.WriteLine(
                        $"Cleanup workflow status: {status}");

                    previousStatus = status;
                }

                if (status == "completed")
                {
                    if (conclusion != "success")
                    {
                        throw new InvalidOperationException(
                            $"Render cleanup workflow finished with " +
                            $"'{conclusion}'. Run ID: {runId}");
                    }

                    Console.WriteLine(
                        $"Render cleanup completed for '{deploymentGroup}'.");

                    return;
                }
            }

            await Task.Delay(
                TimeSpan.FromSeconds(10),
                cancellationToken);
        }
    }

    private static async Task<string> RunGhAsync(
        string directory,
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        cancellationToken.ThrowIfCancellationRequested();

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
                    $"GitHub CLI failed with exit code {process.ExitCode}." +
                    Environment.NewLine +
                    error +
                    Environment.NewLine +
                    output);
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