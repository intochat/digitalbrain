using System.Text.Json;
using DigitalBrain.Abstractions.Behavior;


namespace DigitalBrain.Core.Behavior;

internal sealed record BehaviorWork(string BehaviorId, string RunId, long Version, BehaviorNode Node,
    JsonElement Input, JsonElement Value, IReadOnlyList<BehaviorStep> Previous, bool Skip);
