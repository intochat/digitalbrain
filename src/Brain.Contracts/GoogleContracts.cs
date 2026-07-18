namespace DigitalBrain.Google;

using Brain.Contracts;

[Alias("digitalbrain.google.IGmail")]
[NeuronContract("google.gmail.v1")]
public interface IGmail : IGrainWithStringKey
{
    [Alias("GetIdentityAsync")]
    Task<string> GetIdentityAsync();

    [Alias("ListMessagesAsync")]
    Task<CommandReceipt> ListMessagesAsync(CommandSynapse<GmailListRequest> command);

    [Alias("SendMessageAsync")]
    Task<CommandReceipt> SendMessageAsync(CommandSynapse<GmailSendRequest> command);

    [Alias("GetSurfaceAsync")]
    Task<UiSurfaceSnapshot> GetSurfaceAsync();
}

[GenerateSerializer, Alias("brain.google.gmail-list-request.v1")]
public sealed record GmailListRequest(
    [property: Id(0)] string Query,
    [property: Id(1)] int MaxResults);

[GenerateSerializer, Alias("brain.google.gmail-send-request.v1")]
public sealed record GmailSendRequest(
    [property: Id(0)] string To,
    [property: Id(1)] string Subject,
    [property: Id(2)] string Body);
