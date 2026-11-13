namespace UpdateManager.StaticSiteManagementCoreLibrary;
public class StaticSiteRegistrationManager(
    StaticSiteDiscoveryService discovery,
    IStaticSitesContext context,
    IStaticSiteHostPublicationService hostPublication)
{
    public async Task RunAsync(
        CancellationToken cancellationToken = default)
    {
        // Finish host updates left pending by an earlier run first.
        int resumedGroups =
            await PublishPendingHostsAsync(cancellationToken);

        var sites = await discovery.DiscoverMissingStaticSitesAsync();

        if (sites.Count == 0)
        {
            Console.WriteLine(
                resumedGroups == 0
                    ? "No new sites or pending host updates."
                    : "Pending host updates completed. No new sites.");

            return;
        }

        foreach (var site in sites)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Console.WriteLine($"Registering: {site.DisplayName}");

            await discovery.AddStaticSiteAsync(site);
        }

        await PublishPendingHostsAsync(cancellationToken);

        Console.WriteLine(
            $"Registered {sites.Count} new static site(s). " +
            "Coordinator changes have been pushed.");
    }

    private async Task<int> PublishPendingHostsAsync(
        CancellationToken cancellationToken)
    {
        var sites = await context.GetStaticSitesAsync();

        var pendingGroups = sites
            .Where(site => site.HostUpdatePending)
            .GroupBy(
                site => site.DeploymentGroup,
                StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var group in pendingGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ArgumentException.ThrowIfNullOrWhiteSpace(group.Key);

            Console.WriteLine($"Updating shared host: {group.Key}");

            await hostPublication.PublishAsync(
                group.Key,
                cancellationToken);

            // Clear flags only after the coordinator push succeeds.
            foreach (var site in group)
            {
                site.HostUpdatePending = false;
                await context.UpdateStaticSiteAsync(site);
            }
        }

        return pendingGroups.Count;
    }
}