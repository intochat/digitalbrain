using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Core;
using DigitalBrain.Core.Config;
using DigitalBrain.Core.Runtime;
using DigitalBrain.Kernel.Abstractions;
using DigitalBrain.Kernel.Runtime;
using DigitalBrain.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Salesforce.Tests;

public sealed class SalesforceOAuthStartNeuronTests : NeuronTestBase
{
    private readonly StatefulSalesforceConfigStore _store = new();
    private readonly SalesforceTestOAuthStateProtector _protector = new();
    private readonly RecordingSalesforceConnector _connector;

    public SalesforceOAuthStartNeuronTests()
    {
        _connector = new RecordingSalesforceConnector(new SalesforceConnector(
            new UnusedSalesforceApiClientFactory(),
            _store,
            _protector,
            new SuccessfulTokenEndpointHandler()));
    }

    protected override void ConfigureSilo(ISiloBuilder builder) =>
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<ISalesforceApiClientFactory>(new UnusedSalesforceApiClientFactory());
            services.AddSingleton<IPackConfigStore>(_store);
            services.AddSingleton<IOAuthStateProtector>(_protector);
            services.AddKeyedSingleton<IConnector>("salesforce", _connector);
        });

    [Fact]
    public async Task OAuth_start_is_persistent_and_provider_redirect_is_idempotent()
    {
        var grain = Grain<ISalesforceReadToolGrain>("principal-oauth-start");

        var disconnected = await grain.ReadRecordsAsync(
            new SalesforceRecordReadRequest(new SalesforceSemanticEntity("Accounts")));

        Assert.Equal(SalesforceReadStatus.NeedsAuth, disconnected.Status);
        var localStartUrl = Assert.IsType<string>(disconnected.ConnectionUrl);
        var token = FlowReference(localStartUrl);
        Assert.Equal($"{OAuthCallbackPaths.SalesforceStart}?f={token}", localStartUrl);
        Assert.False(localStartUrl.Contains("services/oauth2/authorize", StringComparison.OrdinalIgnoreCase));
        Assert.False(localStartUrl.Contains("state=", StringComparison.OrdinalIgnoreCase));
        Assert.False(localStartUrl.Contains("client_id=", StringComparison.OrdinalIgnoreCase));
        Assert.False(localStartUrl.Contains("code_challenge=", StringComparison.OrdinalIgnoreCase));
        Assert.False(token.Contains("principal-oauth-start", StringComparison.Ordinal));

        var beforeClick = await grain.ResolveAuthorizationAsync();
        Assert.Equal(ExternalAuthorizationResolutionState.Waiting, beforeClick.State);

        await Cluster.DeactivateAsync(grain);

        var authorization = await grain.BeginAuthorizationAsync(token);

        Assert.Equal(SalesforceReadStatus.NeedsAuth, authorization.Status);
        var providerUrl = Assert.IsType<string>(authorization.ConnectionUrl);
        Assert.True(SalesforceClientFactory.IsAllowedAuthorizationUrl(providerUrl));
        Assert.True(providerUrl.Contains("/services/oauth2/authorize", StringComparison.Ordinal));
        Assert.Equal(1, _connector.BeginAuthCallCount);
        var providerPending = _store.PendingSnapshot("principal-oauth-start");

        await Cluster.DeactivateAsync(grain);
        var replay = await grain.BeginAuthorizationAsync(token);

        Assert.Equal(SalesforceReadStatus.NeedsAuth, replay.Status);
        Assert.True(SameSecret(providerUrl, replay.ConnectionUrl), "The persisted provider challenge changed.");
        Assert.Equal(2, _connector.BeginAuthCallCount);
        Assert.True(
            SameSecretDictionary(providerPending, _store.PendingSnapshot("principal-oauth-start")),
            "The persisted provider attempt changed.");
    }

    [Fact]
    public async Task Provider_pending_atomically_supersedes_local_start_and_callback_survives_reactivation()
    {
        var grain = Grain<ISalesforceReadToolGrain>("principal-oauth-crash-boundary");
        var disconnected = await grain.ReadRecordsAsync(
            new SalesforceRecordReadRequest(new SalesforceSemanticEntity("Accounts")));
        var localStartUrl = Assert.IsType<string>(disconnected.ConnectionUrl);
        var token = FlowReference(localStartUrl);

        var authorization = await grain.BeginAuthorizationAsync(token);
        Assert.Equal(SalesforceReadStatus.NeedsAuth, authorization.Status);
        Assert.True(_store.HasLocalStart("principal-oauth-crash-boundary"));
        Assert.True(_store.HasProviderPending("principal-oauth-crash-boundary"));
        var providerState = _store.ProviderState("principal-oauth-crash-boundary");

        await Cluster.DeactivateAsync(grain);

        var completion = await grain.CompleteAuthorizationAsync(
            new OAuthCallback("code", providerState));
        Assert.True(completion.Success);

        await Cluster.DeactivateAsync(grain);
        var resolution = await grain.ResolveAuthorizationAsync();

        Assert.Equal(ExternalAuthorizationResolutionState.Ready, resolution.State);
    }

    [Fact]
    public async Task Late_callback_after_success_and_expired_pending_residue_returns_completed_result()
    {
        const string principal = "principal-oauth-late-callback";
        var grain = Grain<ISalesforceReadToolGrain>(principal);
        var disconnected = await grain.ReadRecordsAsync(
            new SalesforceRecordReadRequest(new SalesforceSemanticEntity("Accounts")));
        var token = FlowReference(disconnected.ConnectionUrl);
        await grain.BeginAuthorizationAsync(token);
        var providerState = _store.ProviderState(principal);
        var pending = _store.PendingSnapshot(principal);

        Assert.True((await grain.CompleteAuthorizationAsync(new OAuthCallback("code", providerState))).Success);
        _store.RestoreExpiredPendingResidue(principal, pending);
        await Cluster.DeactivateAsync(grain);

        var replay = await grain.CompleteAuthorizationAsync(new OAuthCallback("same-code", providerState));

        Assert.True(replay.Success);
    }

    [Fact]
    public async Task Second_read_coalesces_live_provider_attempt_without_replacing_pkce_state()
    {
        var grain = Grain<ISalesforceReadToolGrain>("principal-oauth-coalesce");
        var firstRead = await grain.ReadRecordsAsync(
            new SalesforceRecordReadRequest(new SalesforceSemanticEntity("Accounts")));
        var firstStartUrl = Assert.IsType<string>(firstRead.ConnectionUrl);
        var token = FlowReference(firstStartUrl);
        var firstChallenge = await grain.BeginAuthorizationAsync(token);
        var pendingBeforeSecondRead = _store.PendingSnapshot("principal-oauth-coalesce");

        var secondRead = await grain.ReadRecordsAsync(
            new SalesforceRecordReadRequest(new SalesforceSemanticEntity("Accounts")));

        Assert.True(SameSecret(firstStartUrl, secondRead.ConnectionUrl), "The local OAuth action changed.");
        Assert.True(
            SameSecretDictionary(pendingBeforeSecondRead, _store.PendingSnapshot("principal-oauth-coalesce")),
            "The provider attempt changed during a coalesced read.");
        var completion = await grain.CompleteAuthorizationAsync(
            new OAuthCallback("code", _store.ProviderState("principal-oauth-coalesce")));
        Assert.True(completion.Success);
        Assert.True(SalesforceClientFactory.IsAllowedAuthorizationUrl(firstChallenge.ConnectionUrl));

        await Cluster.DeactivateAsync(grain);
        Assert.Equal(
            ExternalAuthorizationResolutionState.Ready,
            (await grain.ResolveAuthorizationAsync()).State);
    }

    private static bool SameSecret(string? left, string? right)
    {
        if (left is null || right is null)
            return left is null && right is null;
        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(left)),
            SHA256.HashData(Encoding.UTF8.GetBytes(right)));
    }

    private static string FlowReference(string? target)
    {
        var value = Assert.IsType<string>(target);
        Assert.True(OAuthCallbackPaths.TryParseInternalStartPath(
            value,
            OAuthCallbackPaths.SalesforceProvider,
            out var flowReference));
        return flowReference;
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

    private sealed class RecordingSalesforceConnector(SalesforceConnector inner) : IConnector
    {
        private int _beginAuthCallCount;

        public int BeginAuthCallCount => Volatile.Read(ref _beginAuthCallCount);

        public ConnectorDescriptor Descriptor => inner.Descriptor;

        public Task<ConnectorConfigStatus> ValidateConfigAsync(
            string? userScope = null,
            CancellationToken cancellationToken = default) =>
            inner.ValidateConfigAsync(userScope, cancellationToken);

        public async Task<AuthChallenge> BeginAuthAsync(
            NeuronId user,
            string? clientIdHint = null,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _beginAuthCallCount);
            return await inner.BeginAuthAsync(user, clientIdHint, cancellationToken);
        }

        public Task<AuthResult> CompleteAuthAsync(
            OAuthCallback callback,
            CancellationToken cancellationToken = default) =>
            inner.CompleteAuthAsync(callback, cancellationToken);

        public Task<ConnectionHealth> TestConnectionAsync(
            NeuronId user,
            CancellationToken cancellationToken = default) =>
            inner.TestConnectionAsync(user, cancellationToken);
    }

    private sealed class StatefulSalesforceConfigStore : IPackConfigStore
    {
        private static readonly IReadOnlyDictionary<string, string> AppConfig =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [SalesforceClientFactory.ClientIdKey] = "test-client",
                [SalesforceClientFactory.ClientSecretKey] = "test-secret"
            };

        private static readonly IReadOnlyDictionary<string, string> Empty =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly IReadOnlyDictionary<string, string> PriorTerminalAuthorization =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [SalesforceClientFactory.OAuthResultKey] = "denied",
                [SalesforceClientFactory.OAuthAttemptFingerprintKey] =
                    SalesforceClientFactory.AuthorizationAttemptFingerprint("prior-state")
            };
        private readonly ConcurrentDictionary<(string Scope, string Pack), IReadOnlyDictionary<string, string>> _values = new();

        public Task SetAsync(
            string scope,
            string pack,
            IReadOnlyDictionary<string, string> values,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _values[(scope, pack)] = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<string, string>> GetAsync(
            string scope,
            string pack,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_values.TryGetValue((scope, pack), out var persisted))
                return Task.FromResult(persisted);
            return Task.FromResult(
                string.Equals(scope, PackConfigScopes.App, StringComparison.Ordinal) &&
                string.Equals(pack, SalesforceClientFactory.PackName, StringComparison.Ordinal)
                    ? AppConfig
                    : string.Equals(pack, SalesforceClientFactory.OAuthPendingPackName, StringComparison.Ordinal)
                        ? PriorTerminalAuthorization
                    : Empty);
        }

        public bool HasLocalStart(string principal) =>
            ReadPending(principal).ContainsKey(SalesforceClientFactory.OAuthStartTokenFingerprintKey);

        public bool HasProviderPending(string principal) =>
            ReadPending(principal).ContainsKey(SalesforceClientFactory.OAuthStateKey);

        public string ProviderState(string principal) =>
            Assert.IsType<string>(ReadPending(principal).GetValueOrDefault(SalesforceClientFactory.OAuthStateKey));

        public IReadOnlyDictionary<string, string> PendingSnapshot(string principal) =>
            new Dictionary<string, string>(ReadPending(principal), StringComparer.OrdinalIgnoreCase);

        public void RestoreExpiredPendingResidue(
            string principal,
            IReadOnlyDictionary<string, string> pending)
        {
            var residue = new Dictionary<string, string>(pending, StringComparer.OrdinalIgnoreCase)
            {
                [SalesforceClientFactory.OAuthPendingExpiresAtKey] = DateTimeOffset.UtcNow
                    .AddMinutes(-1)
                    .ToUnixTimeSeconds()
                    .ToString(System.Globalization.CultureInfo.InvariantCulture)
            };
            _values[(
                PackConfigScopes.ForUser(new UserId(principal)),
                SalesforceClientFactory.OAuthPendingPackName)] = residue;
        }

        private IReadOnlyDictionary<string, string> ReadPending(string principal) =>
            _values.GetValueOrDefault((
                PackConfigScopes.ForUser(new UserId(principal)),
                SalesforceClientFactory.OAuthPendingPackName),
                Empty);
    }

    private sealed class SuccessfulTokenEndpointHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"access_token\":\"access-a\",\"instance_url\":\"https://example.my.salesforce.com\",\"refresh_token\":\"refresh-a\"}",
                    Encoding.UTF8,
                    "application/json")
            });
        }
    }

    private sealed class UnusedSalesforceApiClientFactory : ISalesforceApiClientFactory
    {
        public Task<ISalesforceApiClient> CreateAsync(
            NeuronScope scope,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("A disconnected read must not create a Salesforce API client.");
    }
}

internal sealed class SalesforceTestOAuthStateProtector : IOAuthStateProtector
{
    private readonly ConcurrentDictionary<string, string> _owners = new(StringComparer.Ordinal);

    public string Protect(NeuronId owner)
    {
        var token = "opaque-" + Guid.NewGuid().ToString("N");
        _owners[token] = owner.Value;
        return token;
    }

    public bool TryUnprotect(string state, out NeuronId owner)
    {
        if (_owners.TryGetValue(state, out var ownerValue))
        {
            owner = new NeuronId(ownerValue);
            return true;
        }

        owner = default!;
        return false;
    }
}
