using System.Text.Json;
using DigitalBrain.Abstractions.Behavior;


namespace DigitalBrain.Core.Behavior;

internal sealed record BehaviorStart(string RunId, long Version, BehaviorDefinition Definition, JsonElement Input);
