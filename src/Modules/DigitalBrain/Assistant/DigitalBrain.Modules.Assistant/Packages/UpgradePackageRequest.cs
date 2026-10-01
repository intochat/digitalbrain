namespace DigitalBrain.Assistant;

internal sealed record UpgradePackageRequest(string? Revision = null, Guid? OperationId = null,
    Dictionary<string, string>? Accounts = null);
