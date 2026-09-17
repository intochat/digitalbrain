using System.Text.Json;


namespace DigitalBrain.Abstractions.Behavior;

[GenerateSerializer, Alias("db.behavior.receipt")]
public sealed record BehaviorReceipt([property: Id(0)] string OperationId, [property: Id(1)] string? Error);
