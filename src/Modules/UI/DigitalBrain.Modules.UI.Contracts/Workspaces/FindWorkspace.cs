using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.UI;

/// <summary>Finds a workspace by name or correlation id.</summary>
[GenerateSerializer]
[Alias("ui.find-workspace")]
public sealed record FindWorkspace(
    [property: Id(0)] string? Name = null,
    [property: Id(1)] string? CorrelationId = null);
