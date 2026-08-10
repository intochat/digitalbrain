using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Chat;

[GenerateSerializer]
[Alias("ui.note")]
[Description("Post a line of text into a chat transcript")]
public sealed record Note([property: Id(0)] string Text) : Synapse;
