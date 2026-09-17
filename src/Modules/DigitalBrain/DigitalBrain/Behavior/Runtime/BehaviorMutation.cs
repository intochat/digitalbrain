using System.Text.Json;
using DigitalBrain.Abstractions.Behavior;


namespace DigitalBrain.Core.Behavior;

internal sealed record BehaviorMutation(string OperationId, string Action, BehaviorDefinition? Definition = null,
    long? ExpectedVersion = null, long? Version = null, bool? Enabled = null, string? RunId = null, JsonElement Input = default);
