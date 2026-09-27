namespace DigitalBrain.Apps;

[GenerateSerializer, Alias("apps.package-content")]
public sealed record PackageContent(
    [property: Id(0)] PackageManifest Manifest,
    [property: Id(1)] string Source);
