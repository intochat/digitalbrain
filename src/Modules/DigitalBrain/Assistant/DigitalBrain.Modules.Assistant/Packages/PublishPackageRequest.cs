namespace DigitalBrain.Assistant;

internal sealed record PublishPackageRequest(string? Revision = null, Guid? OperationId = null);
