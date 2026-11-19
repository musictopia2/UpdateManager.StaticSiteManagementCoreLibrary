using System.Text;
namespace UpdateManager.StaticSiteManagementCoreLibrary;
public class StaticSiteHostRegistryBuilder(
    IStaticSitesContext context,
    IStaticSiteRepositoryHandler repositoryHandler,
    IStaticSiteDeploymentGroupProvider groupProvider)
{
    public async Task<string> BuildAsync(string deploymentGroup)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deploymentGroup);

        var sites = await context.GetStaticSitesAsync();

        PublicStaticSiteDeploymentSettings settings =
            await StaticSiteDeploymentGroupSettings.GetPublicAsync(
                groupProvider, deploymentGroup);

        ArgumentException.ThrowIfNullOrWhiteSpace(settings.RepositoryDirectory);
        string registryPath = Path.Combine(settings.RepositoryDirectory, "apps.txt");
        string current = File.Exists(registryPath)
            ? await File.ReadAllTextAsync(registryPath)
            : "";
        var existingFolders = StaticSiteHostRegistryContent.ReadFolders(current);

        var selected = sites
            .Where(site =>
                !site.Disabled &&
                // Preserve existing members, including legacy .0 sites.
                // New sites qualify only after their first changed source is pushed.
                (existingFolders.Contains(site.Folder) ||
                    (!site.Version.EndsWith(".0", StringComparison.Ordinal) &&
                        !site.SourceUpdatePending) ||
                    site.DeploymentBuildPending) &&
                string.Equals(
                    site.DeploymentGroup,
                    deploymentGroup,
                    StringComparison.OrdinalIgnoreCase))
            .OrderBy(
                site => site.DisplayName,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                site => site.Folder,
                StringComparer.OrdinalIgnoreCase)
            .ToList();

        // A header-only list is valid when all sites are staged or disabled.
        HashSet<string> folders =
            new(StringComparer.OrdinalIgnoreCase);

        StringBuilder output = new();

        output.AppendLine("Repository\tProject\tFolder\tDisplayName");

        foreach (var site in selected)
        {
            if (string.IsNullOrWhiteSpace(site.Folder) ||
                !folders.Add(site.Folder))
            {
                throw new InvalidOperationException(
                    $"Missing or duplicate site folder: {site.Folder}");
            }

            string repository =
                $"{repositoryHandler.Owner}/" +
                repositoryHandler.GetRepositoryName(site.ProjectPath);

            string[] columns =
            [
                repository,
                Path.GetFileName(site.ProjectPath),
                site.Folder,
                site.DisplayName
            ];

            foreach (string column in columns)
            {
                if (string.IsNullOrWhiteSpace(column) ||
                    column.Contains('\t') ||
                    column.Contains('\r') ||
                    column.Contains('\n'))
                {
                    throw new InvalidOperationException(
                        $"Invalid host-list value for {site.ProjectPath}.");
                }
            }

            output.AppendLine(string.Join("\t", columns));
        }

        return output.ToString();
    }
}