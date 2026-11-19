namespace UpdateManager.StaticSiteManagementCoreLibrary;
public sealed class LocalStaticSiteDeploymentService(
    IStaticSitesContext context,
    StaticSiteDependencyUpdateService dependencies)
{
    public async Task<bool> DeployIfChangedAsync(
        StaticSiteModel site,
        string stagingRoot,
        string hostWebRoot)
    {
        ArgumentNullException.ThrowIfNull(site);

        await dependencies.UpdateAsync(site);

        string sourceDirectory = Path.GetDirectoryName(site.ProjectPath)
            ?? throw new InvalidOperationException(
                $"Could not determine the project folder for '{site.DisplayName}'.");

        string currentFingerprint =
            await LocalStaticSiteFingerprint.ComputeAsync(sourceDirectory);

        bool sourceMatches = string.Equals(
            currentFingerprint,
            site.LastDeployedSourceFingerprint,
            StringComparison.Ordinal);

        string deploymentVersion = sourceMatches
            ? site.Version
            : StaticSiteVersionPreparationService.GetNextVersion(site.Version);

       

        // Write both version files in the original project before copying it.
        StaticSiteVersionWriter versionWriter = new();
        await versionWriter.WriteVersionAsync(site.ProjectPath, deploymentVersion);

        string liveIndexPath = Path.Combine(
            hostWebRoot, site.Folder, "index.html");

        if (sourceMatches && File.Exists(liveIndexPath))
        {
            string stampedFingerprint =
                await LocalStaticSiteFingerprint.ComputeAsync(sourceDirectory);

            if (string.Equals(
                stampedFingerprint,
                site.LastDeployedSourceFingerprint,
                StringComparison.Ordinal))
            {
                return false;
            }
        }

        var (publishedWebRoot, deployedFingerprint) =
            await LocalStaticSitePublishService.PublishAsync(site, stagingRoot);

        await LocalStaticSiteHostDeploymentService.DeployAsync(
            publishedWebRoot, hostWebRoot, site.Folder);

        site.Version = deploymentVersion;
        site.LastDeployedSourceFingerprint = deployedFingerprint;
        await context.UpdateStaticSiteAsync(site);

        return true;
    }
}