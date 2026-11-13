namespace UpdateManager.StaticSiteManagementCoreLibrary;
public class StaticSiteDiscoveryService(
    IStaticSitesContext context,
    IStaticSiteDiscoveryHandler handler,
    IStaticSiteSetupHandler setupHandler,
    IStaticSitePreparationService preparation,
    IStaticSiteRepositoryService repositories)
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

        await repositories.EnsureRepositoryAsync(site.ProjectPath);

        await preparation.PrepareAsync(site);

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