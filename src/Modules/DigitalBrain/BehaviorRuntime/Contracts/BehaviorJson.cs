using System.Text.Json.Serialization;

namespace DigitalBrain.Abstractions.Behavior;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BehaviorDefinition))]
[JsonSerializable(typeof(BehaviorSnapshot))]
[JsonSerializable(typeof(BehaviorRunSnapshot))]
[JsonSerializable(typeof(BehaviorValidation))]
[JsonSerializable(typeof(IReadOnlyList<string>))]
public sealed partial class BehaviorJson : JsonSerializerContext;
