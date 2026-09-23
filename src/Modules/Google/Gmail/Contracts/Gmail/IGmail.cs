using DigitalBrain.Contracts;

namespace DigitalBrain.Google.Gmail;

[Alias("gmail")]
[Orleans.Metadata.DefaultGrainType("gmail")]
public interface IGmail : INeuron
{
    Task AcceptWatchPush(GmailWatchPush push);
    Task AcceptAuthorizationCode(string authorizationCode, string? secretOwner = null);
}

[GenerateSerializer, Alias("gmail.watch-push")]
public sealed record GmailWatchPush(
    [property: Id(0)] string HistoryId,
    [property: Id(1)] string EmailAddress);