namespace UpdateManager.StaticSiteManagementCoreLibrary;

public class CombinedStaticSiteDeploymentManager(
    IStaticSiteDeploymentGroupProvider groupProvider,
    LocalStaticSiteDeploymentManager localDeployment,
    PublicStaticSiteDeploymentManager publicDeployment)
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var groups = await groupProvider.GetDeploymentGroupsAsync();

        foreach (var group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var settings = StaticSiteDeploymentGroupSettings.GetSettings(group);
            if (settings is not LocalStaticSiteDeploymentSettings)
            {
                continue;
            }

            Console.WriteLine($"Deploying local static sites for '{group.Name}'.");
            await localDeployment.RunAsync(group.Name);
        }

        cancellationToken.ThrowIfCancellationRequested();
        Console.WriteLine("Deploying public static sites.");
        await publicDeployment.RunAsync(cancellationToken);
    }
}
