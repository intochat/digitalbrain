using System.Text.Json.Serialization;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.AI;

[assembly: NeuronJsonContext(typeof(AIJson))]

namespace DigitalBrain.AI;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TextBody))]
[JsonSerializable(typeof(SaidBody))]
public sealed partial class AIJson : JsonSerializerContext;
