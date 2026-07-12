using System.Net;
using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Core;
using DigitalBrain.Core.Config;
using DigitalBrain.Core.Runtime;
using DigitalBrain.Google;
using DigitalBrain.Kernel.Abstractions;
using DigitalBrain.Salesforce;

namespace DigitalBrain.Tests.Integrations;

public sealed class OAuthConnectorSecurityTests
{
    [Fact]
    public async Task Google_oauth_state_and_tokens_are_principal_scoped()
    {
        var store = new FakePackConfigStore();
        await store.SetAsync(GoogleClientFactory.DefaultScope, GoogleClientFactory.PackName, GoogleAppConfig());
        var handler = new StubTokenEndpointHandler(
            "{\"access_token\":\"access-a\",\"refresh_token\":\"refresh-a\",\"expires_in\":3600}");
        var connector = new GoogleConnector(store, new FakeOAuthStateProtector(), tokenEndpointHandler: handler);
        var first = new NeuronId("principal-a");
        var second = new NeuronId("principal-b");

        var firstChallenge = await connector.BeginAuthAsync(first);
        var secondChallenge = await connector.BeginAuthAsync(second);

        Assert.False(firstChallenge.IsForm);
        Assert.False(secondChallenge.IsForm);
        Assert.False(SameSecret(firstChallenge.State, secondChallenge.State));
        Assert.False(firstChallenge.State!.Contains(first.Value, StringComparison.Ordinal));
        Assert.False(secondChallenge.State!.Contains(second.Value, StringComparison.Ordinal));

        var malformed = await connector.CompleteAuthAsync(new OAuthCallback(Code: "code", State: "tampered"));
        var denied = await connector.CompleteAuthAsync(new OAuthCallback(Code: string.Empty, State: firstChallenge.State!, Error: "access_denied"));
        var replay = await connector.CompleteAuthAsync(new OAuthCallback(Code: "code", State: firstChallenge.State!));
        var freshChallenge = await connector.BeginAuthAsync(first);
        var completed = await connector.CompleteAuthAsync(new OAuthCallback(Code: "code", State: freshChallenge.State!));

        Assert.Equal("invalid-state", malformed.Error);
        Assert.Equal("consent-denied", denied.Error);
        Assert.Equal("no-pending", replay.Error);
        Assert.True(completed.Success);
        Assert.Equal(1, handler.CallCount);

        var firstTokens = await store.GetAsync(UserScope(first), GoogleClientFactory.PackName);
        var secondTokens = await store.GetAsync(UserScope(second), GoogleClientFactory.PackName);
        Assert.True(firstTokens.ContainsKey(GoogleClientFactory.RefreshTokenKey));
        Assert.Empty(secondTokens);
    }

    [Fact]
    public async Task Google_reuses_the_live_principal_attempt_instead_of_invalidating_the_first_action()
    {
        var store = new FakePackConfigStore();
        await store.SetAsync(GoogleClientFactory.DefaultScope, GoogleClientFactory.PackName, GoogleAppConfig());
        var connector = new GoogleConnector(store, new FakeOAuthStateProtector());
        var owner = new NeuronId("principal-coalesced-google");

        var first = await connector.BeginAuthAsync(owner);
        var firstPending = await store.GetAsync(UserScope(owner), GoogleClientFactory.OAuthPendingPackName);
        var second = await connector.BeginAuthAsync(owner);
        var secondPending = await store.GetAsync(UserScope(owner), GoogleClientFactory.OAuthPendingPackName);

        Assert.True(SameSecret(first.State, second.State), "The live Google attempt state changed.");
        Assert.True(SameSecret(first.UrlOrForm, second.UrlOrForm), "The live Google challenge changed.");
        Assert.True(SameSecretDictionary(firstPending, secondPending), "The live Google attempt was replaced.");
    }

