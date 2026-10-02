namespace DigitalBrain.Apps;

internal sealed record PublishAppRequest(string? Revision = null, Guid? OperationId = null);
