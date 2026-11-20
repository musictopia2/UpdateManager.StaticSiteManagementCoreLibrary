using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace UpdateManager.StaticSiteManagementCoreLibrary;

public static partial class StaticSitePathConfigurator
{
    public static async Task ConfigureAsync(
        string projectPath,
        string folder,
        string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ValidateFolder(folder);

        projectPath = Path.GetFullPath(projectPath);

        if (File.Exists(projectPath) == false)
        {
            throw new FileNotFoundException(
                "The static-site project was not found.",
                projectPath);
        }

        string directory = Path.GetDirectoryName(projectPath)!;
        string webroot = Path.Combine(directory, "wwwroot");

        string indexPath = Path.Combine(webroot, "index.html");
        string manifestPath = Path.Combine(webroot, "manifest.webmanifest");

        string index = await File.ReadAllTextAsync(indexPath);
        string manifestText = await File.ReadAllTextAsync(manifestPath);

        if (BaseTagPattern.Matches(index).Count != 1)
        {
            throw new InvalidOperationException(
                $"Expected exactly one base element with an href in {indexPath}.");
        }

        JsonObject manifest =
            JsonNode.Parse(manifestText) as JsonObject
            ?? throw new InvalidOperationException(
                $"Expected a JSON object in {manifestPath}.");

        string basePath = $"/{folder}/";

        string updatedIndex = BaseTagPattern.Replace(
            index,
            $"<base href=\"{basePath}\" />");

        manifest["name"] = displayName;
        manifest["short_name"] = displayName;
        manifest["id"] = basePath;
        manifest["start_url"] = basePath;
        manifest["scope"] = basePath;

        string updatedManifest = FormatJson(manifest);

        await WriteIfChangedAsync(indexPath, updatedIndex);
        await WriteIfChangedAsync(manifestPath, updatedManifest);
        await ConfigureLaunchSettingsAsync(directory, folder);
    }

