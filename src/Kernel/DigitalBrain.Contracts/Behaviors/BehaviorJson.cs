using System.Text.Json.Serialization;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Abstractions.Behaviors;

[assembly: NeuronJsonContext(typeof(BehaviorJson))]

namespace DigitalBrain.Abstractions.Behaviors;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(BehaviorDefinition))]
[JsonSerializable(typeof(BehaviorSnapshot))]
[JsonSerializable(typeof(BehaviorValidation))]
[JsonSerializable(typeof(BehaviorVersion))]
[JsonSerializable(typeof(SaveBehavior))]
[JsonSerializable(typeof(ChangeBehavior))]
[JsonSerializable(typeof(BehaviorNodeActivation))]
[JsonSerializable(typeof(BehaviorNodeStatus))]
[JsonSerializable(typeof(IReadOnlyList<BehaviorDiagnostic>))]
[JsonSerializable(typeof(IReadOnlyList<DigitalBrain.Abstractions.Identity.NeuronId>))]
[JsonSerializable(typeof(IReadOnlyList<BehaviorCapability>))]
[JsonSerializable(typeof(Accepted<BehaviorVersion>))]
public sealed partial class BehaviorJson : JsonSerializerContext;
