namespace UpdateManager.StaticSiteManagementCoreLibrary;
public class StaticSiteRegistrationManager(
    StaticSiteDiscoveryService discovery)
{
    public async Task RunAsync(
        CancellationToken cancellationToken = default)
    {
        var sites = await discovery.DiscoverMissingStaticSitesAsync();

        if (sites.Count == 0)
        {
            Console.WriteLine("No new static sites.");
            return;
        }

        foreach (var site in sites)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Console.WriteLine($"Registering: {site.DisplayName}");
            await discovery.AddStaticSiteAsync(site);
        }

        Console.WriteLine(
            $"Registered {sites.Count} new static site(s). " +
            "Source is prepared and pushed. Host membership is unchanged.");
    }
}
