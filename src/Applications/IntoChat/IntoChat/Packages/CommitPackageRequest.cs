using DigitalBrain.Apps;

namespace IntoChat.Packages;

internal sealed record CommitPackageRequest(PackageContent Content, string Message, string? ExpectedHead = null, PackageReference? MergeFrom = null, Guid? OperationId = null);
