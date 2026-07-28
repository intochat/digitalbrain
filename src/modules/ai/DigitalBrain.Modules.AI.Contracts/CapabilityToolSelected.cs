using DigitalBrain.Abstractions;

namespace DigitalBrain.AI;

[GenerateSerializer]
[Alias("ai.capability-tool-selected")]
public sealed record CapabilityToolSelected([property: Id(0)] string Tool) : Synapse;
