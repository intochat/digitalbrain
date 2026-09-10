using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Google;

/// <summary>Disconnects Gmail.</summary>
[GenerateSerializer]
[Alias("db.gmail.disconnect")]
public sealed record DisconnectGmail(
    CommandId Id) : Command(Id);
