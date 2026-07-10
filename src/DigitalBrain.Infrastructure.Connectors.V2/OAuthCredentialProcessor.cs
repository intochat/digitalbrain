using DigitalBrain.Core.V2;

namespace DigitalBrain.Infrastructure.Connectors.V2;

public sealed class V2OAuthCredentialProcessor(
    IV2CredentialStore credentials,
    IProviderOAuthAdapterRegistry adapters,
    IClock? clock = null)
{
    private readonly IClock _clock = clock ?? SystemClock.Instance;

    public async Task<CredentialRecord?> RefreshAsync(CredentialOwner owner, CredentialRef reference, string leaseOwner, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
    {
        var leased = await credentials.TryAcquireLeaseAsync(owner, reference, leaseOwner, leaseDuration, CredentialStatus.Refreshing, cancellationToken);
        if (leased is null) return null;
        var secret = await credentials.ReadSecretAsync(owner, reference, cancellationToken);
        if (secret is null) { await credentials.MarkStatusAsync(owner, reference, leased.Revision, CredentialStatus.ReauthorizationRequired, "credential-secret-unavailable", cancellationToken); return null; }
        ProviderCallResult<ProviderTokenSet> result;
        try { result = await adapters.GetRequired(leased.Provider).RefreshAsync(new OAuthRefreshRequest(reference, secret, leased.GrantedScopes), cancellationToken); }
        catch (Exception ex) when (ex is not OperationCanceledException) { result = ProviderCallResult<ProviderTokenSet>.Unknown(V2Redaction.SafeSummary(ex.Message)); }
        return result.Outcome switch
        {
            ProviderCallOutcome.Success when result.Value is not null => await credentials.RotateAsync(owner, reference, leased.Revision, result.Value, true, cancellationToken),
            ProviderCallOutcome.ReauthorizationRequired => await Mark(owner, reference, leased.Revision, CredentialStatus.ReauthorizationRequired, result.SafeReason, cancellationToken),
            ProviderCallOutcome.OutcomeUnknown => await Mark(owner, reference, leased.Revision, CredentialStatus.OutcomeUnknown, result.SafeReason, cancellationToken),
            _ => await Mark(owner, reference, leased.Revision, CredentialStatus.Expired, result.SafeReason ?? "refresh-failed", cancellationToken),
        };
    }

    public async Task<bool> RevokeAsync(CredentialOwner owner, CredentialRef reference, string leaseOwner, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
    {
        var leased = await credentials.TryAcquireLeaseAsync(owner, reference, leaseOwner, leaseDuration, CredentialStatus.Revoking, cancellationToken);
        if (leased is null) return false;
        var secret = await credentials.ReadSecretAsync(owner, reference, cancellationToken);
        if (secret is null) { await credentials.MarkRevokedAsync(owner, reference, leased.Revision, cancellationToken); return true; }
        ProviderCallResult<bool> result;
        try { result = await adapters.GetRequired(leased.Provider).RevokeAsync(new OAuthRevokeRequest(reference, secret), cancellationToken); }
        catch (Exception ex) when (ex is not OperationCanceledException) { result = ProviderCallResult<bool>.Unknown(V2Redaction.SafeSummary(ex.Message)); }
        if (result.Outcome == ProviderCallOutcome.Success || result.Outcome == ProviderCallOutcome.ReauthorizationRequired)
        {
            await credentials.MarkRevokedAsync(owner, reference, leased.Revision, cancellationToken);
            return true;
        }
        await credentials.MarkStatusAsync(owner, reference, leased.Revision, result.Outcome == ProviderCallOutcome.OutcomeUnknown ? CredentialStatus.OutcomeUnknown : CredentialStatus.Unavailable, result.SafeReason, cancellationToken);
        return false;
    }

    private async Task<CredentialRecord?> Mark(CredentialOwner owner, CredentialRef reference, long revision, CredentialStatus status, string? reason, CancellationToken cancellationToken)
    {
        await credentials.MarkStatusAsync(owner, reference, revision, status, V2Redaction.SafeSummary(reason), cancellationToken);
        return await credentials.GetAsync(owner, reference, cancellationToken);
    }
}
