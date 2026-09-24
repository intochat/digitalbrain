namespace IntoChat.Packages;

internal sealed record PullPackageRequest(PackageReference? Source = null, Guid? OperationId = null);
