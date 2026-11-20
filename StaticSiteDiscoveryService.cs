namespace UpdateManager.StaticSiteManagementCoreLibrary;
public class StaticSiteDiscoveryService(
    IStaticSitesContext context,
    IStaticSiteDiscoveryHandler handler,
    IStaticSiteSetupHandler setupHandler,
    IStaticSitePreparationService preparation,
    IStaticSiteRepositoryService repositories,
    IStaticSiteDeploymentGroupProvider groupProvider,
    StaticSiteUpdateSupportInstaller updateSupport,
    StaticSiteLanLaunchSettings? lanLaunchSettings = null
    )
{
    public async Task AddStaticSiteAsync(StaticSiteModel site)
    {
        ArgumentNullException.ThrowIfNull(site);
        ArgumentException.ThrowIfNullOrWhiteSpace(site.ProjectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(site.DeploymentGroup);
        ArgumentException.ThrowIfNullOrWhiteSpace(site.Folder);
        ArgumentException.ThrowIfNullOrWhiteSpace(site.DisplayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(site.Version);

        site.ProjectPath = Path.GetFullPath(site.ProjectPath);

        var existing = await context.GetStaticSitesAsync();

        bool alreadyRegistered = existing.Any(item =>
            string.Equals(
                Path.GetFullPath(item.ProjectPath),
                site.ProjectPath,
                StringComparison.OrdinalIgnoreCase));

        if (alreadyRegistered)
        {
            throw new InvalidOperationException(
                $"Static site is already registered: {site.ProjectPath}");
        }

        if (await setupHandler.IsProjectConfiguredAsync(site.ProjectPath) == false)
        {
            throw new InvalidOperationException(
                $"Static-site setup validation failed: {site.ProjectPath}");
        }

        var settings = await GetDeploymentSettingsAsync(site.DeploymentGroup);

        if (settings is LocalStaticSiteDeploymentSettings)
        {
            await updateSupport.InstallAsync(site.ProjectPath, site.Version);
            await ApplyLanLaunchSettingsAsync(site.ProjectPath, settings);
            site.RegisteredSourceFingerprint = await LocalStaticSiteFingerprint.ComputeAsync(
                Path.GetDirectoryName(site.ProjectPath)!);
            await context.AddStaticSiteAsync(site);

            Console.WriteLine($"Registered local static site: {site.DisplayName}");
            return;
        }

        _ = StaticSiteDeploymentGroupSettings.GetPublicSettings(settings, site.DeploymentGroup);

        await repositories.EnsureRepositoryAsync(site.ProjectPath);

        await preparation.PrepareAsync(site);
        await ApplyLanLaunchSettingsAsync(site.ProjectPath, settings);

        await repositories.CommitAndPushAsync(
            site.ProjectPath,
            $"Prepare static site {site.Version}");

        var submitted = await StaticSiteChangeDetector.InspectAsync(
            site.ProjectPath,
            lastSubmittedCommit: "");

        if (submitted.HasUncommittedChanges)
        {
            throw new InvalidOperationException(
                $"Source changed during registration: {site.DisplayName}. " +
                "Registration was not saved.");
        }

        site.LastSubmittedCommit = submitted.CurrentCommit;
        site.SourceUpdatePending = false;
        site.DeploymentBuildPending = false;

        // Registration prepares source only. Deployment adds host membership.
        site.HostUpdatePending = false;

        await context.AddStaticSiteAsync(site);

        Console.WriteLine($"Registered static site: {site.DisplayName}");
    }

    public async Task RefreshLanLaunchProfileAsync(string projectPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        if (lanLaunchSettings is null)
        {
            throw new InvalidOperationException("LAN launch settings were not registered.");
        }

        projectPath = Path.GetFullPath(projectPath);
        var sites = await context.GetStaticSitesAsync();
        var site = sites.SingleOrDefault(item => string.Equals(
            Path.GetFullPath(item.ProjectPath), projectPath, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Static site is not registered: {projectPath}");
        if (!File.Exists(projectPath))
        {
            throw new FileNotFoundException("The static-site project was not found.", projectPath);
        }

        var settings = await GetDeploymentSettingsAsync(site.DeploymentGroup);
        await ApplyLanLaunchSettingsAsync(projectPath, settings);
    }

    private async Task<StaticSiteDeploymentSettings> GetDeploymentSettingsAsync(string deploymentGroup)
    {
        var groups = await groupProvider.GetDeploymentGroupsAsync();

        var group = groups.SingleOrDefault(item => string.Equals(
            item.Name,
            deploymentGroup,
            StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"Deployment group '{deploymentGroup}' is not configured.");

        return StaticSiteDeploymentGroupSettings.GetSettings(group);
    }

    private async Task ApplyLanLaunchSettingsAsync(
        string projectPath,
        StaticSiteDeploymentSettings settings)
    {
        if (lanLaunchSettings is null)
        {
            return;
        }

        if (settings is PublicStaticSiteDeploymentSettings)
        {
            await StaticSiteRepositoryService.EnsureUserFileIgnoredAsync(projectPath);
        }

        await StaticSitePathConfigurator.ConfigureLanLaunchProfileAsync(projectPath, lanLaunchSettings);
    }

    public async Task<BasicList<StaticSiteModel>> DiscoverMissingStaticSitesAsync()
    {
        var existing = await context.GetStaticSitesAsync();

        HashSet<string> knownPaths = new(
            existing.Select(x => Path.GetFullPath(x.ProjectPath)),
            StringComparer.OrdinalIgnoreCase);

        BasicList<StaticSiteModel> output = [];
        var roots = await handler.GetStaticSiteDirectoriesAsync();

        foreach (string root in roots)
        {
            if (Directory.Exists(root) == false)
            {
                throw new DirectoryNotFoundException(
                    $"Static-site scan directory was not found: {root}");
            }

            foreach (string project in GetProjectFiles(root))
            {
                string projectPath = Path.GetFullPath(project);

                if (knownPaths.Contains(projectPath))
                {
                    continue;
                }

                if (IsStandalonePwa(projectPath) == false)
                {
                    continue;
                }

                if (handler.CanIncludeProject(projectPath) == false)
                {
                    continue;
                }

                StaticSiteModel site = new()
                {
                    ProjectPath = projectPath
                };

                handler.CustomizeStaticSiteModel(site);

                // Discovery controls the identity.
                site.ProjectPath = projectPath;
                site.Version = $"{bb1.Configuration!.NetVersion}.0";
                output.Add(site);
                knownPaths.Add(projectPath);
            }
        }

        return output;
    }

    private static IEnumerable<string> GetProjectFiles(string directory)
    {
        foreach (string project in Directory.EnumerateFiles(
            directory, "*.csproj", SearchOption.TopDirectoryOnly))
        {
            yield return project;
        }

        foreach (string child in Directory.EnumerateDirectories(directory))
        {
            string name = Path.GetFileName(child);

            if (name.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith('.') ||
                (File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
            {
                continue;
            }

            foreach (string project in GetProjectFiles(child))
            {
                yield return project;
            }
        }
    }

    private static bool IsStandalonePwa(string projectPath)
    {
        XElement project = XDocument.Load(projectPath).Root
            ?? throw new InvalidOperationException(
                $"Project XML has no root: {projectPath}");

        string sdk = project.Attribute("Sdk")?.Value ?? "";

        if (sdk.Equals(
            "Microsoft.NET.Sdk.BlazorWebAssembly",
            StringComparison.OrdinalIgnoreCase) == false)
        {
            return false;
        }

        return project.Descendants().Any(x =>
            x.Name.LocalName == "ServiceWorker" &&
            string.IsNullOrWhiteSpace(
                x.Attribute("Include")?.Value) == false);
    }
}
