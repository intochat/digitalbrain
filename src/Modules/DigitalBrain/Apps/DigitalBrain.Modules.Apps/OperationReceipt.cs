namespace DigitalBrain.Apps;

[GenerateSerializer, Alias("apps.operation-receipt")]
public sealed record OperationReceipt([property: Id(0)] Guid OperationId, [property: Id(1)] string RequestHash, [property: Id(2)] string Result);
