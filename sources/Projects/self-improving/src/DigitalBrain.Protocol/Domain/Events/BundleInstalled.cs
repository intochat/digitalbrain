using DigitalBrain.Protocol.Domain.ValueObjects.Identity;

namespace DigitalBrain.Protocol.Domain.Events;

[GenerateSerializer]
public sealed record BundleInstalled(
    BundleId BundleId
) : Synapse;
