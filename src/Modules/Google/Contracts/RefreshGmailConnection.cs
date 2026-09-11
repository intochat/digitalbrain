using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Google;

/// <summary>Requires a stored refresh token; a refused grant clears the connection.</summary>
[GenerateSerializer, Alias("db.gmail.refresh")]
public sealed record RefreshGmailConnection(CommandId Id) : Command(Id);