    [Fact]
    public async Task Google_begin_never_mutates_app_owned_configuration()
    {
        var store = new FakePackConfigStore();
        await store.SetAsync(GoogleClientFactory.DefaultScope, GoogleClientFactory.PackName, GoogleAppConfig());
        var connector = new GoogleConnector(store, new FakeOAuthStateProtector());

        var challenge = await connector.BeginAuthAsync(
            new NeuronId("principal-google-read-only-config"),
            clientIdHint: "untrusted-client-hint");
        var appConfig = await store.GetAsync(GoogleClientFactory.DefaultScope, GoogleClientFactory.PackName);

        Assert.False(challenge.IsForm);
        Assert.Equal("client-id", appConfig[GoogleClientFactory.ClientIdKey]);
        Assert.Equal("client-secret", appConfig[GoogleClientFactory.ClientSecretKey]);
        Assert.Equal(GoogleClientFactory.DefaultRedirectUri, appConfig[GoogleClientFactory.RedirectUriKey]);
    }

    [Fact]
    public async Task Google_oauth_pins_redirect_and_keeps_app_owned_secret_for_the_started_flow()
    {
        var store = new FakePackConfigStore();
        await store.SetAsync(GoogleClientFactory.DefaultScope, GoogleClientFactory.PackName, GoogleAppConfig());
        var handler = new StubTokenEndpointHandler(
            "{\"access_token\":\"access-a\",\"refresh_token\":\"refresh-a\",\"expires_in\":3600}");
        var connector = new GoogleConnector(store, new FakeOAuthStateProtector(), tokenEndpointHandler: handler);
        var owner = new NeuronId("principal-google-pinned-config");
        var challenge = await connector.BeginAuthAsync(owner);
        await store.SetAsync(
            GoogleClientFactory.DefaultScope,
            GoogleClientFactory.PackName,
            new Dictionary<string, string>
            {
                [GoogleClientFactory.ClientIdKey] = "client-id",
                [GoogleClientFactory.ClientSecretKey] = "rotated-secret",
                [GoogleClientFactory.RedirectUriKey] = "http://localhost:9999/oauth/callback/google"
            });

        var completed = await connector.CompleteAuthAsync(new OAuthCallback("code", challenge.State!));

        Assert.True(completed.Success);
        Assert.True(handler.RequestBody.Contains("client_id=client-id", StringComparison.Ordinal));
        Assert.True(handler.RequestBody.Contains(
            "redirect_uri=" + Uri.EscapeDataString(GoogleClientFactory.DefaultRedirectUri),
            StringComparison.Ordinal));
        Assert.True(handler.RequestBody.Contains("client_secret=rotated-secret", StringComparison.Ordinal));
        Assert.False(handler.RequestBody.Contains("9999", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Google_oauth_rejects_client_rotation_during_an_in_flight_flow()
    {
        var store = new FakePackConfigStore();
        await store.SetAsync(GoogleClientFactory.DefaultScope, GoogleClientFactory.PackName, GoogleAppConfig());
        var handler = new StubTokenEndpointHandler(
            "{\"access_token\":\"access-a\",\"refresh_token\":\"refresh-a\",\"expires_in\":3600}");
        var connector = new GoogleConnector(store, new FakeOAuthStateProtector(), tokenEndpointHandler: handler);
        var owner = new NeuronId("principal-google-client-rotation");
        var challenge = await connector.BeginAuthAsync(owner);
        await store.SetAsync(
            GoogleClientFactory.DefaultScope,
            GoogleClientFactory.PackName,
            new Dictionary<string, string>
            {
                [GoogleClientFactory.ClientIdKey] = "rotated-client",
                [GoogleClientFactory.ClientSecretKey] = "rotated-secret",
                [GoogleClientFactory.RedirectUriKey] = GoogleClientFactory.DefaultRedirectUri
            });

        var completed = await connector.CompleteAuthAsync(new OAuthCallback("code", challenge.State!));

        Assert.False(completed.Success);
        Assert.Equal("configuration-changed", completed.Error);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Google_begin_supersedes_a_live_challenge_after_client_rotation()
    {
        var store = new FakePackConfigStore();
        await store.SetAsync(GoogleClientFactory.DefaultScope, GoogleClientFactory.PackName, GoogleAppConfig());
        var connector = new GoogleConnector(store, new FakeOAuthStateProtector());
        var owner = new NeuronId("principal-google-client-rotation-restart");
        var first = await connector.BeginAuthAsync(owner);
        await store.SetAsync(
            GoogleClientFactory.DefaultScope,
            GoogleClientFactory.PackName,
            new Dictionary<string, string>
            {
                [GoogleClientFactory.ClientIdKey] = "rotated-client",
                [GoogleClientFactory.ClientSecretKey] = "rotated-secret",
                [GoogleClientFactory.RedirectUriKey] = GoogleClientFactory.DefaultRedirectUri
            });

        var second = await connector.BeginAuthAsync(owner);
        var pending = await store.GetAsync(UserScope(owner), GoogleClientFactory.OAuthPendingPackName);

        Assert.False(SameSecret(first.State, second.State));
        Assert.Equal("rotated-client", pending[GoogleClientFactory.OAuthPendingClientIdKey]);
    }

    [Fact]
    public async Task Google_app_configuration_remains_authoritative_over_legacy_user_values()
    {
        var store = new FakePackConfigStore();
        await store.SetAsync(GoogleClientFactory.DefaultScope, GoogleClientFactory.PackName, GoogleAppConfig());
        var owner = new NeuronId("principal-google-app-authority");
        await store.SetAsync(
            UserScope(owner),
            GoogleClientFactory.PackName,
            new Dictionary<string, string>
            {
                [GoogleClientFactory.ClientIdKey] = "legacy-user-client",
                [GoogleClientFactory.ClientSecretKey] = "legacy-user-secret",
                [GoogleClientFactory.RedirectUriKey] = "http://localhost:9999/oauth/callback/google",
                [GoogleClientFactory.RefreshTokenKey] = "user-refresh"
            });

        var merged = await GoogleClientFactory.GetMergedScopedValuesAsync(
            store,
            new NeuronScope(new UserId(owner.Value), ThreadId: null));

        Assert.Equal("client-id", merged[GoogleClientFactory.ClientIdKey]);
        Assert.Equal("client-secret", merged[GoogleClientFactory.ClientSecretKey]);
        Assert.Equal(GoogleClientFactory.DefaultRedirectUri, merged[GoogleClientFactory.RedirectUriKey]);
        Assert.Equal("user-refresh", merged[GoogleClientFactory.RefreshTokenKey]);
    }

    [Fact]
    public async Task Google_non_caller_exchange_timeout_terminalizes_the_attempt()
    {
        var store = new FakePackConfigStore();
        await store.SetAsync(GoogleClientFactory.DefaultScope, GoogleClientFactory.PackName, GoogleAppConfig());
        var connector = new GoogleConnector(
            store,
            new FakeOAuthStateProtector(),
            tokenEndpointHandler: new TimeoutTokenEndpointHandler());
        var owner = new NeuronId("principal-google-timeout");
        var challenge = await connector.BeginAuthAsync(owner);

        var completed = await connector.CompleteAuthAsync(new OAuthCallback("code", challenge.State!));
        var pending = await store.GetAsync(UserScope(owner), GoogleClientFactory.OAuthPendingPackName);

        Assert.False(completed.Success);
        Assert.Equal("exchange-failed", completed.Error);
        Assert.Equal(GoogleClientFactory.OAuthPhaseFailed, pending[GoogleClientFactory.OAuthPhaseKey]);
        Assert.Equal("exchange-timeout", pending[GoogleClientFactory.OAuthResultKey]);
    }

    [Fact]
    public async Task Google_reconnect_does_not_certify_the_refresh_token_that_triggered_reconnect()
    {
        var store = new FakePackConfigStore();
        await store.SetAsync(GoogleClientFactory.DefaultScope, GoogleClientFactory.PackName, GoogleAppConfig());
        var owner = new NeuronId("principal-google-stale-refresh");
        await store.SetAsync(
            UserScope(owner),
            GoogleClientFactory.PackName,
            new Dictionary<string, string>
            {
                [GoogleClientFactory.RefreshTokenKey] = "known-bad-refresh-token",
                [GoogleClientFactory.OAuthCompletedFingerprintKey] =
                    GoogleClientFactory.AuthorizationAttemptFingerprint("prior-state")
            });
        var handler = new StubTokenEndpointHandler(
            "{\"access_token\":\"access-only\",\"expires_in\":3600}");
        var connector = new GoogleConnector(store, new FakeOAuthStateProtector(), tokenEndpointHandler: handler);
        var challenge = await connector.BeginAuthAsync(owner);

        var completed = await connector.CompleteAuthAsync(new OAuthCallback("code", challenge.State!));
        var credentials = await GoogleClientFactory.GetMergedScopedValuesAsync(
            store,
            new NeuronScope(new UserId(owner.Value), ThreadId: null));

        Assert.False(completed.Success);
        Assert.Equal("exchange-failed", completed.Error);
        Assert.False(credentials.ContainsKey(GoogleClientFactory.RefreshTokenKey));
        Assert.False(credentials.ContainsKey(GoogleClientFactory.OAuthCompletedFingerprintKey));
        Assert.False(credentials.ContainsKey(GoogleClientFactory.OAuthCompletedFlowIdKey));
    }

    [Fact]
    public async Task Google_oauth_completion_witness_survives_pending_cleanup_failure()
    {
        var store = new PendingClearFailureStore();
        await store.SetAsync(GoogleClientFactory.DefaultScope, GoogleClientFactory.PackName, GoogleAppConfig());
        var handler = new StubTokenEndpointHandler(
            "{\"access_token\":\"access-a\",\"refresh_token\":\"refresh-a\",\"expires_in\":3600}");
        var connector = new GoogleConnector(
            store,
            new FakeOAuthStateProtector(),
            tokenEndpointHandler: handler);
        var owner = new NeuronId("principal-cleanup-failure");
        var challenge = await connector.BeginAuthAsync(owner);
        store.FailNextPendingClear = true;

        var completed = await connector.CompleteAuthAsync(new OAuthCallback("code", challenge.State!));
        var credentials = await GoogleClientFactory.GetMergedScopedValuesAsync(
            store,
            new NeuronScope(new UserId(owner.Value), ThreadId: null));
        var pending = await store.GetAsync(UserScope(owner), GoogleClientFactory.OAuthPendingPackName);
        var replay = await connector.CompleteAuthAsync(new OAuthCallback("code", challenge.State!));
        var reconnect = await connector.BeginAuthAsync(owner);
        var reconnectedCredentials = await GoogleClientFactory.GetMergedScopedValuesAsync(
            store,
            new NeuronScope(new UserId(owner.Value), ThreadId: null));
        var reconnectedPending = await store.GetAsync(UserScope(owner), GoogleClientFactory.OAuthPendingPackName);

        Assert.True(completed.Success);
        Assert.True(replay.Success);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal("processing", pending[GoogleClientFactory.OAuthResultKey]);
        Assert.True(GoogleClientFactory.IsAuthorizationReady(credentials, pending));
        Assert.False(SameSecret(challenge.State, reconnect.State));
        Assert.Equal(
            ExternalAuthorizationResolutionState.Waiting,
            GoogleClientFactory.ResolveAuthorization(reconnectedCredentials, reconnectedPending).State);
    }

    [Fact]
    public void Google_terminal_and_abandoned_processing_states_are_not_left_waiting()
    {
        var denied = new Dictionary<string, string>
        {
            [GoogleClientFactory.OAuthResultKey] = "denied",
            [GoogleClientFactory.OAuthAttemptFingerprintKey] =
                GoogleClientFactory.AuthorizationAttemptFingerprint("denied-state")
        };
        var abandoned = new Dictionary<string, string>
        {
            [GoogleClientFactory.OAuthResultKey] = "processing",
            [GoogleClientFactory.OAuthAttemptFingerprintKey] =
                GoogleClientFactory.AuthorizationAttemptFingerprint("abandoned-state"),
            [GoogleClientFactory.OAuthProcessingExpiresAtKey] = "0"
        };

        Assert.Equal(
            ExternalAuthorizationResolutionState.Failed,
            GoogleClientFactory.ResolveAuthorization(new Dictionary<string, string>(), denied).State);
        Assert.Equal(
            ExternalAuthorizationResolutionState.Failed,
            GoogleClientFactory.ResolveAuthorization(new Dictionary<string, string>(), abandoned).State);
    }

    [Fact]
    public async Task Salesforce_oauth_roundtrip_sends_pkce_and_keeps_tokens_principal_scoped()
    {
        var store = new FakePackConfigStore();
        await store.SetAsync(PackConfigScopes.App, SalesforceClientFactory.PackName, SalesforceAppConfig());
        var handler = new StubTokenEndpointHandler(
            "{\"access_token\":\"access-a\",\"instance_url\":\"https://example.my.salesforce.com\",\"refresh_token\":\"refresh-a\",\"scope\":\"api refresh_token\"}");
        var connector = new SalesforceConnector(
            new FakeSalesforceApiClientFactory(),
            store,
            new FakeOAuthStateProtector(),
            tokenEndpointHandler: handler);
        var first = new NeuronId("principal-a");
        var second = new NeuronId("principal-b");

        var validation = await connector.ValidateConfigAsync(UserScope(first));
        var firstChallenge = await connector.BeginAuthAsync(first);
        var secondChallenge = await connector.BeginAuthAsync(second);

        Assert.True(validation.IsValid, validation.Message);
        Assert.False(firstChallenge.IsForm);
        Assert.False(secondChallenge.IsForm);
        Assert.False(SameSecret(firstChallenge.State, secondChallenge.State));
        Assert.False(firstChallenge.State!.Contains(first.Value, StringComparison.Ordinal));
        Assert.False(secondChallenge.State!.Contains(second.Value, StringComparison.Ordinal));
        var firstPending = await store.GetAsync(
            UserScope(first),
            SalesforceClientFactory.OAuthPendingPackName);
        Assert.True(firstPending.ContainsKey(SalesforceClientFactory.OAuthPendingExpiresAtKey));

        var malformed = await connector.CompleteAuthAsync(new OAuthCallback(Code: "code", State: "tampered"));
        var denied = await connector.CompleteAuthAsync(new OAuthCallback(Code: string.Empty, State: firstChallenge.State!, Error: "user_denied_authorization"));
        var replay = await connector.CompleteAuthAsync(new OAuthCallback(Code: "code", State: firstChallenge.State!));
        var freshChallenge = await connector.BeginAuthAsync(first);
        var completed = await connector.CompleteAuthAsync(new OAuthCallback(Code: "code", State: freshChallenge.State!));

        Assert.Equal("invalid-state", malformed.Error);
        Assert.Equal("consent-denied", denied.Error);
        Assert.Equal("no-pending", replay.Error);
        Assert.True(completed.Success);
        Assert.Equal(1, handler.CallCount);
        Assert.True(handler.RequestBody.Contains("code_verifier=", StringComparison.Ordinal));

        var firstTokens = await store.GetAsync(UserScope(first), SalesforceClientFactory.PackName);
        var secondTokens = await store.GetAsync(UserScope(second), SalesforceClientFactory.PackName);
        Assert.True(firstTokens.ContainsKey(SalesforceClientFactory.RefreshTokenKey));
        Assert.False(firstTokens.ContainsKey(SalesforceClientFactory.ClientIdKey));
        Assert.False(firstTokens.ContainsKey(SalesforceClientFactory.ClientSecretKey));
        Assert.False(firstTokens.ContainsKey(SalesforceClientFactory.RedirectUriKey));
        Assert.Empty(secondTokens);
    }

    [Fact]
    public async Task Salesforce_denial_cannot_release_a_wait_with_stale_credentials()
    {
        var store = new FakePackConfigStore();
        var owner = new NeuronId("principal-stale-salesforce");
        await store.SetAsync(PackConfigScopes.App, SalesforceClientFactory.PackName, SalesforceAppConfig());
        await store.SetAsync(UserScope(owner), SalesforceClientFactory.PackName, new Dictionary<string, string>
        {
            [SalesforceClientFactory.AccessTokenKey] = "stale-access",
            [SalesforceClientFactory.InstanceUrlKey] = "https://example.my.salesforce.com"
        });
        var connector = new SalesforceConnector(
            new FakeSalesforceApiClientFactory(),
            store,
            new FakeOAuthStateProtector());
        var challenge = await connector.BeginAuthAsync(owner);

        var denied = await connector.CompleteAuthAsync(new OAuthCallback(
            Code: string.Empty,
            State: challenge.State!,
            Error: "access_denied"));
        var credentials = await SalesforceClientFactory.GetMergedScopedValuesAsync(
            store,
            new NeuronScope(new UserId(owner.Value), ThreadId: null));
        var pending = await store.GetAsync(
            UserScope(owner),
            SalesforceClientFactory.OAuthPendingPackName);

        Assert.Equal("consent-denied", denied.Error);
        Assert.Equal(
            ExternalAuthorizationResolutionState.Failed,
            SalesforceClientFactory.ResolveAuthorization(credentials, pending).State);
    }

    [Fact]
    public async Task Salesforce_oauth_completion_witness_survives_pending_cleanup_failure()
    {
        var store = new PendingClearFailureStore();
        await store.SetAsync(PackConfigScopes.App, SalesforceClientFactory.PackName, SalesforceAppConfig());
        var handler = new StubTokenEndpointHandler(
            "{\"access_token\":\"access-a\",\"instance_url\":\"https://example.my.salesforce.com\",\"refresh_token\":\"refresh-a\"}");
        var connector = new SalesforceConnector(
            new FakeSalesforceApiClientFactory(),
            store,
            new FakeOAuthStateProtector(),
            tokenEndpointHandler: handler);
        var owner = new NeuronId("principal-salesforce-cleanup-failure");
        var challenge = await connector.BeginAuthAsync(owner);
        store.FailNextPendingClear = true;

        var completed = await connector.CompleteAuthAsync(new OAuthCallback("code", challenge.State!));
        var credentials = await SalesforceClientFactory.GetMergedScopedValuesAsync(
            store,
            new NeuronScope(new UserId(owner.Value), ThreadId: null));
        var pending = await store.GetAsync(
            UserScope(owner),
            SalesforceClientFactory.OAuthPendingPackName);
        var replay = await connector.CompleteAuthAsync(new OAuthCallback("code", challenge.State!));

        Assert.True(completed.Success);
        Assert.True(replay.Success);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal("processing", pending[SalesforceClientFactory.OAuthResultKey]);
        Assert.Equal(
            ExternalAuthorizationResolutionState.Ready,
            SalesforceClientFactory.ResolveAuthorization(credentials, pending).State);
    }

    [Fact]
    public async Task Salesforce_oauth_pins_login_and_redirect_for_the_started_flow()
    {
        var store = new FakePackConfigStore();
        var initial = new Dictionary<string, string>(SalesforceAppConfig())
        {
            [SalesforceClientFactory.LoginUrlKey] = "https://test.salesforce.com"
        };
        await store.SetAsync(PackConfigScopes.App, SalesforceClientFactory.PackName, initial);
        var handler = new StubTokenEndpointHandler(
            "{\"access_token\":\"access-a\",\"instance_url\":\"https://example.my.salesforce.com\",\"refresh_token\":\"refresh-a\"}");
        var connector = new SalesforceConnector(
            new FakeSalesforceApiClientFactory(),
            store,
            new FakeOAuthStateProtector(),
            tokenEndpointHandler: handler);
        var owner = new NeuronId("principal-pinned-flow");
        var challenge = await connector.BeginAuthAsync(owner);
        var changed = new Dictionary<string, string>(initial)
        {
            [SalesforceClientFactory.LoginUrlKey] = "https://login.salesforce.com",
            [SalesforceClientFactory.RedirectUriKey] = "http://localhost:9090/oauth/callback/salesforce"
        };
        await store.SetAsync(PackConfigScopes.App, SalesforceClientFactory.PackName, changed);

        var result = await connector.CompleteAuthAsync(new OAuthCallback("code", challenge.State!));

        Assert.True(result.Success);
        Assert.Equal("test.salesforce.com", handler.RequestUri?.Host);
        Assert.True(handler.RequestBody.Contains(
            "redirect_uri=" + Uri.EscapeDataString(SalesforceClientFactory.DefaultRedirectUri),
            StringComparison.Ordinal));
        Assert.False(handler.RequestBody.Contains("9090", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Salesforce_app_owned_configuration_cannot_be_overridden_by_legacy_user_values()
    {
        var store = new FakePackConfigStore();
        var owner = new NeuronId("principal-legacy-config");
        await store.SetAsync(PackConfigScopes.App, SalesforceClientFactory.PackName, SalesforceAppConfig());
        await store.SetAsync(UserScope(owner), SalesforceClientFactory.PackName, new Dictionary<string, string>
        {
            [SalesforceClientFactory.ClientIdKey] = "stale-client-id",
            [SalesforceClientFactory.ClientSecretKey] = "stale-client-secret",
            [SalesforceClientFactory.LoginUrlKey] = "https://stale.my.salesforce.com",
            [SalesforceClientFactory.RedirectUriKey] = "http://localhost:9999/oauth/callback/salesforce",
            [SalesforceClientFactory.AccessTokenKey] = "user-access-token",
            [SalesforceClientFactory.InstanceUrlKey] = "https://example.my.salesforce.com"
        });

        var merged = await SalesforceClientFactory.GetMergedScopedValuesAsync(
            store,
            new NeuronScope(new UserId(owner.Value), ThreadId: null));

        Assert.Equal("client-id", merged[SalesforceClientFactory.ClientIdKey]);
        Assert.Equal("client-secret", merged[SalesforceClientFactory.ClientSecretKey]);
        Assert.Equal(SalesforceClientFactory.DefaultLoginUrl, merged[SalesforceClientFactory.LoginUrlKey]);
        Assert.Equal(SalesforceClientFactory.DefaultRedirectUri, merged[SalesforceClientFactory.RedirectUriKey]);
        Assert.Equal("user-access-token", merged[SalesforceClientFactory.AccessTokenKey]);
    }

    [Fact]
    public async Task Salesforce_scoped_validation_uses_app_config_and_terminalizes_expired_pending_state()
    {
        var store = new FakePackConfigStore();
        var owner = new NeuronId("principal-expired-pending");
        var userScope = UserScope(owner);
        await store.SetAsync(PackConfigScopes.App, SalesforceClientFactory.PackName, SalesforceAppConfig());
        await store.SetAsync(userScope, SalesforceClientFactory.OAuthPendingPackName, new Dictionary<string, string>
        {
            [SalesforceClientFactory.OAuthStateKey] = "expired-state",
            [SalesforceClientFactory.OAuthCodeVerifierKey] = "expired-verifier",
            [SalesforceClientFactory.OAuthPendingExpiresAtKey] = "0"
        });
        var connector = new SalesforceConnector(
            new FakeSalesforceApiClientFactory(),
            store,
            new FakeOAuthStateProtector());

        var validation = await connector.ValidateConfigAsync(userScope);
        var pending = await store.GetAsync(userScope, SalesforceClientFactory.OAuthPendingPackName);

        Assert.True(validation.IsValid, validation.Message);
        Assert.Equal(SalesforceClientFactory.OAuthPhaseFailed, pending[SalesforceClientFactory.OAuthPhaseKey]);
        Assert.Equal("expired", pending[SalesforceClientFactory.OAuthResultKey]);
        Assert.False(pending.ContainsKey(SalesforceClientFactory.OAuthStateKey));
        Assert.False(pending.ContainsKey(SalesforceClientFactory.OAuthCodeVerifierKey));
        Assert.False(pending.ContainsKey(SalesforceClientFactory.OAuthAuthorizationUrlKey));
        Assert.False(pending.ContainsKey(SalesforceClientFactory.OAuthStartTokenKey));
    }

    private static string UserScope(NeuronId owner) => PackConfigScopes.ForUser(new UserId(owner.Value));

    private static bool SameSecret(string? left, string? right)
    {
        if (left is null || right is null)
            return left is null && right is null;
        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(left)),
            SHA256.HashData(Encoding.UTF8.GetBytes(right)));
    }

    private static bool SameSecretDictionary(
        IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right)
    {
        if (left.Count != right.Count)
            return false;
        foreach (var (key, value) in left)
        {
            if (!right.TryGetValue(key, out var other) || !SameSecret(value, other))
                return false;
        }
        return true;
    }

    private static IReadOnlyDictionary<string, string> GoogleAppConfig() => new Dictionary<string, string>
    {
        [GoogleClientFactory.ClientIdKey] = "client-id",
        [GoogleClientFactory.ClientSecretKey] = "client-secret",
        [GoogleClientFactory.RedirectUriKey] = GoogleClientFactory.DefaultRedirectUri
    };

    private static IReadOnlyDictionary<string, string> SalesforceAppConfig() => new Dictionary<string, string>
    {
        [SalesforceClientFactory.ClientIdKey] = "client-id",
        [SalesforceClientFactory.ClientSecretKey] = "client-secret",
        [SalesforceClientFactory.RedirectUriKey] = SalesforceClientFactory.DefaultRedirectUri,
        [SalesforceClientFactory.LoginUrlKey] = SalesforceClientFactory.DefaultLoginUrl,
        [SalesforceClientFactory.ApiVersionKey] = SalesforceClientFactory.DefaultApiVersion
    };

    private sealed class StubTokenEndpointHandler(string responseBody) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public string RequestBody { get; private set; } = string.Empty;
        public Uri? RequestUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            RequestUri = request.RequestUri;
            RequestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class TimeoutTokenEndpointHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(new TaskCanceledException("Simulated provider timeout."));
    }

    private sealed class PendingClearFailureStore : IPackConfigStore
    {
        private readonly Dictionary<(string Scope, string Pack), Dictionary<string, string>> _data = [];
        public bool FailNextPendingClear { get; set; }

        public Task SetAsync(
            string scope,
            string pack,
            IReadOnlyDictionary<string, string> values,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FailNextPendingClear &&
                (string.Equals(pack, GoogleClientFactory.OAuthPendingPackName, StringComparison.Ordinal) ||
                 string.Equals(pack, SalesforceClientFactory.OAuthPendingPackName, StringComparison.Ordinal)) &&
                values.Count == 0)
            {
                FailNextPendingClear = false;
                throw new IOException("Injected pending cleanup failure.");
            }
            _data[(scope, pack)] = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<string, string>> GetAsync(
            string scope,
            string pack,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyDictionary<string, string>>(
                _data.TryGetValue((scope, pack), out var values)
                    ? new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }
    }
}
