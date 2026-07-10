using DigitalBrain.Core.V2;
using System.Security.Cryptography;
using V2RequestContext = DigitalBrain.Core.V2.RequestContext;

namespace DigitalBrain.Infrastructure.Connectors.V2;

public sealed class V2OAuthCoordinator(
    OAuthStateKeyRing stateKeys,
    IProviderOAuthAdapterRegistry adapters,
    IConnectorAuthorizationPolicy authorization,
    IOAuthFlowStore flows,
    ISecretVault secrets,
    IClock? clock = null)
{
    private readonly IClock _clock = clock ?? SystemClock.Instance;

    public async Task<BeginOAuthResult> BeginAsync(BeginOAuthRequest request, CancellationToken cancellationToken = default)
    {
        authorization.DemandAuthorize(request.Context, request.Provider, request.CapabilityIds);
        var adapter = adapters.GetRequired(request.Provider);
        if (!adapter.IsAllowedRedirectUri(request.RedirectUri)) throw new InvalidOperationException("OAuth redirect URI is not registered for this provider.");
        if (request.Lifetime <= TimeSpan.Zero || request.Lifetime > TimeSpan.FromMinutes(15)) throw new ArgumentOutOfRangeException(nameof(request));
        var capabilities = request.CapabilityIds.Select(id => adapter.Capabilities.Single(x => x.Id == id)).ToArray();
        var scopes = capabilities.SelectMany(x => x.RequiredScopes).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var state = stateKeys.CreateState();
        var flowKey = stateKeys.DeriveFlowKey(state);
        var owner = CredentialOwner.From(request.Context);
        var verifier = Pkce.CreateVerifier();
        var verifierRef = await secrets.WriteAsync(owner, OAuthSecretNames.CodeVerifier, new SecretPayload(new Dictionary<string, string> { [OAuthSecretNames.CodeVerifier] = verifier }), _clock.UtcNow.Add(request.Lifetime), cancellationToken);
        var expires = _clock.UtcNow.Add(request.Lifetime);
        var flow = new OAuthFlowRecord(flowKey, 0, OAuthFlowStatus.Started, request.Provider, owner, request.RedirectUri, scopes, request.CapabilityIds.ToArray(), verifierRef, null, null, new CredentialRef("v2-cred-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12))), _clock.UtcNow, expires, request.Context.CorrelationId, [], null, null, null, null);
        if (!await flows.TryCreateAsync(flow, cancellationToken)) throw new InvalidOperationException("OAuth flow collision.");
        var uri = adapter.CreateAuthorizationUri(new OAuthAuthorizationRequest(state, request.RedirectUri, Pkce.CreateS256Challenge(verifier), scopes));
        return new BeginOAuthResult(uri, state, flowKey, expires);
    }

    public async Task<OAuthCallbackResult> CompleteAsync(OAuthCallbackRequest request, CancellationToken cancellationToken = default)
    {
        var flowKey = stateKeys.DeriveFlowKey(request.State);
        var flow = await flows.GetAsync(flowKey, cancellationToken) ?? throw new InvalidOperationException("OAuth flow not found.");
        if (!string.Equals(flow.Provider, request.Provider, StringComparison.Ordinal) || !string.Equals(flow.RedirectUri, request.RedirectUri, StringComparison.Ordinal)) throw new UnauthorizedAccessException("OAuth callback does not match its flow.");
        if (flow.ExpiresAt <= _clock.UtcNow) return new OAuthCallbackResult(flowKey, OAuthFlowStatus.Expired, null, flow.ResultCredentialRef, false, "flow-expired");
        if (flow.Status != OAuthFlowStatus.Started) return new OAuthCallbackResult(flowKey, flow.Status, flow.EffectId, flow.ResultCredentialRef, true, "callback-already-claimed");
        if (!string.IsNullOrWhiteSpace(request.Error) || string.IsNullOrWhiteSpace(request.Code))
        {
            var failed = await flows.TransitionAsync(flowKey, flow.Revision, OAuthFlowStatus.Failed, _clock.UtcNow, V2Redaction.SafeSummary(request.ErrorDescription ?? request.Error ?? "authorization-denied"), null, null, cancellationToken);
            return new OAuthCallbackResult(flowKey, failed.Status, null, failed.ResultCredentialRef, false, failed.SafeFailure);
        }
        var owner = flow.Owner;
        var codeRef = await secrets.WriteAsync(owner, OAuthSecretNames.AuthorizationCode, new SecretPayload(new Dictionary<string, string> { [OAuthSecretNames.AuthorizationCode] = request.Code! }), flow.ExpiresAt, cancellationToken);
        var effectId = "v2-oauth-exchange-" + Guid.NewGuid().ToString("N");
        var intent = new OAuthEffectIntent(effectId, OAuthEffectKind.Exchange, flow.Provider, flow.Key, codeRef, flow.VerifierRef, flow.ResultCredentialRef, 0, _clock.UtcNow, flow.ExpiresAt, flow.CorrelationId);
        var claimed = await flows.ClaimAndEnqueueAsync(flowKey, flow.Revision, codeRef, intent, _clock.UtcNow, cancellationToken);
        if (claimed.Status != OAuthFlowStatus.ExchangeQueued) return new OAuthCallbackResult(flowKey, claimed.Status, claimed.EffectId, claimed.ResultCredentialRef, true, "callback-already-claimed");
        return new OAuthCallbackResult(flowKey, claimed.Status, effectId, claimed.ResultCredentialRef, false);
    }
}
