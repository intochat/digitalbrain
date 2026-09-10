using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.UI;

[GenerateSerializer, Alias("db.ui.command-turn")]
public sealed record CommandTurn(
    [property: Id(0)] CommandId Command,
    [property: Id(1)] SignalId Turn);
