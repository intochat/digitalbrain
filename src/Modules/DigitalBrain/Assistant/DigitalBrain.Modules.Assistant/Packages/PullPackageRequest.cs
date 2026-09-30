namespace DigitalBrain.Assistant;

internal sealed record PullPackageRequest(PackageReference? Source = null, Guid? OperationId = null);
