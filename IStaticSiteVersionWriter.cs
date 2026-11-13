namespace UpdateManager.StaticSiteManagementCoreLibrary;
public interface IStaticSiteVersionWriter
{
    Task WriteVersionAsync(string projectPath, string version);
}