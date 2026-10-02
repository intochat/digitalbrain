namespace DigitalBrain.Apps;

// Source is the legacy single script of a "csharp" app and empty otherwise. Files carry everything
// else an app is made of, keyed by path: its app.spec.md spec, its tests.cs, its behaviors/*.cs
// scripts, prompts and the configuration its runtime reads.
[GenerateSerializer, Alias("apps.package-content")]
public sealed record PackageContent(
    [property: Id(0)] PackageManifest Manifest,
    [property: Id(1)] string Source,
    [property: Id(2)] Dictionary<string, string>? Files = null)
{
    public const string SpecPath = "app.spec.md";
    public const string TestsPath = "tests.cs";
    public const string SourcePath = "app.cs";
    public const string BehaviorsPrefix = "behaviors/";

    public string? File(string path) => Files is not null && Files.TryGetValue(path, out var text) ? text : null;

    // Everything a csharp app runs: the legacy single Source plus one script per behaviors/*.cs file.
    public IReadOnlyDictionary<string, string> Programs()
    {
        var programs = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(Source)) { programs[SourcePath] = Source; }
        foreach (var (path, text) in Files ?? new Dictionary<string, string>())
        {
            if (path.StartsWith(BehaviorsPrefix, StringComparison.Ordinal) && path.EndsWith(".cs", StringComparison.Ordinal))
            { programs[path] = text; }
        }
        return programs;
    }
}
