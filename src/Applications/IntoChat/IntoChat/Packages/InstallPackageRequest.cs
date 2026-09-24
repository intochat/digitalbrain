namespace IntoChat.Packages;

internal sealed record InstallPackageRequest(string? Revision = null, Dictionary<string, string>? Settings = null, Guid? OperationId = null);
