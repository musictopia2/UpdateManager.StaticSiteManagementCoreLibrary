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
        bool hasChanges = false;

        foreach (var site in disabledSites)
        {
            if (await LocalStaticSiteHostDeploymentService.TakeDownAsync(site, hostWebRoot))
            {
                hasChanges = true;
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
                hasChanges = true;
                Console.WriteLine($"Updated apps.txt for '{group.Name}' ({remainingSites} site(s)).");
            }
        }

        foreach (var site in enabledSites)
        {
            var result = await deployment.DeployWithResultAsync(
                site, local.StagingRoot, hostWebRoot);

            switch (result)
            {
                case EnumLocalStaticSiteDeploymentResult.Published:
                    hasChanges = true;
                    Console.WriteLine($"Deployed {site.DisplayName} at {site.Version} and saved its fingerprint.");
                    break;
                case EnumLocalStaticSiteDeploymentResult.Restored:
                    hasChanges = true;
                    Console.WriteLine($"Restored {site.DisplayName} at {site.Version}. Skipping publish.");
                    break;
            }
        }

        var (listedSites, registryChanged) =
            await LocalStaticSiteHostRegistryWriter.WriteAsync(hostWebRoot, enabledSites);

        if (registryChanged)
        {
            hasChanges = true;
            Console.WriteLine($"Updated apps.txt for '{group.Name}' ({listedSites} site(s)).");
        }

        var (landingPage, landingPageChanged) =
            await LocalStaticSiteHostRegistryWriter.WriteLandingPageAsync(hostWebRoot);

        if (landingPageChanged)
        {
            hasChanges = true;
            Console.WriteLine($"Updated /{landingPage} for '{group.Name}'.");
        }

        if (!hasChanges)
        {
            Console.WriteLine($"No changes for local group '{group.Name}'.");
        }
    }
}
