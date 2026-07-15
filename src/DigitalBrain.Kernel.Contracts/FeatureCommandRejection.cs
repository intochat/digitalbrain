using Orleans;

namespace DigitalBrain.Kernel.Contracts;

[Alias("digitalbrain.feature.command-rejection-reason.v1")]
public enum FeatureCommandRejectionReason
{
    Unspecified = 0,
    Conflict = 1,
    Precondition = 2,
    Limit = 3,
    Unavailable = 4
}

[Alias("digitalbrain.feature.authority-rejection-reason.v1")]
public enum FeatureAuthorityRejectionReason
{
    Unspecified = 0,
    MissingGrant = 1,
    ActorMismatch = 2
}

[GenerateSerializer]
[Alias("digitalbrain.feature.command-rejected.v1")]
public sealed class FeatureCommandRejectedException(FeatureCommandRejectionReason reason)
    : InvalidOperationException("The Feature command was rejected.")
{
    [Id(0)] public FeatureCommandRejectionReason Reason { get; } = reason;
}

[GenerateSerializer]
[Alias("digitalbrain.feature.authority-rejected.v1")]
public sealed class FeatureAuthorityRejectedException(FeatureAuthorityRejectionReason reason)
    : UnauthorizedAccessException("Feature authority was rejected.")
{
    [Id(0)] public FeatureAuthorityRejectionReason Reason { get; } = reason;
}
