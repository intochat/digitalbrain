namespace DigitalBrain.Apps;

internal sealed record UpgradeAppRequest(string? Revision = null, Guid? OperationId = null,
    Dictionary<string, string>? Accounts = null);