    internal static async Task ConfigurePublicDevelopmentAsync(
        string projectPath,
        string folder,
        string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ValidateFolder(folder);

        string directory = Path.GetDirectoryName(Path.GetFullPath(projectPath))!;
        string manifestPath = Path.Combine(directory, "wwwroot", "manifest.webmanifest");
        JsonObject manifest = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath)) as JsonObject
            ?? throw new InvalidOperationException($"Expected a JSON object in {manifestPath}.");
        manifest["name"] = displayName;
        manifest["short_name"] = displayName;

        await WriteIfChangedAsync(manifestPath, FormatJson(manifest));
    }

    internal static async Task ConfigureLanLaunchProfileAsync(
        string projectPath,
        StaticSiteLanLaunchSettings launchSettings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(launchSettings.ApplicationUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(launchSettings.LaunchUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(launchSettings.CertificatePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(launchSettings.KeyPath);

        string directory = Path.GetDirectoryName(Path.GetFullPath(projectPath))!;
        string propertiesDirectory = Path.Combine(directory, "Properties");
        string path = Path.Combine(propertiesDirectory, "launchSettings.json");

        JsonObject settings = new()
        {
            ["$schema"] = "https://json.schemastore.org/launchsettings.json",
            ["profiles"] = new JsonObject
            {
                ["lan-https"] = new JsonObject
                {
                    ["commandName"] = "Project",
                    ["dotnetRunMessages"] = true,
                    ["launchBrowser"] = false,
                    ["launchUrl"] = "$(StaticSiteLanLaunchUrl)",
                    ["applicationUrl"] = "$(StaticSiteLanApplicationUrl)",
                    ["environmentVariables"] = new JsonObject
                    {
                        ["ASPNETCORE_ENVIRONMENT"] = "Development",
                        ["Kestrel__Certificates__Default__Path"] = "$(StaticSiteLanCertificatePath)",
                        ["Kestrel__Certificates__Default__KeyPath"] = "$(StaticSiteLanKeyPath)"
                    }
                }
            }
        };

        await WriteLanUserPropertiesAsync(projectPath + ".user", launchSettings);
        Directory.CreateDirectory(propertiesDirectory);
        await WriteIfChangedAsync(path, FormatJson(settings));
    }

    private static async Task WriteLanUserPropertiesAsync(
        string path,
        StaticSiteLanLaunchSettings settings)
    {
        string? existing = await ReadIfExistsAsync(path);
        XDocument project = existing is null
            ? new XDocument(new XElement("Project"))
            : XDocument.Parse(existing);
        XElement root = project.Root!;
        XNamespace ns = root.Name.Namespace;
        if (root.Name.LocalName != "Project")
        {
            throw new InvalidOperationException($"Expected an MSBuild Project element in {path}.");
        }

        const string label = "StaticSiteLanLaunch";
        XElement properties = new(ns + "PropertyGroup",
            new XAttribute("Label", label),
            new XElement(ns + "ActiveDebugProfile", "lan-https"),
            new XElement(ns + "StaticSiteLanApplicationUrl", EscapeMSBuildValue(settings.ApplicationUrl)),
            new XElement(ns + "StaticSiteLanLaunchUrl", EscapeMSBuildValue(settings.LaunchUrl)),
            new XElement(ns + "StaticSiteLanCertificatePath", EscapeMSBuildValue(settings.CertificatePath)),
            new XElement(ns + "StaticSiteLanKeyPath", EscapeMSBuildValue(settings.KeyPath)));
        root.Elements(ns + "PropertyGroup")
            .Where(group => (string?)group.Attribute("Label") == label)
            .Remove();
        root.Add(properties);
        await WriteIfChangedAsync(path, project.ToString() + Environment.NewLine);
    }

    private static string EscapeMSBuildValue(string value) => value
        .Replace("%", "%25")
        .Replace("$", "%24")
        .Replace("@", "%40")
        .Replace(";", "%3B")
        .Replace("(", "%28")
        .Replace(")", "%29")
        .Replace("'", "%27")
        .Replace("*", "%2A")
        .Replace("?", "%3F");

    internal static void ValidateFolder(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        if (!FolderPattern.IsMatch(folder))
        {
            throw new ArgumentException(
                "Use lowercase letters, numbers, and separating hyphens.",
                nameof(folder));
        }
    }

    private static async Task ConfigureLaunchSettingsAsync(
    string directory,
    string folder)
    {
        string path = Path.Combine(
            directory,
            "Properties",
            "launchSettings.json");

        JsonObject settings = await ReadLaunchSettingsAsync(path);

        JsonObject profiles = settings["profiles"] as JsonObject
            ?? throw new InvalidOperationException(
                $"Launch profiles were not found in {path}.");

        int updatedProfiles = 0;

        foreach (var entry in profiles)
        {
            if (entry.Value is not JsonObject profile)
            {
                continue;
            }

            if (profile["commandName"]?.GetValue<string>() != "Project")
            {
                continue;
            }

            string arguments =
                profile["commandLineArgs"]?.GetValue<string>() ?? "";

            arguments = PathBaseArgumentPattern.Replace(
                arguments,
                "").Trim();

            if (string.IsNullOrWhiteSpace(arguments))
            {
                profile.Remove("commandLineArgs");
            }
            else
            {
                profile["commandLineArgs"] = arguments;
            }

            JsonObject environment;

            if (profile["environmentVariables"] is null)
            {
                environment = new JsonObject();
                profile["environmentVariables"] = environment;
            }
            else
            {
                environment =
                    profile["environmentVariables"] as JsonObject
                    ?? throw new InvalidOperationException(
                        $"Invalid environmentVariables in profile '{entry.Key}'.");
            }

            environment["Gateway__PathBase"] = $"/{folder}";
            profile["launchUrl"] = $"{folder}/";

            updatedProfiles++;
        }

        if (updatedProfiles == 0)
        {
            throw new InvalidOperationException(
                $"No Project launch profiles were found in {path}.");
        }

        await WriteIfChangedAsync(path, FormatJson(settings));
    }

    private static async Task<JsonObject> ReadLaunchSettingsAsync(string path)
    {
        string content = await File.ReadAllTextAsync(path);

        return JsonNode.Parse(
            content,
            documentOptions: new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            }) as JsonObject
            ?? throw new InvalidOperationException(
                $"Expected a JSON object in {path}.");
    }

    [GeneratedRegex(
    """(?:^|\s)--(?:Gateway:)?pathbase(?:=(?:"[^"]*"|'[^']*'|\S+)|\s+(?:"[^"]*"|'[^']*'|\S+))""",
    RegexOptions.IgnoreCase)]
    private static partial Regex PathBaseArgumentPattern { get; }

    private static string FormatJson(JsonObject value)
    {
        using MemoryStream stream = new();

        using (Utf8JsonWriter writer = new(
            stream,
            new JsonWriterOptions { Indented = true }))
        {
            value.WriteTo(writer);
        }

        return Encoding.UTF8.GetString(stream.ToArray())
            + Environment.NewLine;
    }

    private static async Task WriteIfChangedAsync(
        string path,
        string content)
    {
        if (await ReadIfExistsAsync(path) != content)
        {
            await File.WriteAllTextAsync(path, content);
        }
    }

    private static async Task<string?> ReadIfExistsAsync(string path)
    {
        try
        {
            return await File.ReadAllTextAsync(path);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"\A[a-z0-9]+(?:-[a-z0-9]+)*\z")]
    private static partial Regex FolderPattern { get; }

    [GeneratedRegex(
        """<base\b[^>]*\bhref\s*=\s*["'][^"']*["'][^>]*>""",
        RegexOptions.IgnoreCase)]
    private static partial Regex BaseTagPattern { get; }
}