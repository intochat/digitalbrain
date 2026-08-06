using System.Text.Json;
using System.Text.Json.Serialization;

namespace DigitalBrain;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(StoredJournalRecord))]
[JsonSerializable(typeof(SynapseOrigin))]
[JsonSerializable(typeof(SynapseReference))]
[JsonSerializable(typeof(DeliveryTarget))]
[JsonSerializable(typeof(DeliveryTarget[]))]
[JsonSerializable(typeof(DeliveryProgress))]
[JsonSerializable(typeof(WatermarkEntry))]
[JsonSerializable(typeof(JournalRecordDirection))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(uint))]
[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(DateTimeOffset))]
[JsonSerializable(typeof(TimeSpan))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(int))]
internal sealed partial class JournalJsonContext : JsonSerializerContext;
