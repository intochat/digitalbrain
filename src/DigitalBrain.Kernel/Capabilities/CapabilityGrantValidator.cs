using DigitalBrain.Kernel.Contracts;
namespace DigitalBrain.Kernel.Capabilities;

internal interface ICapabilityGrantSource
{
    ValueTask<CapabilityGrant?> ReadAsync(CapabilityRequest request, CancellationToken cancellationToken = default);
}
internal sealed class CapabilityGrantValidator
{
    public static readonly TimeSpan MaximumDeadline = TimeSpan.FromSeconds(60);
    public TimeSpan Validate(CapabilityRequest request, CapabilityGrant? grant, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);
        var remaining = request.Deadline - now;
        if (remaining <= TimeSpan.Zero || remaining > MaximumDeadline || grant is null || !grant.Enabled || grant.Paused ||
            grant.OwnerId != request.OwnerId || grant.InstallationId != request.InstallationId ||
            grant.ReleaseDigest != request.ReleaseDigest ||
            !string.Equals(grant.CapabilityId, request.CapabilityId, StringComparison.Ordinal) ||
            grant.CapabilityVersion != request.CapabilityVersion ||
            grant.ProviderConnectionId != request.ProviderConnectionId || grant.Revision != request.GrantRevision ||
            !grant.Allows(request))
            throw new CapabilityDeniedException();
        return remaining;
    }
}
