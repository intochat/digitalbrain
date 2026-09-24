namespace IntoChat.Packages;

internal sealed record UpgradePackageRequest(string? Revision = null, Guid? OperationId = null);
