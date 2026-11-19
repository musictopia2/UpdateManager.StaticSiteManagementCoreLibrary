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
        Dictionary<string, PublicStaticSiteDeploymentSettings> publicGroups =
            new(StringComparer.OrdinalIgnoreCase);

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

            publicGroups.Add(groupName,
                StaticSiteDeploymentGroupSettings.GetPublicSettings(settings, group.Name));
        }

        var activeSites = sites
            .Where(site => !site.Disabled && publicGroups.ContainsKey(site.DeploymentGroup))
            .OrderBy(
                site => site.DisplayName,
                StringComparer.OrdinalIgnoreCase)
            .ToList();

        activeSites = await GetSitesReadyForSourceUpdateAsync(
            activeSites, publicGroups, cancellationToken);

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
                publicGroups.ContainsKey(site.DeploymentGroup))
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

    private static async Task<List<StaticSiteModel>> GetSitesReadyForSourceUpdateAsync(
        IEnumerable<StaticSiteModel> sites,
        Dictionary<string, PublicStaticSiteDeploymentSettings> publicGroups,
        CancellationToken cancellationToken)
    {
        List<StaticSiteModel> readySites = [];

        foreach (var group in sites.GroupBy(
            site => site.DeploymentGroup, StringComparer.OrdinalIgnoreCase))
        {
            HashSet<string>? listedFolders = null;

            foreach (var site in group)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!site.SourceUpdatePending &&
                    !site.DeploymentBuildPending &&
                    site.Version.EndsWith(".0", StringComparison.Ordinal))
                {
                    if (listedFolders is null)
                    {
                        string registryPath = Path.Combine(
                            publicGroups[group.Key].RepositoryDirectory, "apps.txt");
                        string contents = File.Exists(registryPath)
                            ? await File.ReadAllTextAsync(registryPath, cancellationToken)
                            : "";
                        listedFolders = StaticSiteHostRegistryContent.ReadFolders(contents);
                    }

                    if (!listedFolders.Contains(site.Folder))
                    {
                        var changes = await StaticSiteChangeDetector.InspectAsync(
                            site.ProjectPath, site.LastSubmittedCommit, cancellationToken);
                        if (!changes.BaselineRequired && !changes.HasChanges)
                        {
                            continue;
                        }
                    }
                }

                readySites.Add(site);
            }
        }

        return readySites;
    }
}
