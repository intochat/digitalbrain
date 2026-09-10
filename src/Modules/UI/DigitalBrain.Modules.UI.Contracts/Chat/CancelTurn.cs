using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Chat;

/// <summary>Cancels a running chat turn by its turn id.</summary>
[GenerateSerializer]
[Alias("chat.cancel-turn")]
public sealed record CancelTurn(CommandId Id, [property: Id(0)] SignalId Turn) : Command(Id);
