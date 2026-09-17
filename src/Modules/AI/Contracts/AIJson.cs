using System.Text.Json.Serialization;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.AI;

[assembly: NeuronJsonContext(typeof(AIJson))]

namespace DigitalBrain.AI;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TextBody))]
[JsonSerializable(typeof(SaidBody))]
[JsonSerializable(typeof(BuildAgent))]
[JsonSerializable(typeof(SendAgentMessage))]
[JsonSerializable(typeof(StopAgent))]
[JsonSerializable(typeof(CloseAgentBuilder))]
[JsonSerializable(typeof(AgentTaskQuery))]
[JsonSerializable(typeof(AgentSnapshot))]
[JsonSerializable(typeof(AgentTaskSnapshot))]
[JsonSerializable(typeof(IReadOnlyList<AgentBuildSnapshot>))]
public sealed partial class AIJson : JsonSerializerContext;
