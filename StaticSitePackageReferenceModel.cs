namespace UpdateManager.StaticSiteManagementCoreLibrary;
public class StaticSitePackageReferenceModel
{
    public string ProjectPath { get; set; } = "";

    public string PackageId { get; set; } = "";

    public string ReferencedVersion { get; set; } = "";

    // Null means this package was not found in our managed inventory.
    public NuGetPackageModel? ManagedPackage { get; set; }
}