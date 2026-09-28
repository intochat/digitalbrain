using DigitalBrain.Contracts;

namespace DigitalBrain.Microsoft.Aspire;

[GenerateSerializer, Alias("microsoft.aspire.resource-state-changed")]
public sealed record ResourceStateChanged(
    [property: Id(0)] string Resource,
    [property: Id(1)] string State,
    [property: Id(2)] string? Health,
    [property: Id(3)] string? PreviousState) : Signal;
