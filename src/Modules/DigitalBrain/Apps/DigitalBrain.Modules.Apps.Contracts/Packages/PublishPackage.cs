namespace DigitalBrain.Apps;

[GenerateSerializer, Alias("apps.publish-package")]
public sealed record PublishPackage([property: Id(0)] Guid OperationId, [property: Id(1)] string Revision);
