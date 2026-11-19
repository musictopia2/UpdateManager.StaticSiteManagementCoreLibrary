namespace UpdateManager.StaticSiteManagementCoreLibrary;
public class PublicStaticSiteDeploymentManager(
    IStaticSitesContext context,
    IStaticSiteDeploymentGroupProvider groupProvider,
    StaticSiteVersionPreparationService preparation,
    StaticSiteSourceSubmissionService submission,
    IStaticSiteWorkflowDispatcher dispatcher,
    StaticSiteHostMembershipService hostMembership,
    StaticSiteDependencyUpdateService dependencies,
    StaticSitePackageInspector packageInspector,
    IRequiredPackagePublicationService packagePublication)
{
    public async Task RunAsync(
        CancellationToken cancellationToken = default)
    {
        var sites = await context.GetStaticSitesAsync();
        var groups = await groupProvider.GetDeploymentGroupsAsync();
        var groupsByName = groups.ToDictionary(
            group => group.Name, StringComparer.OrdinalIgnoreCase);
        HashSet<string> publicGroupNames = new(StringComparer.OrdinalIgnoreCase);

        foreach (string groupName in sites.Select(site => site.DeploymentGroup)
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!groupsByName.TryGetValue(groupName, out var group))
            {
                throw new InvalidOperationException(
                    $"Deployment group '{groupName}' is not configured.");
            }

            var settings = StaticSiteDeploymentGroupSettings.GetSettings(group);
            if (settings is LocalStaticSiteDeploymentSettings)
            {
                continue;
            }

            _ = StaticSiteDeploymentGroupSettings.GetPublicSettings(settings, group.Name);
            publicGroupNames.Add(groupName);
        }

        var activeSites = sites
            .Where(site => !site.Disabled && publicGroupNames.Contains(site.DeploymentGroup))
            .OrderBy(
                site => site.DisplayName,
                StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Update project references before inspecting source changes.
        foreach (var site in activeSites)
        {
            cancellationToken.ThrowIfCancellationRequested();

            bool changed = await dependencies.UpdateAsync(site);

            if (changed)
            {
                Console.WriteLine(
                    $"Updated dependencies: {site.DisplayName}");
            }
        }

        List<StaticSiteModel> preparedSites = [];

        foreach (var site in activeSites)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await preparation.PrepareAsync(site))
            {
                preparedSites.Add(site);
            }
        }

        // Only changed or unfinished submissions need packages.
        // Allocated versions remain pending if publication must be retried.
        if (preparedSites.Count > 0)
        {
            var packages =
                await packageInspector.GetRequiredManagedPackagesAsync(preparedSites);

            await packagePublication.EnsurePublishedAsync(packages, cancellationToken);
        }

        int submittedCount = 0;
        foreach (var site in preparedSites)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await submission.SubmitAsync(site, cancellationToken))
            {
                submittedCount++;
            }
        }

        // Publish membership after source submission, before requesting builds.
        // This also resumes host updates and handles disabled sites.
        int membershipUpdates =
            await hostMembership.SynchronizeAsync(cancellationToken);

        // Reload to include flags saved during source submission.
        sites = await context.GetStaticSitesAsync();

        var pendingGroups = sites
            .Where(site =>
                !site.Disabled &&
                site.DeploymentBuildPending &&
                publicGroupNames.Contains(site.DeploymentGroup))
            .GroupBy(
                site => site.DeploymentGroup,
                StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var group in pendingGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await dispatcher.DispatchAsync(
                group.Key,
                cancellationToken);

            // Clear only after GitHub accepts the workflow request.
            foreach (var site in group)
            {
                site.DeploymentBuildPending = false;

                await context.UpdateStaticSiteAsync(site);
            }
        }

        if (submittedCount == 0 &&
            pendingGroups.Count == 0 &&
            membershipUpdates == 0)
        {
            Console.WriteLine(
                "No changed sites or pending deployment builds.");

            return;
        }

        Console.WriteLine(
            $"Updated {membershipUpdates} host membership list(s). " +
            $"Submitted {submittedCount} site update(s). " +
            $"Requested {pendingGroups.Count} coordinator build(s).");
    }
}