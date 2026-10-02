namespace DigitalBrain.Apps;

internal sealed record PullAppRequest(PackageReference? Source = null, Guid? OperationId = null);
