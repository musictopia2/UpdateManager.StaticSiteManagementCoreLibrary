using System.Diagnostics;

namespace UpdateManager.StaticSiteManagementCoreLibrary;
public static class LocalStaticSiteHostDeploymentService
{
    public static async Task DeployAsync(
        string publishedWebRoot,
        string hostWebRoot,
        string siteFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publishedWebRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(hostWebRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(siteFolder);

        if (siteFolder is "." or ".." ||
            siteFolder.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            siteFolder.EndsWith('.') ||
            siteFolder.EndsWith(' '))
        {
            throw new ArgumentException(
                $"Invalid site folder: '{siteFolder}'.", nameof(siteFolder));
        }

        publishedWebRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(publishedWebRoot));

        hostWebRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(hostWebRoot));

        if (!Directory.Exists(hostWebRoot))
        {
            throw new DirectoryNotFoundException(
                $"The host web root was not found: {hostWebRoot}");
        }

        VerifyIndexFile(publishedWebRoot);

        DirectoryInfo hostDirectory = new(hostWebRoot);
        DirectoryInfo hostParent = hostDirectory.Parent
            ?? throw new ArgumentException(
                "The host web root must have a parent folder.", nameof(hostWebRoot));

        string liveDirectory = Path.Combine(hostWebRoot, siteFolder);
        string workDirectory = Path.Combine(
            hostParent.FullName, $".{hostDirectory.Name}-deploy", siteFolder);

        if (DirectoriesOverlap(publishedWebRoot, liveDirectory) ||
            DirectoriesOverlap(publishedWebRoot, workDirectory))
        {
            throw new ArgumentException(
                "The published output must be separate from the live and deployment folders.",
                nameof(publishedWebRoot));
        }

        Directory.CreateDirectory(workDirectory);

        using FileStream deploymentLock = File.Open(
            Path.Combine(workDirectory, "deploy.lock"),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);

        string incomingDirectory = Path.Combine(workDirectory, "incoming");
        string previousDirectory = Path.Combine(workDirectory, "previous");

        // Recover the previous build if a run stopped between the moves.
        if (Directory.Exists(previousDirectory))
        {
            if (Directory.Exists(liveDirectory))
            {
                Directory.Delete(previousDirectory, recursive: true);
            }
            else
            {
                Directory.Move(previousDirectory, liveDirectory);
            }
        }

        if (Directory.Exists(incomingDirectory))
        {
            Directory.Delete(incomingDirectory, recursive: true);
        }

        await CopyPublishedFilesAsync(publishedWebRoot, incomingDirectory);
        VerifyIndexFile(incomingDirectory);

        if (Directory.Exists(liveDirectory))
        {
            Directory.Move(liveDirectory, previousDirectory);
        }

        try
        {
            Directory.Move(incomingDirectory, liveDirectory);
        }
        catch
        {
            if (Directory.Exists(previousDirectory) &&
                !Directory.Exists(liveDirectory))
            {
                Directory.Move(previousDirectory, liveDirectory);
            }

            throw;
        }

        if (Directory.Exists(previousDirectory))
        {
            try
            {
                Directory.Delete(previousDirectory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.WriteLine(
                    $"Deployment succeeded. Could not remove '{previousDirectory}': {ex.Message}");
            }
        }
    }

    private static async Task CopyPublishedFilesAsync(
        string sourceDirectory,
        string destinationDirectory)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "robocopy",
            WorkingDirectory = sourceDirectory,
            UseShellExecute = false,
            ArgumentList =
            {
                sourceDirectory,
                destinationDirectory,
                "/E", "/XJ", "/R:1", "/W:1"
            }
        };

        using Process copy = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start robocopy.");

        await copy.WaitForExitAsync();

        if (copy.ExitCode < 0 || copy.ExitCode >= 8)
        {
            throw new InvalidOperationException(
                $"Could not copy the published files. Robocopy exit code: {copy.ExitCode}.");
        }
    }

    private static void VerifyIndexFile(string directory)
    {
        string indexPath = Path.Combine(directory, "index.html");

        if (!File.Exists(indexPath))
        {
            throw new FileNotFoundException("Published index.html was missing.", indexPath);
        }
    }

    private static bool DirectoriesOverlap(string first, string second)
    {
        return IsWithinDirectory(first, second) || IsWithinDirectory(second, first);
    }

    private static bool IsWithinDirectory(string directory, string candidate)
    {
        string relativePath = Path.GetRelativePath(directory, candidate);

        return !Path.IsPathRooted(relativePath) &&
            relativePath != ".." &&
            !relativePath.StartsWith(
                $"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }
}