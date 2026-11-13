namespace UpdateManager.StaticSiteManagementCoreLibrary;
public class StaticSiteDeploymentManager(
    IStaticSitesContext context,
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
        int membershipUpdates =
            await hostMembership.SynchronizeAsync(cancellationToken);

        // Membership synchronization may have changed saved flags.
        var sites = await context.GetStaticSitesAsync();

        var activeSites = sites
            .Where(site => !site.Disabled)
            .OrderBy(
                site => site.DisplayName,
                StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (activeSites.Any(site => site.HostUpdatePending))
        {
            throw new InvalidOperationException(
                "There are unfinished host registrations. " +
                "Run the registration app before deploying updates.");
        }

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

        if (activeSites.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Inspect after updates so selection reflects current references.
            var packages =
                await packageInspector.GetRequiredManagedPackagesAsync();

            await packagePublication.EnsurePublishedAsync(
                packages,
                cancellationToken);
        }

        int submittedCount = 0;

        foreach (var site in activeSites)
        {
            cancellationToken.ThrowIfCancellationRequested();

            bool prepared = await preparation.PrepareAsync(site);

            if (!prepared)
            {
                continue;
            }

            bool submitted = await submission.SubmitAsync(
                site,
                cancellationToken);

            if (submitted)
            {
                submittedCount++;
            }
        }

        // Reload to include flags saved during source submission.
        sites = await context.GetStaticSitesAsync();

        var pendingGroups = sites
            .Where(site =>
                !site.Disabled &&
                site.DeploymentBuildPending)
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