namespace UpdateManager.StaticSiteManagementCoreLibrary;
public class StaticSiteUpdateSupportInstaller(
    IStaticSiteSetupHandler setupHandler,
    IStaticSiteVersionWriter versionWriter)
{
    public async Task InstallAsync(
        string projectPath,
        string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        projectPath = Path.GetFullPath(projectPath);

        if (await setupHandler.IsProjectConfiguredAsync(projectPath) == false)
        {
            throw new InvalidOperationException(
                $"The project does not satisfy the required setup: {projectPath}");
        }

        string directory = Path.GetDirectoryName(projectPath)!;
        string appPath = Path.Combine(directory, "App.razor");

        if (File.Exists(appPath) == false)
        {
            throw new FileNotFoundException(
                "App.razor was not found.",
                appPath);
        }

        string app = await File.ReadAllTextAsync(appPath);

        string component = PwaFileTemplates.CreateUpdateComponent(
            setupHandler.GetUpdateButtonMarkup());

        string webroot = Path.Combine(directory, "wwwroot");
        Directory.CreateDirectory(webroot);

        await WriteIfChangedAsync(
            Path.Combine(webroot, "serviceWorkerInterop.js"),
            PwaFileTemplates.ServiceWorkerInterop);

        await WriteIfChangedAsync(
            Path.Combine(webroot, "service-worker.published.js"),
            PwaFileTemplates.PublishedServiceWorker);

        await WriteIfChangedAsync(
            Path.Combine(directory, "StaticSiteUpdate.razor"),
            component);

        await versionWriter.WriteVersionAsync(projectPath, version);

        if (app.Contains(
            "<StaticSiteUpdate",
            StringComparison.Ordinal) == false)
        {
            string updatedApp =
                app.TrimEnd() +
                Environment.NewLine +
                "<StaticSiteUpdate />" +
                Environment.NewLine;

            await WriteIfChangedAsync(appPath, updatedApp);
        }
    }

    private static async Task WriteIfChangedAsync(
        string path,
        string content)
    {
        if (File.Exists(path))
        {
            string existing = await File.ReadAllTextAsync(path);

            if (existing == content)
            {
                return;
            }
        }

        await File.WriteAllTextAsync(path, content);
    }
}