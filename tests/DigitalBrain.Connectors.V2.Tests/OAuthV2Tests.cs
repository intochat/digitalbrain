using DigitalBrain.Infrastructure.Connectors.V2;
using Xunit;

namespace DigitalBrain.Connectors.V2.Tests;

public sealed class OAuthV2Tests
{
    [Fact]
    public void State_is_versioned_random_and_flow_key_is_server_derived()
    {
        var ring = new OAuthStateKeyRing(1, new Dictionary<int, byte[]> { [1] = new byte[32] });
        var state = ring.CreateState();
        Assert.StartsWith("v1.", state);
        Assert.NotEqual(ring.DeriveFlowKey(state).Digest, ring.DeriveFlowKey(ring.CreateState()).Digest);
    }

    [Fact]
    public void Pkce_uses_s256()
    {
        var verifier = Pkce.CreateVerifier();
        var challenge = Pkce.CreateS256Challenge(verifier);
        Assert.NotEqual(verifier, challenge);
        Assert.NotEmpty(challenge);
    }

    [Fact]
    public async Task Encrypted_v2_secrets_are_scope_bound_and_not_plaintext()
    {
        var repository = new InMemoryEncryptedSecretBlobRepository();
        var vault = new EncryptedSecretVault(1, new Dictionary<int, byte[]> { [1] = new byte[32] }, repository);
        var owner = new CredentialOwner(new("tenant"), new("workspace"), new("user", DigitalBrain.Core.V2.PrincipalKind.User));
        var reference = await vault.WriteAsync(owner, "token", new SecretPayload(new Dictionary<string, string> { ["access_token"] = "canary-secret" }), null, default);
        Assert.NotNull(await vault.ReadAsync(owner, reference, default));
        var other = new CredentialOwner(new("tenant"), new("other"), new("user", DigitalBrain.Core.V2.PrincipalKind.User));
        Assert.Null(await vault.ReadAsync(other, reference, default));
        Assert.DoesNotContain("canary-secret", repository.Snapshot.Select(x => Convert.ToBase64String(x.Ciphertext)));
    }

    [Fact]
    public void Connector_policy_requires_scope_approval_and_exact_owner()
    {
        var adapter = new GoogleV2OAuthAdapter("client", "secret", "https://app/callback");
        var registry = new V2ProviderOAuthAdapterRegistry([adapter]);
        var policy = new V2ConnectorAuthorizationPolicy(registry);
        var context = new DigitalBrain.Core.V2.RequestContext(new("t"), new("w"), new("u", DigitalBrain.Core.V2.PrincipalKind.User), "s", DigitalBrain.Core.V2.AuthAssurance.Password, "c", null, new HashSet<string> { "gmail.send", "brain.approve" });
        var credential = new CredentialRecord(new("cred"), 1, "google", CredentialOwner.From(context), CredentialStatus.Connected, new SecretRef("secret", 1, "token"), ["https://www.googleapis.com/auth/gmail.send"], "account", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null, null, null);
        policy.DemandAuthorize(context, "google", ["gmail.send"]);
        policy.DemandUse(context, credential, adapter.Capabilities.Single(x => x.Id == "gmail.send"));
        Assert.Throws<UnauthorizedAccessException>(() => policy.DemandUse(context with { WorkspaceId = new("other") }, credential, adapter.Capabilities.Single(x => x.Id == "gmail.send")));
    }

    [Fact]
    public async Task OAuth_coordinator_claims_callback_once_and_persists_only_secret_refs()
    {
        var adapter = new GoogleV2OAuthAdapter("client", "secret", "https://app/callback");
        var policy = new V2ConnectorAuthorizationPolicy(new V2ProviderOAuthAdapterRegistry([adapter]));
        var vault = new MemoryVault();
        var flows = new InMemoryOAuthFlowStore();
        var coordinator = new V2OAuthCoordinator(new OAuthStateKeyRing(1, new Dictionary<int, byte[]> { [1] = new byte[32] }), new V2ProviderOAuthAdapterRegistry([adapter]), policy, flows, vault);
        var context = new DigitalBrain.Core.V2.RequestContext(new("t"), new("w"), new("u", DigitalBrain.Core.V2.PrincipalKind.User), "s", DigitalBrain.Core.V2.AuthAssurance.Password, "corr", null, new HashSet<string> { "gmail.read", "connector:google" });
        var begin = await coordinator.BeginAsync(new BeginOAuthRequest(context, "google", ["gmail.read"], "https://app/callback", TimeSpan.FromMinutes(5)));
        var callback = new OAuthCallbackRequest("google", begin.State, "https://app/callback", "auth-code", null, null);
        var completed = await coordinator.CompleteAsync(callback);
        var duplicate = await coordinator.CompleteAsync(callback);
        Assert.Equal(OAuthFlowStatus.ExchangeQueued, completed.Status);
        Assert.True(duplicate.Duplicate);
        var flow = await flows.GetAsync(begin.FlowKey, default);
        Assert.DoesNotContain("auth-code", System.Text.Json.JsonSerializer.Serialize(flow));
    }

