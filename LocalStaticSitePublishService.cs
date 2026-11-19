namespace UpdateManager.StaticSiteManagementCoreLibrary;

public static class LocalStaticSitePublishService
{
    public static Task<(string PublishedWebRoot, string SourceFingerprint)> PublishAsync(
        StaticSiteModel site,
        string stagingRoot)
    {
        return PublishAsync(site, stagingRoot, version: null);
    }

    internal static async Task<(string PublishedWebRoot, string SourceFingerprint)> PublishAsync(
        StaticSiteModel site,
        string stagingRoot,
        string? version)
    {
        ArgumentNullException.ThrowIfNull(site);
        ArgumentException.ThrowIfNullOrWhiteSpace(site.ProjectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(site.Folder);
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingRoot);

        string folder = site.Folder;

        if (folder is "." or ".." ||
            folder.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            folder.EndsWith('.') ||
            folder.EndsWith(' '))
        {
            throw new ArgumentException($"Invalid site folder: '{folder}'.", nameof(site));
        }

        string projectPath = Path.GetFullPath(site.ProjectPath);
        if (!File.Exists(projectPath))
        {
            throw new FileNotFoundException("The site project was not found.", projectPath);
        }

        stagingRoot = Path.GetFullPath(stagingRoot);

        string sourceDirectory = Path.GetDirectoryName(projectPath)!;
        string stagedDirectory = Path.Combine(stagingRoot, "Source", folder);
        string outputDirectory = Path.Combine(stagingRoot, "Current", folder);

        if (DirectoriesOverlap(sourceDirectory, stagedDirectory) ||
            DirectoriesOverlap(sourceDirectory, outputDirectory))
        {
            throw new ArgumentException(
                "The staging and output folders must be separate from the project source.",
                nameof(stagingRoot));
        }

        Directory.CreateDirectory(stagingRoot);

        using FileStream publishLock = File.Open(
            Path.Combine(stagingRoot, $"{folder}.publish.lock"),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);

        Console.WriteLine($"Publishing {site.DisplayName} for Docker.");
        DeleteDirectoryIfPresent(stagedDirectory);

        await LocalStaticSiteProcessRunner.RunAsync(
            "robocopy",
            sourceDirectory,
            [
                sourceDirectory, stagedDirectory,
                "/E", "/XJ", "/XD", "bin", "obj", ".vs", ".git", "/R:1", "/W:1"
            ],
            highestSuccessfulExitCode: 7);

        string sourceFingerprint =
            await LocalStaticSiteFingerprint.ComputeAsync(stagedDirectory);

        string stagedProjectPath = Path.Combine(
            stagedDirectory, Path.GetFileName(projectPath));

        if (version is not null)
        {
            StaticSiteVersionWriter versionWriter = new();
            await versionWriter.WriteVersionAsync(stagedProjectPath, version);
        }

        await StaticSitePathConfigurator.ConfigureAsync(
            stagedProjectPath, folder, site.DisplayName);

        DeleteDirectoryIfPresent(outputDirectory);

        await LocalStaticSiteProcessRunner.RunAsync(
            "dotnet",
            stagedDirectory,
            ["publish", stagedProjectPath, "-c", "Release", "-o", outputDirectory]);

        string publishedWebRoot = Path.Combine(outputDirectory, "wwwroot");
        string indexPath = Path.Combine(publishedWebRoot, "index.html");

        if (!File.Exists(indexPath))
        {
            throw new FileNotFoundException("Published index.html was missing.", indexPath);
        }

        return (publishedWebRoot, sourceFingerprint);
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

    private static void DeleteDirectoryIfPresent(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
