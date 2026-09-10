using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Google;

/// <summary>Redeems a one-use login nonce; the grant must include Gmail read and identity scopes.</summary>
[GenerateSerializer]
[Alias("db.gmail.connect-account")]
public sealed record ConnectGmailAccount(
    CommandId Id,
    [property: Id(0)] string Subject,
    [property: Id(1)] string Email,
    [property: Id(2)] string GrantedScopes,
    [property: Id(3)] int ExpiresInSeconds,
    [property: Id(4)] string Nonce) : Command(Id);
