using System.Text;

namespace UpdateManager.StaticSiteManagementCoreLibrary;

public class StaticSiteHostRegistryWriter(
    StaticSiteHostRegistryBuilder builder,
    IStaticSiteDeploymentGroupProvider groupProvider)
{
    public async Task<string> WriteAsync(string deploymentGroup)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deploymentGroup);

        var groups = await groupProvider.GetDeploymentGroupsAsync();

        var group = groups.SingleOrDefault(item =>
            string.Equals(
                item.Name,
                deploymentGroup,
                StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"Deployment group '{deploymentGroup}' is not configured.");

        ArgumentException.ThrowIfNullOrWhiteSpace(
            group.RepositoryDirectory);

        string directory =
            Path.GetFullPath(group.RepositoryDirectory);

        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException(directory);
        }

        string scriptPath = Path.Combine(
            directory, "scripts", "build-site.py");

        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException(
                "The coordinator build script was not found.",
                scriptPath);
        }

        string content = await builder.BuildAsync(deploymentGroup);
        string destination = Path.Combine(directory, "apps.txt");

        if (File.Exists(destination))
        {
            string existing =
                await File.ReadAllTextAsync(destination);

            if (existing == content)
            {
                return destination;
            }
        }

        await File.WriteAllTextAsync(
            destination,
            content,
            new UTF8Encoding(false));

        return destination;
    }
}