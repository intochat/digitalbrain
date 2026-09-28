namespace DigitalBrain.Apps;

[GenerateSerializer, Alias("apps.uninstall-app")]
public sealed record UninstallApp([property: Id(0)] Guid OperationId);
