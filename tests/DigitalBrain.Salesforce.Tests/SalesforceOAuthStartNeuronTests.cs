using System.Collections.Concurrent;
using DigitalBrain.Core;
using DigitalBrain.Core.Config;
using DigitalBrain.Kernel.Abstractions;
using DigitalBrain.Kernel.V2;
using DigitalBrain.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Salesforce.Tests;

public sealed class SalesforceOAuthStartNeuronTests : NeuronTestBase
{
    private readonly RecordingSalesforceConnector _connector = new();

    protected override void ConfigureSilo(ISiloBuilder builder) =>
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<ISalesforceApiClientFactory>(new UnusedSalesforceApiClientFactory());
            services.AddSingleton<IPackConfigStore, DisconnectedSalesforceConfigStore>();
            services.AddSingleton<IOAuthStateProtector>(new SalesforceTestOAuthStateProtector());
            services.AddKeyedSingleton<IConnector>("salesforce", _connector);
        });

    [Fact]
    public async Task OAuth_start_is_local_persistent_and_single_use()
    {
        var grain = Grain<IV2SalesforceReadToolGrain>("principal-oauth-start");

        var disconnected = await grain.ReadRecordsAsync(
            new V2SalesforceRecordReadRequest(new V2SalesforceSemanticEntity("Accounts")));

        Assert.Equal(V2SalesforceReadStatus.NeedsAuth, disconnected.Status);
        var localStartUrl = Assert.IsType<string>(disconnected.ConnectionUrl);
        var localStart = new Uri(localStartUrl, UriKind.Absolute);
        Assert.True(localStart.IsLoopback);
        Assert.Equal(Uri.UriSchemeHttp, localStart.Scheme);
        Assert.Equal(OAuthCallbackPaths.SalesforceStart, localStart.AbsolutePath);
        Assert.StartsWith("?t=", localStart.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("&", localStart.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("services/oauth2/authorize", localStartUrl, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("state=", localStartUrl, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("client_id=", localStartUrl, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("code_challenge=", localStartUrl, StringComparison.OrdinalIgnoreCase);

        var token = Uri.UnescapeDataString(localStart.Query["?t=".Length..]);
        Assert.StartsWith("opaque-", token, StringComparison.Ordinal);
        Assert.DoesNotContain("principal-oauth-start", token, StringComparison.Ordinal);

        await Cluster.DeactivateAsync(grain);

        var authorization = await grain.BeginAuthorizationAsync(token);

        Assert.Equal(V2SalesforceReadStatus.NeedsAuth, authorization.Status);
        var providerUrl = Assert.IsType<string>(authorization.ConnectionUrl);
        Assert.True(SalesforceClientFactory.IsAllowedAuthorizationUrl(providerUrl));
        Assert.Contains("/services/oauth2/authorize", providerUrl, StringComparison.Ordinal);
        Assert.Equal(1, _connector.BeginAuthCallCount);

        await Cluster.DeactivateAsync(grain);
        var replay = await grain.BeginAuthorizationAsync(token);

        Assert.Equal(V2SalesforceReadStatus.Unavailable, replay.Status);
        Assert.Null(replay.ConnectionUrl);
        Assert.Equal(1, _connector.BeginAuthCallCount);
    }

    private sealed class RecordingSalesforceConnector : IConnector
    {
        private int _beginAuthCallCount;

        public int BeginAuthCallCount => Volatile.Read(ref _beginAuthCallCount);

        public ConnectorDescriptor Descriptor { get; } = new("salesforce", "Salesforce", [], []);

        public Task<ConnectorConfigStatus> ValidateConfigAsync(
            string? userScope = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new ConnectorConfigStatus(true));
        }

        public Task<AuthChallenge> BeginAuthAsync(
            NeuronId user,
            string? clientIdHint = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _beginAuthCallCount);
            return Task.FromResult(new AuthChallenge(
                "https://login.salesforce.com/services/oauth2/authorize?response_type=code&client_id=test-client&state=provider-state"));
        }

        public Task<AuthResult> CompleteAuthAsync(
            OAuthCallback callback,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ConnectionHealth> TestConnectionAsync(
            NeuronId user,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class DisconnectedSalesforceConfigStore : IPackConfigStore
    {
        private static readonly IReadOnlyDictionary<string, string> AppConfig =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [SalesforceClientFactory.ClientIdKey] = "test-client",
                [SalesforceClientFactory.ClientSecretKey] = "test-secret"
            };

        private static readonly IReadOnlyDictionary<string, string> Empty =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public Task SetAsync(
            string scope,
            string pack,
            IReadOnlyDictionary<string, string> values,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyDictionary<string, string>> GetAsync(
            string scope,
            string pack,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                string.Equals(scope, PackConfigScopes.App, StringComparison.Ordinal) &&
                string.Equals(pack, SalesforceClientFactory.PackName, StringComparison.Ordinal)
                    ? AppConfig
                    : Empty);
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
