using System.Text;

namespace UpdateManager.StaticSiteManagementCoreLibrary;

internal static class LocalStaticSiteHostRegistryWriter
{
    internal static async Task<(int ListedSites, bool Changed)> WriteAsync(
        string hostWebRoot,
        IEnumerable<StaticSiteModel> sites)
    {
        StringBuilder registry = new("Folder\tDisplayName\tPath\n");
        HashSet<string> folders = new(StringComparer.OrdinalIgnoreCase);

        foreach (var site in sites)
        {
            if (string.IsNullOrWhiteSpace(site.LastDeployedSourceFingerprint))
            {
                continue;
            }

            StaticSitePathConfigurator.ValidateFolder(site.Folder);

            string indexPath = Path.Combine(hostWebRoot, site.Folder, "index.html");
            if (!File.Exists(indexPath))
            {
                continue;
            }

            if (!folders.Add(site.Folder))
            {
                throw new InvalidOperationException(
                    $"Multiple deployed sites use the folder '{site.Folder}'.");
            }

            if (string.IsNullOrWhiteSpace(site.DisplayName) ||
                site.DisplayName.IndexOfAny(['\t', '\r', '\n']) >= 0)
            {
                throw new InvalidOperationException(
                    $"Invalid display name for static site '{site.Folder}'.");
            }

            registry.Append(site.Folder)
                .Append('\t')
                .Append(site.DisplayName)
                .Append("\t/")
                .Append(site.Folder)
                .Append("/\n");
        }

        string destination = Path.Combine(hostWebRoot, "apps.txt");
        string content = registry.ToString();

        if (File.Exists(destination) &&
            string.Equals(await File.ReadAllTextAsync(destination), content, StringComparison.Ordinal))
        {
            return (folders.Count, false);
        }

        await WriteAtomicallyAsync(destination, content);

        return (folders.Count, true);
    }

    internal static async Task<(string Page, bool Changed)> WriteLandingPageAsync(string hostWebRoot)
    {
        foreach (string page in new[] { "index.html", "sites.html" })
        {
            string destination = Path.Combine(hostWebRoot, page);
            string? existing = File.Exists(destination)
                ? await File.ReadAllTextAsync(destination)
                : null;

            if (existing is not null &&
                !existing.Contains(LocalStaticSiteLandingPage.Marker, StringComparison.Ordinal))
            {
                continue;
            }

            string content = LocalStaticSiteLandingPage.Content;
            if (string.Equals(existing, content, StringComparison.Ordinal))
            {
                return (page, false);
            }

            await WriteAtomicallyAsync(destination, content);
            return (page, true);
        }

        throw new InvalidOperationException(
            "The local web root already has a custom index.html and sites.html.");
    }

    private static async Task WriteAtomicallyAsync(string destination, string content)
    {
        string temporaryPath = destination + $".{Guid.NewGuid():N}.tmp";

        try
        {
            await File.WriteAllTextAsync(temporaryPath, content);
            File.Move(temporaryPath, destination, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }
}
