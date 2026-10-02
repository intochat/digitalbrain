using DigitalBrain.Apps;

namespace DigitalBrain.Apps;

internal sealed record CommitAppRequest(PackageContent Content, string Message, string? ExpectedHead = null, PackageReference? MergeFrom = null, Guid? OperationId = null);
