namespace UpdateManager.StaticSiteManagementCoreLibrary;
public class StaticSiteSourceSubmissionService(
    IStaticSitesContext context,
    IStaticSiteRepositoryService repositories)
{
    public async Task<bool> SubmitAsync(
        StaticSiteModel site,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(site);

        if (site.Disabled || !site.SourceUpdatePending)
        {
            return false;
        }

        await repositories.CommitAndPushAsync(
            site.ProjectPath,
            $"Update static site to {site.Version}",
            cancellationToken);

        var result = await StaticSiteChangeDetector.InspectAsync(
            site.ProjectPath,
            site.LastSubmittedCommit,
            cancellationToken);

        if (result.HasUncommittedChanges)
        {
            throw new InvalidOperationException(
                $"Source changed during submission: {site.DisplayName}. " +
                "The update remains pending.");
        }

        site.LastSubmittedCommit = result.CurrentCommit;
        site.SourceUpdatePending = false;
        site.DeploymentBuildPending = true;

        await context.UpdateStaticSiteAsync(site);

        Console.WriteLine(
            $"Submitted {site.DisplayName} version {site.Version}.");

        return true;
    }
}