namespace DigitalBrain.Apps;

[GenerateSerializer, Alias("apps.pull-package")]
public sealed record PullPackage([property: Id(0)] Guid OperationId, [property: Id(1)] PackageRevisionRef Source);
