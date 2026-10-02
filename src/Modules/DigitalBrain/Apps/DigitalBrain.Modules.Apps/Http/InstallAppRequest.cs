namespace DigitalBrain.Apps;

internal sealed record InstallAppRequest(string? Revision = null, Dictionary<string, string>? Settings = null, Guid? OperationId = null,
    Dictionary<string, string>? Accounts = null);
