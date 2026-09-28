namespace DigitalBrain.Apps;

// Source is the C# script of a "csharp" app and empty otherwise. Files carry everything else an app is
// made of, keyed by path: its app.feature spec, prompts and the configuration its runtime reads.
[GenerateSerializer, Alias("apps.package-content")]
public sealed record PackageContent(
    [property: Id(0)] PackageManifest Manifest,
    [property: Id(1)] string Source,
    [property: Id(2)] IReadOnlyDictionary<string, string>? Files = null)
{
    public const string SpecPath = "app.feature";

    public string? File(string path) => Files is not null && Files.TryGetValue(path, out var text) ? text : null;
}
