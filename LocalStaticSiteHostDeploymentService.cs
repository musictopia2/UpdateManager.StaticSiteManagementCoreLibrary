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

        var (liveDirectory, workDirectory) = GetSiteDirectories(hostWebRoot, siteFolder);
        VerifyIndexFile(publishedWebRoot);

        if (DirectoriesOverlap(publishedWebRoot, liveDirectory) ||
            DirectoriesOverlap(publishedWebRoot, workDirectory))
        {
            throw new ArgumentException(
                "The published output must be separate from the live and deployment folders.",
                nameof(publishedWebRoot));
        }

        using FileStream deploymentLock = OpenDeploymentLock(workDirectory);

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

        TryDeleteBuildDirectory(previousDirectory);
        TryDeleteBuildDirectory(Path.Combine(workDirectory, "disabled"));
    }

    internal static async Task<bool> TakeDownAsync(StaticSiteModel site, string hostWebRoot)
    {
        StaticSitePathConfigurator.ValidateFolder(site.Folder);
        var (liveDirectory, workDirectory) = GetSiteDirectories(hostWebRoot, site.Folder);

        using FileStream deploymentLock = OpenDeploymentLock(workDirectory);
        if (!Directory.Exists(liveDirectory))
        {
            return false;
        }

        string disabledDirectory = Path.Combine(workDirectory, "disabled");
        if (Directory.Exists(disabledDirectory))
        {
            Directory.Delete(disabledDirectory, recursive: true);
        }

        Directory.CreateDirectory(disabledDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(disabledDirectory, "deployment.txt"),
            GetDeploymentIdentity(site));

        // The working directory is beside the web root, outside the Docker bind mount.
        Directory.Move(liveDirectory, Path.Combine(disabledDirectory, "web"));
        return true;
    }

    internal static async Task<bool> TryRestoreAsync(StaticSiteModel site, string hostWebRoot)
    {
        StaticSitePathConfigurator.ValidateFolder(site.Folder);
        var (liveDirectory, workDirectory) = GetSiteDirectories(hostWebRoot, site.Folder);

        using FileStream deploymentLock = OpenDeploymentLock(workDirectory);
        string disabledDirectory = Path.Combine(workDirectory, "disabled");
        string savedBuild = Path.Combine(disabledDirectory, "web");
        string identityPath = Path.Combine(disabledDirectory, "deployment.txt");
        string workerPath = Path.Combine(savedBuild, "service-worker.js");

        if (Directory.Exists(liveDirectory) ||
            !File.Exists(Path.Combine(savedBuild, "index.html")) ||
            !File.Exists(identityPath) ||
            !File.Exists(workerPath) ||
            !string.Equals(
                await File.ReadAllTextAsync(identityPath),
                GetDeploymentIdentity(site),
                StringComparison.Ordinal))
        {
            return false;
        }

        // Guard against reusing a newer build whose version was never saved to the inventory.
        string expectedVersion = $"const version = '{site.Version}';";
        if (!File.ReadLines(workerPath).Any(line =>
            string.Equals(line.Trim(), expectedVersion, StringComparison.Ordinal)))
        {
            return false;
        }

        Directory.Move(savedBuild, liveDirectory);
        TryDeleteBuildDirectory(disabledDirectory);
        return true;
    }

    private static string GetDeploymentIdentity(StaticSiteModel site) =>
        $"{site.Version}{Environment.NewLine}{site.LastDeployedSourceFingerprint}";

    private static (string LiveDirectory, string WorkDirectory) GetSiteDirectories(
        string hostWebRoot, string siteFolder)
    {
        hostWebRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(hostWebRoot));

        if (!Directory.Exists(hostWebRoot))
        {
            throw new DirectoryNotFoundException(
                $"The host web root was not found: {hostWebRoot}");
        }

        DirectoryInfo hostDirectory = new(hostWebRoot);
        DirectoryInfo hostParent = hostDirectory.Parent
            ?? throw new ArgumentException(
                "The host web root must have a parent folder.", nameof(hostWebRoot));

        return (
            Path.Combine(hostWebRoot, siteFolder),
            Path.Combine(hostParent.FullName, $".{hostDirectory.Name}-deploy", siteFolder));
    }

    private static FileStream OpenDeploymentLock(string workDirectory)
    {
        Directory.CreateDirectory(workDirectory);
        return File.Open(
            Path.Combine(workDirectory, "deploy.lock"),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);
    }

    private static void TryDeleteBuildDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"Could not remove the old build folder '{directory}': {ex.Message}");
        }
    }

    private static Task CopyPublishedFilesAsync(
        string sourceDirectory,
        string destinationDirectory)
    {
        return LocalStaticSiteProcessRunner.RunAsync(
            "robocopy",
            sourceDirectory,
            [sourceDirectory, destinationDirectory, "/E", "/XJ", "/R:1", "/W:1"],
            highestSuccessfulExitCode: 7);
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
