namespace UpdateManager.StaticSiteManagementCoreLibrary;
public class StaticSiteHostMembershipService(
    IStaticSitesContext context,
    IStaticSiteDeploymentGroupProvider groupProvider,
    StaticSiteHostRegistryBuilder registryBuilder,
    IStaticSiteHostPublicationService publication,
    IStaticSiteHostCleanupService cleanup)
{
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

            var settings = StaticSiteDeploymentGroupSettings.GetSettings(configuration);
            if (settings is LocalStaticSiteDeploymentSettings)
            {
                continue;
            }

            var publicSettings =
                StaticSiteDeploymentGroupSettings.GetPublicSettings(settings, configuration.Name);

            ArgumentException.ThrowIfNullOrWhiteSpace(
                publicSettings.RepositoryDirectory);

            string registryPath = Path.Combine(
                publicSettings.RepositoryDirectory,
                "apps.txt");

            string desired =
                await registryBuilder.BuildAsync(members.Key);

            string current = File.Exists(registryPath)
                ? await File.ReadAllTextAsync(registryPath, cancellationToken)
                : "";

            HashSet<string> previousFolders = StaticSiteHostRegistryContent.ReadFolders(current);

            var removedSites = members
                .Where(site =>
                    site.Disabled &&
                    previousFolders.Contains(site.Folder))
                .ToList();

            // An untouched group containing only staged sites needs no list yet.
            bool emptyStagingGroup = string.IsNullOrWhiteSpace(current) &&
                StaticSiteHostRegistryContent.ReadFolders(desired).Count == 0;

            bool listChanged = !emptyStagingGroup && !string.Equals(
                current.ReplaceLineEndings("\n"),
                desired.ReplaceLineEndings("\n"),
                StringComparison.Ordinal);

            bool publicationPending =
                members.Any(site => site.HostUpdatePending);

            bool cleanupPending =
                members.Any(site => site.HostCleanupPending);

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
                        (needsCleanup && !site.HostCleanupPending))
                    {
                        site.HostUpdatePending = true;

                        if (needsCleanup)
                        {
                            site.HostCleanupPending = true;
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
            if (members.Any(site => site.HostCleanupPending))
            {
                await cleanup.ClearAsync(
                    members.Key,
                    cancellationToken);

                foreach (var site in members.Where(
                    site => site.HostCleanupPending))
                {
                    site.HostCleanupPending = false;
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