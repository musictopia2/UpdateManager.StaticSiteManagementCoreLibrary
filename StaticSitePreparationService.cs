using System;
using System.Collections.Generic;
using System.Text;
namespace UpdateManager.StaticSiteManagementCoreLibrary;
public class StaticSitePreparationService(
    StaticSiteUpdateSupportInstaller updateSupport)
    : IStaticSitePreparationService
{
    public async Task PrepareAsync(StaticSiteModel site)
    {
        ArgumentNullException.ThrowIfNull(site);

        // Validates your template setup, installs update support,
        // and writes the C# and JavaScript versions.
        await updateSupport.InstallAsync(
            site.ProjectPath,
            site.Version);

        // Configures the published subfolder, manifest,
        // and local launch profiles.
        await StaticSitePathConfigurator.ConfigureAsync(
            site.ProjectPath,
            site.Folder,
            site.DisplayName);
    }
}