namespace UpdateManager.StaticSiteManagementCoreLibrary;
public class StaticSiteChangeResult
{
    public string CurrentCommit { get; init; } = "";

    public bool BaselineRequired { get; init; }

    public bool HasUncommittedChanges { get; init; }

    public bool HasCommittedChanges { get; init; }

    public bool HasChanges =>
        HasUncommittedChanges || HasCommittedChanges;
}