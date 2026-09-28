namespace DigitalBrain.Apps;

[GenerateSerializer, Alias("apps.fork-package")]
public sealed record ForkPackage([property: Id(0)] Guid OperationId, [property: Id(1)] PackageRevisionRef Source);
