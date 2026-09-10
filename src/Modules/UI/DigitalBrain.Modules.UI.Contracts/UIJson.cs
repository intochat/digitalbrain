using System.Text.Json.Serialization;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Chat;
using DigitalBrain.UI;

[assembly: NeuronJsonContext(typeof(UIJson))]

namespace DigitalBrain.UI;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(SendMessage))]
[JsonSerializable(typeof(CancelTurn))]
[JsonSerializable(typeof(Accepted<ChatTurnStatus>))]
[JsonSerializable(typeof(ReadTranscript))]
[JsonSerializable(typeof(ReadTurns))]
[JsonSerializable(typeof(ReadTurn))]
[JsonSerializable(typeof(ContextRef))]
[JsonSerializable(typeof(ChatTurn))]
[JsonSerializable(typeof(ChatTranscript))]
[JsonSerializable(typeof(ChatTurns))]
[JsonSerializable(typeof(ChatTurnSnapshot))]
[JsonSerializable(typeof(ChatTurnStatus))]
[JsonSerializable(typeof(KitCardOffer))]
[JsonSerializable(typeof(Responded))]
[JsonSerializable(typeof(Accepted<SignalId>))]
[JsonSerializable(typeof(SignalId))]
public sealed partial class UIJson : JsonSerializerContext;
