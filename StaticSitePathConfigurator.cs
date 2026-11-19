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

        string content = await File.ReadAllTextAsync(path);

        JsonObject settings = JsonNode.Parse(
            content,
            documentOptions: new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            }) as JsonObject
            ?? throw new InvalidOperationException(
                $"Expected a JSON object in {path}.");

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
        string existing = await File.ReadAllTextAsync(path);

        if (existing != content)
        {
            await File.WriteAllTextAsync(path, content);
        }
    }

    [GeneratedRegex(@"\A[a-z0-9]+(?:-[a-z0-9]+)*\z")]
    private static partial Regex FolderPattern { get; }

    [GeneratedRegex(
        """<base\b[^>]*\bhref\s*=\s*["'][^"']*["'][^>]*>""",
        RegexOptions.IgnoreCase)]
    private static partial Regex BaseTagPattern { get; }
}