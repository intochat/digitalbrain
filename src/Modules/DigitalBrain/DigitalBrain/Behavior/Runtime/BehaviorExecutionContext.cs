using System.Text.Json;
using DigitalBrain.Abstractions.Behavior;


namespace DigitalBrain.Core.Behavior;

public sealed record BehaviorExecutionContext(
    string BehaviorId, string RunId, long Version, BehaviorNode Node,
    JsonElement Input, JsonElement Value, IReadOnlyDictionary<string, JsonElement> Outputs);
