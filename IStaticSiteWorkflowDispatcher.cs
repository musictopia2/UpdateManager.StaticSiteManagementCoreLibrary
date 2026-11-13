namespace UpdateManager.StaticSiteManagementCoreLibrary;
public interface IStaticSiteWorkflowDispatcher
{
    Task DispatchAsync(
        string deploymentGroup,
        CancellationToken cancellationToken = default);
}