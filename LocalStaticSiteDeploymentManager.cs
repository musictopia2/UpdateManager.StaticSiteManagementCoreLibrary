namespace UpdateManager.StaticSiteManagementCoreLibrary;

public sealed class LocalStaticSiteDeploymentManager(
    IStaticSitesContext context,
    IStaticSiteDeploymentGroupProvider groupProvider,
    LocalStaticSiteDeploymentService deployment)
{
    public async Task RunAsync(string groupName)
    {
        var groups = await groupProvider.GetDeploymentGroupsAsync();

        var group = groups.SingleOrDefault(group => string.Equals(
            group.Name, groupName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"Deployment group '{groupName}' is not configured.");

        StaticSiteDeploymentSettings? settings = group.Settings;

        if (!settings.HasValue ||
            settings.Value is not LocalStaticSiteDeploymentSettings local)
        {
            throw new InvalidOperationException(
                $"Deployment group '{group.Name}' needs local Docker settings.");
        }

        var sites = await context.GetStaticSitesAsync();

        var groupSites = sites
            .Where(site => string.Equals(
                site.DeploymentGroup, group.Name, StringComparison.OrdinalIgnoreCase))
            .OrderBy(site => site.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        string hostWebRoot = await DockerBindMountResolver.GetWebRootAsync(local);
        DirectoryInfo hostDirectory = new(hostWebRoot);
        string releasesRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(local.ExpectedReleasesRoot));

        if (!string.Equals(hostDirectory.Name, "web", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                hostDirectory.Parent?.Parent?.FullName,
                releasesRoot,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Docker is serving from an unexpected folder: {hostWebRoot}");
        }

        var disabledSites = groupSites.Where(site => site.Disabled).ToList();
        var enabledSites = groupSites.Where(site => !site.Disabled).ToList();

        foreach (var site in disabledSites)
        {
            if (await LocalStaticSiteHostDeploymentService.TakeDownAsync(site, hostWebRoot))
            {
                Console.WriteLine($"Took {site.DisplayName} offline.");
            }
        }

        // Publish removals before an enabled site's build can fail.
        if (disabledSites.Count > 0)
        {
            var (remainingSites, removedFromRegistry) =
                await LocalStaticSiteHostRegistryWriter.WriteAsync(hostWebRoot, enabledSites);

            if (removedFromRegistry)
            {
                Console.WriteLine($"Updated apps.txt for '{group.Name}' ({remainingSites} site(s)).");
            }
        }

        foreach (var site in enabledSites)
        {
            var result = await deployment.DeployWithResultAsync(
                site, local.StagingRoot, hostWebRoot);

            Console.WriteLine(result switch
            {
                EnumLocalStaticSiteDeploymentResult.Published =>
                    $"Deployed {site.DisplayName} at {site.Version} and saved its fingerprint.",
                EnumLocalStaticSiteDeploymentResult.Restored =>
                    $"Restored {site.DisplayName} at {site.Version}. Skipping publish.",
                _ => $"No changes for {site.DisplayName} at {site.Version}. Skipping deployment."
            });
        }

        var (listedSites, registryChanged) =
            await LocalStaticSiteHostRegistryWriter.WriteAsync(hostWebRoot, enabledSites);

        Console.WriteLine(registryChanged
            ? $"Updated apps.txt for '{group.Name}' ({listedSites} site(s))."
            : $"apps.txt for '{group.Name}' is current ({listedSites} site(s)).");

        var (landingPage, landingPageChanged) =
            await LocalStaticSiteHostRegistryWriter.WriteLandingPageAsync(hostWebRoot);

        Console.WriteLine(landingPageChanged
            ? $"Updated /{landingPage} for '{group.Name}'."
            : $"/{landingPage} for '{group.Name}' is current.");
    }
}