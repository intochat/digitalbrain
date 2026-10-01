namespace DigitalBrain.Microsoft.Aspire;

[GenerateSerializer, Alias("microsoft.aspire.resource")]
public sealed record AspireResource(
    [property: Id(0)] string Name,
    [property: Id(1)] string Type,
    [property: Id(2)] string State,
    [property: Id(3)] string? Health,
    [property: Id(4)] IReadOnlyList<string> Urls);
