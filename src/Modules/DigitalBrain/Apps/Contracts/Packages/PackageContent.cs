namespace DigitalBrain.Apps;

[GenerateSerializer, Alias("apps.package-content")]
public sealed record PackageContent(
    [property: Id(0)] PackageManifest Manifest,
    [property: Id(1)] string Source,
    [property: Id(2)] string Tests,
    [property: Id(3)] IReadOnlyList<string> ModuleIds);
