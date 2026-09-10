using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Google;

/// <summary>Connects a validated Google account.</summary>
[GenerateSerializer]
[Alias("db.gmail.connect-account")]
public sealed record ConnectGmailAccount(
    CommandId Id,
    [property: Id(0)] string Subject,
    [property: Id(1)] string Email,
    [property: Id(2)] string AccessToken,
    [property: Id(3)] string? RefreshToken,
    [property: Id(4)] string GrantedScopes,
    [property: Id(5)] int ExpiresInSeconds) : Command(Id);
