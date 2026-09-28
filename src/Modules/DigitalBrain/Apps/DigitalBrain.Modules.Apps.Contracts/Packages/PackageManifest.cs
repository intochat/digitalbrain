namespace DigitalBrain.Apps;

[GenerateSerializer, Alias("apps.package-manifest")]
public sealed record PackageManifest(
    [property: Id(0)] string Title,
    [property: Id(1)] string Description,
    [property: Id(2)] IReadOnlyList<PackageOperation> Operations,
    [property: Id(3)] IReadOnlyList<PackageSetting> Settings,
    [property: Id(4)] IReadOnlyList<PackageAccount>? Accounts = null,
    [property: Id(5)] string? Runtime = null)
{
    public const string CSharpRuntime = "csharp";

    public string RuntimeName => string.IsNullOrWhiteSpace(Runtime) ? CSharpRuntime : Runtime;
}
