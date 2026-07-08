namespace DigitalBrain.Core;

[GenerateSerializer]
[Alias("DigitalBrain.Core.CapabilityInvocation")]
public record CapabilityInvocation(
    [property: Id(0)] string CapabilityId,
    [property: Id(1)] string Prompt,
    [property: Id(2)] string? ClientId = null,
    [property: Id(3)] string? WorkspaceId = null,
    [property: Id(4)] IReadOnlyDictionary<string, object?>? Hints = null)
    : Synapse(nameof(CapabilityInvocation), DateTimeOffset.UtcNow);
