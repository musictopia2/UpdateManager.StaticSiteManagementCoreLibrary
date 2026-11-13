namespace UpdateManager.StaticSiteManagementCoreLibrary;
public interface IStaticSiteHostPublicationService
{
    Task PublishAsync(
        string deploymentGroup,
        CancellationToken cancellationToken = default);
}