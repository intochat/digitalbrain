namespace DigitalBrain.Assistant;

internal sealed record ConfigurePackageRequest(Dictionary<string, string> Settings, Guid? OperationId = null,
    Dictionary<string, string>? Accounts = null);
