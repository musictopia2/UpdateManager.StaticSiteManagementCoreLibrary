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
        return await DeployWithResultAsync(site, stagingRoot, hostWebRoot) !=
            EnumLocalStaticSiteDeploymentResult.Unchanged;
    }

    internal async Task<EnumLocalStaticSiteDeploymentResult> DeployWithResultAsync(
        StaticSiteModel site,
        string stagingRoot,
        string hostWebRoot)
    {
        ArgumentNullException.ThrowIfNull(site);

        if (site.Disabled)
        {
            return EnumLocalStaticSiteDeploymentResult.Unchanged;
        }

        string sourceDirectory = Path.GetDirectoryName(site.ProjectPath)
            ?? throw new InvalidOperationException(
                $"Could not determine the project folder for '{site.DisplayName}'.");

        if (string.IsNullOrWhiteSpace(site.LastDeployedSourceFingerprint))
        {
            string registrationFingerprint =
                await LocalStaticSiteFingerprint.ComputeAsync(sourceDirectory);

            if (string.IsNullOrWhiteSpace(site.RegisteredSourceFingerprint))
            {
                // Older registrations did not keep a source baseline.
                site.RegisteredSourceFingerprint = registrationFingerprint;
                await context.UpdateStaticSiteAsync(site);
                Console.WriteLine($"Recorded a registration baseline for {site.DisplayName}.");
            }

            if (string.Equals(registrationFingerprint, site.RegisteredSourceFingerprint,
                StringComparison.Ordinal))
            {
                return EnumLocalStaticSiteDeploymentResult.Unchanged;
            }
        }

        await dependencies.UpdateAsync(site);

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

        if (sourceMatches)
        {
            string stampedFingerprint =
                await LocalStaticSiteFingerprint.ComputeAsync(sourceDirectory);

            if (string.Equals(
                stampedFingerprint,
                site.LastDeployedSourceFingerprint,
                StringComparison.Ordinal))
            {
                string liveIndexPath = Path.Combine(hostWebRoot, site.Folder, "index.html");

                if (File.Exists(liveIndexPath))
                {
                    return EnumLocalStaticSiteDeploymentResult.Unchanged;
                }

                if (await LocalStaticSiteHostDeploymentService.TryRestoreAsync(site, hostWebRoot))
                {
                    return EnumLocalStaticSiteDeploymentResult.Restored;
                }
            }
        }

        var (publishedWebRoot, deployedFingerprint) =
            await LocalStaticSitePublishService.PublishAsync(site, stagingRoot);

        await LocalStaticSiteHostDeploymentService.DeployAsync(
            publishedWebRoot, hostWebRoot, site.Folder);

        site.Version = deploymentVersion;
        site.LastDeployedSourceFingerprint = deployedFingerprint;
        await context.UpdateStaticSiteAsync(site);

        return EnumLocalStaticSiteDeploymentResult.Published;
    }
}

internal enum EnumLocalStaticSiteDeploymentResult
{
    Unchanged,
    Published,
    Restored
}
