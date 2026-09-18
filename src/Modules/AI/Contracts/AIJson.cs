using System.Text.Json.Serialization;
using DigitalBrain.AI;

namespace DigitalBrain.AI;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TextBody))]
[JsonSerializable(typeof(SaidBody))]
[JsonSerializable(typeof(BuildAgent))]
[JsonSerializable(typeof(AgentRequest))]
[JsonSerializable(typeof(CancelAgent))]
[JsonSerializable(typeof(CloseAgentBuilder))]
[JsonSerializable(typeof(AgentMessage))]
[JsonSerializable(typeof(AgentSnapshot))]
[JsonSerializable(typeof(AgentResponse))]
[JsonSerializable(typeof(IReadOnlyList<AgentBuildSnapshot>))]
public sealed partial class AIJson : JsonSerializerContext;
