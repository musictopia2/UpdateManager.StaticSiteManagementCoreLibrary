using System.Diagnostics;
using System.Text.Json;

namespace UpdateManager.StaticSiteManagementCoreLibrary;

public static class DockerBindMountResolver
{
    public static Task<string> GetWebRootAsync(
        LocalStaticSiteDeploymentSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return GetHostPathAsync(
            settings.ContainerName,
            settings.ContainerWebRoot);
    }

    private static async Task<string> GetHostPathAsync(
        string containerName,
        string containerPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerPath);

        ProcessStartInfo startInfo = new()
        {
            FileName = "docker",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            ArgumentList = { "inspect", "--type", "container", containerName }
        };

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start Docker.");

        string output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Could not inspect Docker container '{containerName}'.");
        }

        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement container = document.RootElement[0];

        if (!container.GetProperty("State").GetProperty("Running").GetBoolean())
        {
            throw new InvalidOperationException(
                $"Docker container '{containerName}' is not running.");
        }

        JsonElement mount = container.GetProperty("Mounts")
            .EnumerateArray()
            .SingleOrDefault(item =>
                item.GetProperty("Type").GetString() == "bind" &&
                item.GetProperty("Destination").GetString() == containerPath);

        if (mount.ValueKind == JsonValueKind.Undefined)
        {
            throw new InvalidOperationException(
                $"Container '{containerName}' has no bind mount at '{containerPath}'.");
        }

        string source = mount.GetProperty("Source").GetString()
            ?? throw new InvalidOperationException("The bind mount has no source path.");

        if (OperatingSystem.IsWindows())
        {
            source = ConvertDockerDesktopPath(source);
        }

        if (!Path.IsPathFullyQualified(source))
        {
            throw new InvalidOperationException(
                $"Docker returned an unexpected mount source: {source}");
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
    }

    private static string ConvertDockerDesktopPath(string source)
    {
        foreach (string prefix in new[] { "/run/desktop/mnt/host/", "/host_mnt/" })
        {
            if (!source.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            string drivePath = source[prefix.Length..];

            if (drivePath.Length < 2 ||
                !char.IsAsciiLetter(drivePath[0]) ||
                drivePath[1] != '/')
            {
                throw new InvalidOperationException(
                    $"Docker returned an unexpected mount source: {source}");
            }

            return $@"{drivePath[0]}:\{drivePath[2..].Replace('/', '\\')}";
        }

        return source;
    }
}