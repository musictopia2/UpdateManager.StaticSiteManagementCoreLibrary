namespace UpdateManager.StaticSiteManagementCoreLibrary;
public class StaticSiteHostMembershipService(
    IStaticSitesContext context,
    IStaticSiteDeploymentGroupProvider groupProvider,
    StaticSiteHostRegistryBuilder registryBuilder,
    IStaticSiteHostPublicationService publication,
    IStaticSiteHostCleanupService cleanup)
{
    private static HashSet<string> ReadFolders(string content)
    {
        HashSet<string> folders =
            new(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(content))
        {
            return folders;
        }

        string[] lines = content.Split('\n');

        string[] headers = lines[0]
            .TrimStart('\uFEFF')
            .TrimEnd('\r')
            .Split('\t');

        int folderIndex = Array.FindIndex(
            headers,
            header => string.Equals(
                header.Trim(),
                "Folder",
                StringComparison.OrdinalIgnoreCase));

        if (folderIndex < 0)
        {
            throw new InvalidOperationException(
                "The existing apps.txt has no Folder column.");
        }

        foreach (string line in lines.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            string[] columns = line.TrimEnd('\r').Split('\t');

            if (columns.Length != headers.Length)
            {
                throw new InvalidOperationException(
                    "The existing apps.txt contains an invalid row.");
            }

            string folder = columns[folderIndex].Trim();

            if (string.IsNullOrWhiteSpace(folder))
            {
                throw new InvalidOperationException(
                    "The existing apps.txt contains an empty Folder value.");
            }

            folders.Add(folder);
        }

        return folders;
    }
    public async Task<int> SynchronizeAsync(
    CancellationToken cancellationToken = default)
    {
        var sites = await context.GetStaticSitesAsync();
        var configuredGroups =
            await groupProvider.GetDeploymentGroupsAsync();

        var groups = sites
            .GroupBy(
                site => site.DeploymentGroup,
                StringComparer.OrdinalIgnoreCase)
            .ToList();

        int processedGroups = 0;

        foreach (var members in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var configuration = configuredGroups.SingleOrDefault(group =>
                string.Equals(
                    group.Name,
                    members.Key,
                    StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException(
                    $"Deployment group '{members.Key}' is not configured.");

            ArgumentException.ThrowIfNullOrWhiteSpace(
                configuration.RepositoryDirectory);

            string registryPath = Path.Combine(
                configuration.RepositoryDirectory,
                "apps.txt");

            string desired =
                await registryBuilder.BuildAsync(members.Key);

            string current = File.Exists(registryPath)
                ? await File.ReadAllTextAsync(registryPath)
                : "";

            HashSet<string> previousFolders = ReadFolders(current);

            var removedSites = members
                .Where(site =>
                    site.Disabled &&
                    previousFolders.Contains(site.Folder))
                .ToList();

            bool listChanged = !string.Equals(
                current.ReplaceLineEndings("\n"),
                desired.ReplaceLineEndings("\n"),
                StringComparison.Ordinal);

            bool publicationPending =
                members.Any(site => site.HostUpdatePending);

            bool cleanupPending =
                members.Any(site => site.RenderCacheClearPending);

            if (!listChanged && !publicationPending && !cleanupPending)
            {
                continue;
            }

            if (listChanged || publicationPending)
            {
                // Save requirements before replacing the existing host list.
                foreach (var site in members)
                {
                    bool needsCleanup = removedSites.Contains(site);

                    if (!site.HostUpdatePending ||
                        (needsCleanup && !site.RenderCacheClearPending))
                    {
                        site.HostUpdatePending = true;

                        if (needsCleanup)
                        {
                            site.RenderCacheClearPending = true;
                        }

                        await context.UpdateStaticSiteAsync(site);
                    }
                }

                await publication.PublishAsync(
                    members.Key,
                    cancellationToken);

                foreach (var site in members)
                {
                    site.HostUpdatePending = false;
                    await context.UpdateStaticSiteAsync(site);
                }
            }

            // Also handles retries where publication already succeeded.
            if (members.Any(site => site.RenderCacheClearPending))
            {
                await cleanup.ClearAsync(
                    members.Key,
                    cancellationToken);

                foreach (var site in members.Where(
                    site => site.RenderCacheClearPending))
                {
                    site.RenderCacheClearPending = false;
                    await context.UpdateStaticSiteAsync(site);
                }
            }

            processedGroups++;

            Console.WriteLine(
                $"Synchronized host membership: {members.Key}");
        }

        return processedGroups;
    }
}