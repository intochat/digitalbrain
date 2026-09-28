using System.Reflection;

namespace DigitalBrain.Apps.Manifests;

// The committed src/Apps/*/app.json files are the first-party manifests. They are embedded here so
// the launcher and discovery read the same generated declarations the repository checks in.
public static class FirstPartyApps
{
    private const string Prefix = "DigitalBrain.Apps.FirstParty.";

    private static readonly IReadOnlyList<AppManifest> Manifests = Load();

    public static IReadOnlyList<AppManifest> All() => Manifests;

    public static AppManifest Get(string id) =>
        Manifests.FirstOrDefault(manifest => string.Equals(manifest.Id, id, StringComparison.Ordinal))
        ?? throw new KeyNotFoundException($"No first-party app '{id}' is registered.");

    public static bool Contains(string id) =>
        Manifests.Any(manifest => string.Equals(manifest.Id, id, StringComparison.Ordinal));

    private static IReadOnlyList<AppManifest> Load()
    {
        var assembly = typeof(FirstPartyApps).Assembly;
        var manifests = new List<AppManifest>();
        foreach (var name in assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(Prefix, StringComparison.Ordinal) && name.EndsWith(".app.json", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"Missing embedded manifest '{name}'.");
            using var reader = new StreamReader(stream);
            var manifest = AppManifestJson.Deserialize(reader.ReadToEnd());
            ManifestValidator.Validate(manifest);
            manifests.Add(manifest);
        }
        if (manifests.Count == 0) { throw new InvalidOperationException("No first-party app.json files are embedded."); }
        return manifests;
    }
}
