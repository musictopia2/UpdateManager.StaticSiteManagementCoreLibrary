namespace UpdateManager.StaticSiteManagementCoreLibrary;

public class StaticSitePackageInspector(
    IStaticSitesContext sitesContext,
    IPackagesContext packagesContext)
{
    public async Task<BasicList<StaticSitePackageReferenceModel>> InspectAsync()
    {
        var sites = await sitesContext.GetStaticSitesAsync();
        return await InspectAsync(sites);
    }

    public async Task<BasicList<StaticSitePackageReferenceModel>> InspectAsync(
        IEnumerable<StaticSiteModel> sites)
    {
        ArgumentNullException.ThrowIfNull(sites);

        var packages = await packagesContext.GetPackagesAsync();
        BasicList<StaticSitePackageReferenceModel> output = [];

        foreach (var site in sites
            .Where(item => item.Disabled == false)
            .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            var document = XDocument.Load(site.ProjectPath);

            var references = document.Descendants()
                .Where(element => element.Name.LocalName == "PackageReference");

            foreach (var reference in references)
            {
                string? packageId = reference.Attribute("Include")?.Value;

                if (string.IsNullOrWhiteSpace(packageId))
                {
                    continue;
                }

                string version = reference.Attribute("Version")?.Value
                    ?? reference.Elements()
                        .FirstOrDefault(element => element.Name.LocalName == "Version")?.Value
                    ?? "";

                var managedPackage = packages.FirstOrDefault(package =>
                    string.Equals(
                        package.PackageID, packageId, StringComparison.OrdinalIgnoreCase));

                output.Add(new()
                {
                    ProjectPath = site.ProjectPath,
                    PackageId = packageId,
                    ReferencedVersion = version,
                    ManagedPackage = managedPackage
                });
            }
        }

        return output;
    }

    public async Task<BasicList<NuGetPackageModel>> GetRequiredManagedPackagesAsync()
    {
        var sites = await sitesContext.GetStaticSitesAsync();
        return await GetRequiredManagedPackagesAsync(sites);
    }

    public async Task<BasicList<NuGetPackageModel>> GetRequiredManagedPackagesAsync(
        IEnumerable<StaticSiteModel> sites)
    {
        var references = await InspectAsync(sites);
        var packages = await packagesContext.GetPackagesAsync();

        BasicList<NuGetPackageModel> output = [];
        HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase);
        Queue<NuGetPackageModel> pending = new();

        foreach (var reference in references)
        {
            if (reference.ManagedPackage is { } package)
            {
                pending.Enqueue(package);
            }
        }

        while (pending.TryDequeue(out var package))
        {
            if (!visited.Add(package.PackageID))
            {
                continue;
            }

            output.Add(package);

            if (!File.Exists(package.CsProjPath))
            {
                throw new FileNotFoundException(
                    $"Cannot inspect dependencies for {package.PackageID}.",
                    package.CsProjPath);
            }

            var document = XDocument.Load(package.CsProjPath);

            var dependencyIds = document.Descendants()
                .Where(element => element.Name.LocalName == "PackageReference")
                .Select(element => element.Attribute("Include")?.Value)
                .Where(id => !string.IsNullOrWhiteSpace(id));

            foreach (string? dependencyId in dependencyIds)
            {
                var dependency = packages.FirstOrDefault(item =>
                    string.Equals(
                        item.PackageID, dependencyId, StringComparison.OrdinalIgnoreCase));

                if (dependency is not null)
                {
                    pending.Enqueue(dependency);
                }
            }
        }

        BasicList<NuGetPackageModel> sorted = [];

        foreach (var package in output.OrderBy(
            item => item.PackageID, StringComparer.OrdinalIgnoreCase))
        {
            sorted.Add(package);
        }

        return sorted;
    }
}