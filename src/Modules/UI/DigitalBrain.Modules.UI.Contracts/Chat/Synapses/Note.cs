using DigitalBrain.Abstractions;

using DigitalBrain.Abstractions.Messaging;
namespace DigitalBrain.Chat;

[GenerateSerializer]
[Alias("ui.note")]
public sealed record Note([property: Id(0)] string Text) : Synapse;
