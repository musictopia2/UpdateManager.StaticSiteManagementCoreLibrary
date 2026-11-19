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
            .Where(site => !site.Disabled && string.Equals(
                site.DeploymentGroup, group.Name, StringComparison.OrdinalIgnoreCase))
            .OrderBy(site => site.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (groupSites.Count == 0)
        {
            Console.WriteLine($"No enabled sites in '{group.Name}'.");
            return;
        }

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

        foreach (var site in groupSites)
        {
            bool deployed = await deployment.DeployIfChangedAsync(
                site, local.StagingRoot, hostWebRoot);

            Console.WriteLine(deployed
                ? $"Deployed {site.DisplayName} at {site.Version} and saved its fingerprint."
                : $"No changes for {site.DisplayName} at {site.Version}. Skipping deployment.");
        }
    }
}