    [Fact]
    public async Task OAuth_exchange_processor_creates_scoped_credential_and_succeeds()
    {
        var adapter = new FakeSuccessAdapter();
        var registry = new V2ProviderOAuthAdapterRegistry([adapter]);
        var policy = new V2ConnectorAuthorizationPolicy(registry);
        var vault = new MemoryVault();
        var flows = new InMemoryOAuthFlowStore();
        var context = new DigitalBrain.Core.V2.RequestContext(new("t"), new("w"), new("u", DigitalBrain.Core.V2.PrincipalKind.User), "s", DigitalBrain.Core.V2.AuthAssurance.Password, "c", null, new HashSet<string> { "fake.read", "connector:fake" });
        var coordinator = new V2OAuthCoordinator(new OAuthStateKeyRing(1, new Dictionary<int, byte[]> { [1] = new byte[32] }), registry, policy, flows, vault);
        var begin = await coordinator.BeginAsync(new BeginOAuthRequest(context, "fake", ["fake.read"], "https://app/callback", TimeSpan.FromMinutes(5)));
        var callback = await coordinator.CompleteAsync(new OAuthCallbackRequest("fake", begin.State, "https://app/callback", "code", null, null));
        var credentialStore = new V2CredentialStore(new InMemoryCredentialMetadataRepository(), vault);
        var result = await new V2OAuthExchangeProcessor(flows, vault, registry, credentialStore).ProcessAsync(begin.FlowKey, callback.EffectId!, "worker", TimeSpan.FromMinutes(1));
        Assert.Equal(OAuthFlowStatus.Succeeded, result!.Status);
        Assert.NotNull(await credentialStore.GetAsync(CredentialOwner.From(context), callback.CredentialRef!.Value, default));
    }

    [Fact]
    public async Task Credential_processor_rotates_refresh_token_and_revokes()
    {
        var adapter = new FakeSuccessAdapter();
        var registry = new V2ProviderOAuthAdapterRegistry([adapter]);
        var vault = new MemoryVault();
        var credentialStore = new V2CredentialStore(new InMemoryCredentialMetadataRepository(), vault);
        var owner = new CredentialOwner(new("t"), new("w"), new("u", DigitalBrain.Core.V2.PrincipalKind.User));
        var reference = new CredentialRef("cred");
        await credentialStore.CreateFromExchangeAsync(reference, "fake", owner, new ProviderTokenSet(new SecretPayload(new Dictionary<string, string> { [OAuthSecretNames.RefreshToken] = "refresh" }), ["fake.read"], "Bearer", "account", null), default);
        var processor = new V2OAuthCredentialProcessor(credentialStore, registry);
        var refreshed = await processor.RefreshAsync(owner, reference, "worker", TimeSpan.FromMinutes(1));
        Assert.Equal(CredentialStatus.Connected, refreshed!.Status);
        Assert.True(await processor.RevokeAsync(owner, reference, "worker", TimeSpan.FromMinutes(1)));
        Assert.Equal(CredentialStatus.Revoked, (await credentialStore.GetAsync(owner, reference, default))!.Status);
    }

    private sealed class MemoryVault : ISecretVault
    {
        private readonly Dictionary<SecretRef, SecretPayload> _values = [];
        public Task<SecretRef> WriteAsync(CredentialOwner owner, string purpose, SecretPayload payload, DateTimeOffset? expiresAt, CancellationToken cancellationToken)
        {
            var reference = new SecretRef(Guid.NewGuid().ToString("N"), 1, purpose);
            _values[reference] = payload;
            return Task.FromResult(reference);
        }
        public Task<SecretPayload?> ReadAsync(CredentialOwner owner, SecretRef reference, CancellationToken cancellationToken) => Task.FromResult(_values.TryGetValue(reference, out var payload) ? payload : null);
        public Task RetireAsync(CredentialOwner owner, SecretRef reference, CancellationToken cancellationToken) { _values.Remove(reference); return Task.CompletedTask; }
    }

    private sealed class FakeSuccessAdapter : IProviderOAuthAdapter
    {
        public string ProviderId => "fake";
        public IReadOnlyList<ConnectorCapabilityDescriptor> Capabilities { get; } = [new("fake.read", 2, "fake", ["fake.read"], ConnectorRiskClass.Read, false, true, "internal")];
        public bool IsAllowedRedirectUri(string redirectUri) => redirectUri == "https://app/callback";
        public Uri CreateAuthorizationUri(OAuthAuthorizationRequest request) => new("https://app/authorize");
        public Task<ProviderCallResult<ProviderTokenSet>> ExchangeAsync(OAuthExchangeRequest request, CancellationToken cancellationToken) => Task.FromResult(ProviderCallResult<ProviderTokenSet>.Success(new ProviderTokenSet(new SecretPayload(new Dictionary<string, string> { [OAuthSecretNames.AccessToken] = "access" }), ["fake.read"], "Bearer", "account", null)));
        public Task<ProviderCallResult<ProviderTokenSet>> RefreshAsync(OAuthRefreshRequest request, CancellationToken cancellationToken) => Task.FromResult(ProviderCallResult<ProviderTokenSet>.Success(new ProviderTokenSet(new SecretPayload(new Dictionary<string, string> { [OAuthSecretNames.AccessToken] = "refreshed", [OAuthSecretNames.RefreshToken] = "rotated" }), ["fake.read"], "Bearer", "account", null)));
        public Task<ProviderCallResult<bool>> RevokeAsync(OAuthRevokeRequest request, CancellationToken cancellationToken) => Task.FromResult(ProviderCallResult<bool>.Success(true));
    }
}
