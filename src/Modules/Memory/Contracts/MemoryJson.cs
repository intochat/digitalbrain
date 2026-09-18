using System.Text.Json.Serialization;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Memory;

namespace DigitalBrain.Memory;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(MemoryKey))]
[JsonSerializable(typeof(MemoryTag))]
[JsonSerializable(typeof(Remember))]
[JsonSerializable(typeof(Forget))]
[JsonSerializable(typeof(Recall))]
[JsonSerializable(typeof(RecallResult))]
[JsonSerializable(typeof(RecalledMemory))]
[JsonSerializable(typeof(RememberingBody))]
[JsonSerializable(typeof(ProtectedPayloadReference))]
[JsonSerializable(typeof(Accepted<MemoryKey>))]
public sealed partial class MemoryJson : JsonSerializerContext;
