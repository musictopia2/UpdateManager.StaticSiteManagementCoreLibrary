namespace UpdateManager.StaticSiteManagementCoreLibrary;
public class StaticSiteDependencyUpdateService(
    IPackagesContext packagesContext)
{
    public async Task<bool> UpdateAsync(StaticSiteModel site)
    {
        ArgumentNullException.ThrowIfNull(site);

        if (site.Disabled)
        {
            return false;
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(site.ProjectPath);

        var packages = await packagesContext.GetPackagesAsync();

        CsProjEditor editor = new(site.ProjectPath);

        bool successful =
            await editor.UpdateDependenciesAsync(packages);

        if (successful == false)
        {
            throw new InvalidOperationException(
                $"Could not update dependencies for '{site.DisplayName}'.");
        }
        bool changed = editor.AnyUpdate;
        editor.SaveChanges();
        return changed;
    }
}