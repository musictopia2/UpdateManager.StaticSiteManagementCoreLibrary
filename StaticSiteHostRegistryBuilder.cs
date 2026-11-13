using System.Text;
namespace UpdateManager.StaticSiteManagementCoreLibrary;
public class StaticSiteHostRegistryBuilder(
    IStaticSitesContext context,
    IStaticSiteRepositoryHandler repositoryHandler)
{
    public async Task<string> BuildAsync(string deploymentGroup)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deploymentGroup);

        var sites = await context.GetStaticSitesAsync();

        var selected = sites
            .Where(site =>
                !site.Disabled &&
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

        if (selected.Count == 0)
        {
            throw new InvalidOperationException(
                $"No sites are registered for '{deploymentGroup}'.");
        }

        

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