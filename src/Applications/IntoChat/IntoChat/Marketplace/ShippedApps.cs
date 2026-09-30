using System.Text.Json;
using DigitalBrain.Apps;

namespace IntoChat.Marketplace;

// The apps IntoChat ships live as folders under src/Applications/IntoChat/Apps and are embedded here.
// app.json is the manifest, app.cs the script of a csharp app, every other file travels with the package.
internal sealed record ShippedApp(PackageId Package, PackageContent Content);

internal static class ShippedApps
{
    public const string Publisher = DigitalBrain.Assistant.ShippedPublisher.Id;
    private const string ResourcePrefix = "IntoChat.ShippedApps/";
    private const string ManifestFile = "app.json";
    private const string ScriptFile = "app.cs";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<ShippedApp> Load()
    {
        var assembly = typeof(ShippedApps).Assembly;
        var folders = new SortedDictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var resource in assembly.GetManifestResourceNames())
        {
            var path = resource.Replace('\\', '/');
            if (!path.StartsWith(ResourcePrefix, StringComparison.Ordinal)) { continue; }
            var relative = path[ResourcePrefix.Length..];
            var slash = relative.IndexOf('/', StringComparison.Ordinal);
            if (slash < 0) { continue; }
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            var folder = relative[..slash];
            if (!folders.TryGetValue(folder, out var files)) { folders[folder] = files = new(StringComparer.Ordinal); }
            files[relative[(slash + 1)..]] = reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal);
        }
        return [.. folders.Select(folder => ToApp(folder.Key, folder.Value))];
    }

    public static ShippedApp ToApp(string folder, IReadOnlyDictionary<string, string> files)
    {
        var manifestText = files.GetValueOrDefault(ManifestFile) ?? throw new InvalidDataException($"Shipped app {folder} has no {ManifestFile}.");
        var manifest = JsonSerializer.Deserialize<AppJson>(manifestText, Json) ?? throw new InvalidDataException($"{folder}/{ManifestFile} is empty.");
        var content = new PackageContent(
            new PackageManifest(
                manifest.Title,
                manifest.Description,
                [.. manifest.Operations.Select(operation => new PackageOperation(operation.Name, operation.Description))],
                [.. manifest.Settings.Select(setting => new PackageSetting(setting.Name, setting.Description, setting.Default))],
                Runtime: manifest.Runtime),
            files.GetValueOrDefault(ScriptFile) ?? "",
            files.Where(file => file.Key is not ManifestFile and not ScriptFile).ToDictionary(StringComparer.Ordinal));
        return new(PackageId.Create(Publisher, manifest.Name), content);
    }

    private sealed record AppJson(string Name, string Title, string Description, string Runtime, IReadOnlyList<OperationJson> Operations, IReadOnlyList<SettingJson> Settings);
    private sealed record OperationJson(string Name, string Description);
    private sealed record SettingJson(string Name, string Description, string Default);
}
