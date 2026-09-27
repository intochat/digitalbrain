using System.Text.Json.Serialization;

namespace DigitalBrain.Apps;

[GenerateSerializer, Alias("apps.app-snapshot")]
public sealed record AppSnapshot(
    [property: Id(0)] AppStatus Status,
    [property: Id(1)] PackageRevisionRef? Revision,
    [property: Id(2)] IReadOnlyDictionary<string, string> Settings,
    [property: Id(3)] IReadOnlyList<PackageOperation> Operations,
    // Web JSON would otherwise spell it "cSharpFile".
    [property: Id(4), JsonPropertyName("csharpFile")] string? CSharpFile,
    [property: Id(5)] IReadOnlyDictionary<string, string>? Accounts = null);
