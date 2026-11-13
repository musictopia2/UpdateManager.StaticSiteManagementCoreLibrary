namespace UpdateManager.StaticSiteManagementCoreLibrary;
public interface IStaticSiteRepositoryService
{
    // Initializes or verifies the local and GitHub repositories.
    Task EnsureRepositoryAsync(
        string projectPath,
        CancellationToken cancellationToken = default);

    // Commits prepared source and pushes it to GitHub.
    Task CommitAndPushAsync(
        string projectPath,
        string commitMessage,
        CancellationToken cancellationToken = default);
}