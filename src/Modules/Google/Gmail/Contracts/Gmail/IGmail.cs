using DigitalBrain.Contracts;

namespace DigitalBrain.Google.Gmail;

[Alias("gmail")]
[Orleans.Metadata.DefaultGrainType("gmail")]
public interface IGmail : INeuron
{
    Task AcceptWatchPush(GmailWatchPush push);
    Task AcceptAuthorizationCode(string authorizationCode);
    Task StoreMailboxSecret(GmailMailboxSecret secret);
    Task ArmWatch();
    Task<bool> IsMailboxConnected();
}

[GenerateSerializer, Alias("gmail.mailbox-secret")]
public sealed record GmailMailboxSecret(
    [property: Id(0)] string Email,
    [property: Id(1)] string ProtectedRefreshToken,
    [property: Id(2)] string GrantedScopes);

[GenerateSerializer, Alias("gmail.watch-push")]
public sealed record GmailWatchPush(
    [property: Id(0)] string HistoryId,
    [property: Id(1)] string EmailAddress);