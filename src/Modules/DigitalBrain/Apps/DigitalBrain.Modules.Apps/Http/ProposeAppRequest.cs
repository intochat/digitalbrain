namespace DigitalBrain.Apps;

internal sealed record ProposeAppRequest(PackageReference Source, string Title, Guid? OperationId = null);
