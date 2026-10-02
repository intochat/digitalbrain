namespace DigitalBrain.Apps;

internal sealed record ForkAppRequest(string? Name = null, string? Revision = null, Guid? OperationId = null);
