using System.Diagnostics;

namespace UpdateManager.StaticSiteManagementCoreLibrary;

internal static class LocalStaticSiteProcessRunner
{
    internal static async Task RunAsync(
        string fileName,
        string workingDirectory,
        string[] arguments,
        int highestSuccessfulExitCode = 0)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start {fileName}.");

        Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> errorTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();
        string[] streams = await Task.WhenAll(outputTask, errorTask);

        if (process.ExitCode < 0 || process.ExitCode > highestSuccessfulExitCode)
        {
            string message = $"{fileName} failed with exit code {process.ExitCode}.";
            string details = string.Join(
                Environment.NewLine,
                streams.Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value.TrimEnd()));

            if (!string.IsNullOrEmpty(details))
            {
                message += Environment.NewLine + details;
            }

            throw new InvalidOperationException(message);
        }
    }
}
