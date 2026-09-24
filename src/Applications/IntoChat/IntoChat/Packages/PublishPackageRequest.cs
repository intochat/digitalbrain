namespace IntoChat.Packages;

internal sealed record PublishPackageRequest(string? Revision = null, Guid? OperationId = null);
