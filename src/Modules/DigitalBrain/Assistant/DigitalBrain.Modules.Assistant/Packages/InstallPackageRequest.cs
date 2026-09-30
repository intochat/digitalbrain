namespace DigitalBrain.Assistant;

internal sealed record InstallPackageRequest(string? Revision = null, Dictionary<string, string>? Settings = null, Guid? OperationId = null,
    Dictionary<string, string>? Accounts = null);
