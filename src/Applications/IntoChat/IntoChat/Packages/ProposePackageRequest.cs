namespace IntoChat.Packages;

internal sealed record ProposePackageRequest(PackageReference Source, string Title, Guid? OperationId = null);
