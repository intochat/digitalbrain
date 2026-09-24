namespace IntoChat.Packages;

internal sealed record ConfigurePackageRequest(Dictionary<string, string> Settings, Guid? OperationId = null);
