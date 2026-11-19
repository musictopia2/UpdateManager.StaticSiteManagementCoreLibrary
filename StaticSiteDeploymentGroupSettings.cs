namespace UpdateManager.StaticSiteManagementCoreLibrary;

internal static class StaticSiteDeploymentGroupSettings
{
    public static StaticSiteDeploymentSettings GetSettings(StaticSiteDeploymentGroupModel group)
    {
        StaticSiteDeploymentSettings? settings = group.Settings;
        return settings ?? throw new InvalidOperationException(
            $"Deployment group '{group.Name}' needs deployment settings.");
    }

    public static PublicStaticSiteDeploymentSettings GetPublicSettings(
        StaticSiteDeploymentSettings settings,
        string groupName)
    {
        return settings is PublicStaticSiteDeploymentSettings publicSettings
            ? publicSettings
            : throw new InvalidOperationException(
                $"Deployment group '{groupName}' needs public deployment settings.");
    }

    public static async Task<PublicStaticSiteDeploymentSettings> GetPublicAsync(
        IStaticSiteDeploymentGroupProvider groupProvider,
        string deploymentGroup)
    {
        var groups = await groupProvider.GetDeploymentGroupsAsync();
        var group = groups.SingleOrDefault(group => string.Equals(
            group.Name, deploymentGroup, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"Deployment group '{deploymentGroup}' is not configured.");

        return GetPublicSettings(GetSettings(group), group.Name);
    }
}
