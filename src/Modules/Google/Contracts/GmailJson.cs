using System.Text.Json.Serialization;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Google;

[assembly: NeuronJsonContext(typeof(GmailJson))]

namespace DigitalBrain.Google;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ConnectGmailAccount))]
[JsonSerializable(typeof(DisconnectGmail))]
[JsonSerializable(typeof(PrepareGmailDraft))]
[JsonSerializable(typeof(ConfirmGmailDraft))]
[JsonSerializable(typeof(SearchGmailThreads))]
[JsonSerializable(typeof(ReadGmailThread))]
[JsonSerializable(typeof(GmailConnection))]
[JsonSerializable(typeof(GmailDraftPreview))]
[JsonSerializable(typeof(GmailContentRead))]
[JsonSerializable(typeof(Accepted<GmailConnection>))]
[JsonSerializable(typeof(Accepted<GmailDraftPreview>))]
[JsonSerializable(typeof(GmailConnected))]
[JsonSerializable(typeof(GmailDisconnected))]
[JsonSerializable(typeof(GmailDraftRequested))]
[JsonSerializable(typeof(GmailDraftPrepared))]
[JsonSerializable(typeof(GmailDraftConfirmed))]
[JsonSerializable(typeof(GmailDraftCreated))]
[JsonSerializable(typeof(GmailDraftUncertain))]
public sealed partial class GmailJson : JsonSerializerContext;
