namespace IntoChat.Packages;

internal sealed record ForkPackageRequest(string? Name = null, string? Revision = null, Guid? OperationId = null);
