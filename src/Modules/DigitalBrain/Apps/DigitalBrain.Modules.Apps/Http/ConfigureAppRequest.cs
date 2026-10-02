namespace DigitalBrain.Apps;

internal sealed record ConfigureAppRequest(Dictionary<string, string> Settings, Guid? OperationId = null,
    Dictionary<string, string>? Accounts = null);
