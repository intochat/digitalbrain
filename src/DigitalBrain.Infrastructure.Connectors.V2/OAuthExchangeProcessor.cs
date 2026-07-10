using DigitalBrain.Core.V2;

namespace DigitalBrain.Infrastructure.Connectors.V2;

public sealed class V2OAuthExchangeProcessor(
    IOAuthFlowStore flows,
    ISecretVault secrets,
    IProviderOAuthAdapterRegistry adapters,
    IV2CredentialStore credentials,
    IClock? clock = null)
{
    private readonly IClock _clock = clock ?? SystemClock.Instance;

    public async Task<OAuthFlowRecord?> ProcessAsync(OAuthFlowKey flowKey, string effectId, string leaseOwner, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var leased = await flows.TryAcquireExchangeLeaseAsync(flowKey, effectId, leaseOwner, now, leaseDuration, cancellationToken);
        if (leased is null) return null;
        if (leased.CodeRef is null) return await Fail(leased, "authorization-code-ref-missing", cancellationToken);
        var code = await secrets.ReadAsync(leased.Owner, leased.CodeRef.Value, cancellationToken);
        var verifier = await secrets.ReadAsync(leased.Owner, leased.VerifierRef, cancellationToken);
        if (code is null || verifier is null || !code.TryGetValue(OAuthSecretNames.AuthorizationCode, out var codeValue) || !verifier.TryGetValue(OAuthSecretNames.CodeVerifier, out var verifierValue))
            return await Fail(leased, "oauth-secret-ref-unavailable", cancellationToken);
        ProviderCallResult<ProviderTokenSet> result;
        try
        {
            var adapter = adapters.GetRequired(leased.Provider);
            result = await adapter.ExchangeAsync(new OAuthExchangeRequest(codeValue, verifierValue, leased.RedirectUri, leased.RequestedScopes), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result = ProviderCallResult<ProviderTokenSet>.Unknown(V2Redaction.SafeSummary(ex.Message));
        }
        return result.Outcome switch
        {
            ProviderCallOutcome.Success when result.Value is not null => await Succeed(leased, result.Value, cancellationToken),
            ProviderCallOutcome.ReauthorizationRequired => await Transition(leased, OAuthFlowStatus.ReauthorizationRequired, result.SafeReason, null, null, cancellationToken),
            ProviderCallOutcome.RetryableFailure when leased.ExpiresAt > now.AddSeconds(10) => await Transition(leased, OAuthFlowStatus.RetryScheduled, result.SafeReason, null, now.AddSeconds(5), cancellationToken),
            ProviderCallOutcome.OutcomeUnknown => await Transition(leased, OAuthFlowStatus.OutcomeUnknown, result.SafeReason, null, null, cancellationToken),
            _ => await Transition(leased, OAuthFlowStatus.Failed, result.SafeReason ?? "provider-exchange-failed", null, null, cancellationToken),
        };
    }

    private async Task<OAuthFlowRecord> Succeed(OAuthFlowRecord flow, ProviderTokenSet tokenSet, CancellationToken cancellationToken)
    {
        var credential = await credentials.CreateFromExchangeAsync(flow.ResultCredentialRef ?? new CredentialRef("v2-cred-" + Guid.NewGuid().ToString("N")), flow.Provider, flow.Owner, tokenSet, cancellationToken);
        return await Transition(flow, OAuthFlowStatus.Succeeded, null, credential.Reference, null, cancellationToken);
    }

    private Task<OAuthFlowRecord> Fail(OAuthFlowRecord flow, string reason, CancellationToken cancellationToken) => Transition(flow, OAuthFlowStatus.Failed, reason, null, null, cancellationToken);

    private Task<OAuthFlowRecord> Transition(OAuthFlowRecord flow, OAuthFlowStatus status, string? reason, CredentialRef? credential, DateTimeOffset? nextAttempt, CancellationToken cancellationToken)
        => flows.TransitionAsync(flow.Key, flow.Revision, status, _clock.UtcNow, V2Redaction.SafeSummary(reason), credential, nextAttempt, cancellationToken);
}
