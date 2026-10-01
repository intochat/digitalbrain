namespace DigitalBrain.Apps;

[GenerateSerializer, Alias("apps.package-directory-state")]
public sealed record PackageDirectoryState
{
    [Id(0)] public Dictionary<string, PackageListing> Listings { get; init; } = [];
}
