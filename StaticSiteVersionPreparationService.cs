using System.Globalization;

namespace UpdateManager.StaticSiteManagementCoreLibrary;

public class StaticSiteVersionPreparationService(
    IStaticSitesContext context,
    IStaticSiteVersionWriter versionWriter)
{
    public async Task<bool> PrepareAsync(StaticSiteModel site)
    {
        ArgumentNullException.ThrowIfNull(site);

        if (site.Disabled)
        {
            return false;
        }

        if (!site.SourceUpdatePending)
        {
            var changes = await StaticSiteChangeDetector.InspectAsync(
                site.ProjectPath,
                site.LastSubmittedCommit);

            if (changes.BaselineRequired)
            {
                throw new InvalidOperationException(
                    $"A submission baseline is required: {site.DisplayName}");
            }

            if (!changes.HasChanges)
            {
                return false;
            }

            site.Version = GetNextVersion(site.Version);
            site.SourceUpdatePending = true;

            // Persist the allocated version before changing source files.
            await context.UpdateStaticSiteAsync(site);
        }

        // Retries write the existing pending version without incrementing.
        await versionWriter.WriteVersionAsync(
            site.ProjectPath,
            site.Version);

        Console.WriteLine(
            $"Prepared {site.DisplayName} version {site.Version}.");

        return true;
    }

    internal static string GetNextVersion(string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        string[] parts = version.Split('.');

        if (parts.Length != 2 ||
            !int.TryParse(
                parts[0],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int major) ||
            !int.TryParse(
                parts[1],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int minor))
        {
            throw new InvalidOperationException(
                $"Expected a two-part static-site version: {version}");
        }

        int nextMinor = checked(minor + 1);

        return FormattableString.Invariant($"{major}.{nextMinor}");
    }
}