using System.ComponentModel;
using DigitalBrain.Abstractions;
using DigitalBrain.UI;

namespace DigitalBrain.Chat;

[GenerateSerializer]
[Alias("chat.responded")]
[Description("A response committed into a chat transcript")]
public sealed record Responded(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] NeuronId Chat,
    [property: Id(2)] string Text,
    [property: Id(3)] ChatButtonOffer[]? Buttons = null,
    [property: Id(4)] ChatChartOffer[]? Charts = null) : Synapse;
