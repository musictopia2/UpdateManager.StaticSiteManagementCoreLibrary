namespace UpdateManager.StaticSiteManagementCoreLibrary;
public interface IStaticSiteHostCleanupService
{
    Task ClearAsync(
        string deploymentGroup,
        CancellationToken cancellationToken = default);
}