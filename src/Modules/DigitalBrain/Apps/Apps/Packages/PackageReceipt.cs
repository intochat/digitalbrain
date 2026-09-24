namespace DigitalBrain.Apps;

[GenerateSerializer, Alias("apps.package-receipt")]
public sealed record PackageReceipt([property: Id(0)] Guid OperationId, [property: Id(1)] string RequestHash, [property: Id(2)] string Result);
