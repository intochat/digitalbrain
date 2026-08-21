using DigitalBrain.Abstractions;

using DigitalBrain.Abstractions.Messaging;
using DigitalBrain.Abstractions.Identity;
namespace DigitalBrain.AI;

[GenerateSerializer]
[Alias("ai.transcribed")]
public sealed record Transcribed(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string Text,
    [property: Id(2)] string ModelId,
    [property: Id(3)] double? DurationSeconds = null) : Synapse;
