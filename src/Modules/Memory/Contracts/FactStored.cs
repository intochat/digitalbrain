using DigitalBrain.Abstractions.Messaging;
using DigitalBrain.Abstractions.Identity;
namespace DigitalBrain.Memory;

[GenerateSerializer]
[Alias("memory.fact-stored")]
public sealed record FactStored(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] long Sequence) : Synapse;
