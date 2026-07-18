using DigitalBrain.Protocol.Domain.ValueObjects.Identity;

namespace DigitalBrain.Protocol.Domain.Events;

[GenerateSerializer]
public sealed record BundlePublished(
    BundleId BundleId,
    string? Description = null
) : Synapse;