using System.Text.Json.Serialization;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Telegram;

[assembly: NeuronJsonContext(typeof(TelegramJson))]
namespace DigitalBrain.Telegram;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TelegramMessage))]
[JsonSerializable(typeof(MessageReceived))]
[JsonSerializable(typeof(TelegramSnapshot))]
[JsonSerializable(typeof(Accepted<MessageReceived>))]
public sealed partial class TelegramJson : JsonSerializerContext;
