using System.Text.Json;
using DigitalBrain.Abstractions.Behavior;


namespace DigitalBrain.Core.Behavior;

public sealed record BehaviorNodeResult(JsonElement Output, bool Stop = false);
