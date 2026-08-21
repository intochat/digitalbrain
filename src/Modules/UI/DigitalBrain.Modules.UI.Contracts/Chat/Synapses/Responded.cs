using DigitalBrain.Abstractions;
using DigitalBrain.UI;

using DigitalBrain.Abstractions.Messaging;
using DigitalBrain.Abstractions.Identity;
namespace DigitalBrain.Chat;

[GenerateSerializer]
[JournalProjection]
[Alias("chat.responded")]
public sealed record Responded(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] NeuronId Chat,
    [property: Id(2)] string Text,
    [property: Id(3)] string Author = "") : Synapse;
