namespace UpdateManager.StaticSiteManagementCoreLibrary;
public interface IStaticSitePreparationService
{
    Task PrepareAsync(StaticSiteModel site);
}
