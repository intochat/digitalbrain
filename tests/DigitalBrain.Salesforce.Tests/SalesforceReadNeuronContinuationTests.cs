using System.Reflection;
using DigitalBrain.Core;
using DigitalBrain.Core.Config;
using DigitalBrain.Kernel.Abstractions;
using DigitalBrain.Kernel.V2;
using DigitalBrain.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Salesforce.Tests;

public sealed class SalesforceReadNeuronContinuationTests : NeuronTestBase
{
    private static readonly SalesforceProviderScope ProviderScope = new("organization-1", "salesforce-user-1");
    private readonly ScenarioSalesforceApiClient _client = new();

    protected override void ConfigureSilo(ISiloBuilder builder) =>
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<ISalesforceApiClientFactory>(new ScenarioSalesforceApiClientFactory(_client));
            services.AddSingleton<IPackConfigStore, CredentialPackConfigStore>();
            services.AddSingleton<IOAuthStateProtector>(new SalesforceTestOAuthStateProtector());
            services.AddKeyedSingleton<IConnector>("salesforce", new ValidSalesforceConnector());
        });

    [Fact]
    public async Task Cancellation_preserves_the_continuation_for_retry()
    {
        var grain = Grain<IV2SalesforceReadToolGrain>("principal-cancel");
        var continuation = await SeedContinuationAsync(grain);
        var providerStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _client.Continue = async (_, cancellationToken) =>
        {
            providerStarted.SetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return CompletedPage();
        };
        using var cancellation = new CancellationTokenSource();

        var pending = grain.ContinueRecordsAsync(continuation, cancellation.Token);
        await providerStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        _client.Continue = (_, _) => Task.FromResult(CompletedPage());
        var retry = await grain.ContinueRecordsAsync(continuation);

        Assert.Equal(V2SalesforceReadStatus.Success, retry.Status);
        Assert.Equal(2, _client.ContinuationCallCount);
    }

    [Fact]
    public async Task Transient_failure_preserves_the_continuation_for_retry()
    {
        var grain = Grain<IV2SalesforceReadToolGrain>("principal-transient");
        var continuation = await SeedContinuationAsync(grain);
        _client.Continue = (_, _) => _client.ContinuationCallCount == 1
            ? Task.FromException<SalesforceReadPage>(new TimeoutException("transient provider failure"))
            : Task.FromResult(CompletedPage());

        var failed = await grain.ContinueRecordsAsync(continuation);
        var retry = await grain.ContinueRecordsAsync(continuation);

        Assert.Equal(V2SalesforceReadStatus.Unavailable, failed.Status);
        Assert.Equal(V2SalesforceReadStatus.Success, retry.Status);
        Assert.Equal(2, _client.ContinuationCallCount);
    }

    [Fact]
    public async Task Success_consumes_the_continuation()
    {
        var grain = Grain<IV2SalesforceReadToolGrain>("principal-success");
        var continuation = await SeedContinuationAsync(grain);
        _client.Continue = (_, _) => Task.FromResult(CompletedPage());

        var success = await grain.ContinueRecordsAsync(continuation);
        await Cluster.DeactivateAsync(grain);
        var replay = await grain.ContinueRecordsAsync(continuation);

        Assert.Equal(V2SalesforceReadStatus.Success, success.Status);
        Assert.Equal(V2SalesforceReadStatus.ContinuationExpired, replay.Status);
        Assert.Equal(1, _client.ContinuationCallCount);
    }

    [Fact]
    public async Task Continuation_survives_grain_reactivation()
    {
        var grain = Grain<IV2SalesforceReadToolGrain>("principal-reactivation");
        var continuation = await SeedContinuationAsync(grain);
        await Cluster.DeactivateAsync(grain);
        _client.Continue = (_, _) => Task.FromResult(CompletedPage());

        var result = await grain.ContinueRecordsAsync(continuation);

        Assert.Equal(V2SalesforceReadStatus.Success, result.Status);
        Assert.Equal(1, _client.ContinuationCallCount);
    }

    private static async Task<V2SalesforceContinuationRequest> SeedContinuationAsync(
        IV2SalesforceReadToolGrain grain)
    {
        var result = await grain.ReadRecordsAsync(
            new V2SalesforceRecordReadRequest(new V2SalesforceSemanticEntity("Accounts")));
        Assert.Equal(V2SalesforceReadStatus.Success, result.Status);
        return new V2SalesforceContinuationRequest(Assert.IsType<V2SalesforceContinuation>(result.Continuation).Value);
    }

    private static SalesforceReadPage CompletedPage() =>
        new("{\"Entity\":\"Account\",\"Records\":[]}", 0, 0, ProviderScope);

    private static SalesforceContinuation ProviderContinuation() =>
        (SalesforceContinuation)Activator.CreateInstance(
            typeof(SalesforceContinuation),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            ["/services/data/v60.0/query/next", ProviderScope, "Account", "Id", new Dictionary<string, string>()],
            culture: null)!;

    private sealed class ScenarioSalesforceApiClientFactory(ScenarioSalesforceApiClient client)
        : ISalesforceApiClientFactory
    {
        public Task<ISalesforceApiClient> CreateAsync(
            NeuronScope scope,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ISalesforceApiClient>(client);
    }

    private sealed class ScenarioSalesforceApiClient : ISalesforceApiClient
    {
        public Func<SalesforceContinuation, CancellationToken, Task<SalesforceReadPage>> Continue { get; set; } =
            (_, _) => Task.FromResult(CompletedPage());

        public int ContinuationCallCount { get; private set; }

        public Task<SalesforceReadPage> ReadRecordsAsync(
            V2SalesforceRecordReadRequest request,
            CancellationToken ct) =>
            Task.FromResult(new SalesforceReadPage(
                "{\"Entity\":\"Account\",\"Records\":[]}",
                0,
                1,
                ProviderScope,
                ProviderContinuation()));

        public Task<SalesforceReadPage> ContinueRecordsAsync(
            SalesforceContinuation continuation,
            CancellationToken ct)
        {
            ContinuationCallCount++;
            return Continue(continuation, ct);
        }

        public Task<string> GetCurrentUserProfileAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<string[]> ListAccountsAsync(int maxResults, CancellationToken ct) => throw new NotSupportedException();
        public Task<string[]> ListContactsAsync(int maxResults, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> DescribeCrmAccessAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class CredentialPackConfigStore : IPackConfigStore
    {
        private static readonly IReadOnlyDictionary<string, string> Credentials = new Dictionary<string, string>
        {
            [SalesforceClientFactory.AccessTokenKey] = "test-access-token",
            [SalesforceClientFactory.InstanceUrlKey] = "https://example.my.salesforce.com"
        };

        public Task SetAsync(
            string scope,
            string pack,
            IReadOnlyDictionary<string, string> values,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyDictionary<string, string>> GetAsync(
            string scope,
            string pack,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Credentials);
    }

    private sealed class ValidSalesforceConnector : IConnector
    {
        public ConnectorDescriptor Descriptor { get; } = new("salesforce", "Salesforce", [], []);

        public Task<ConnectorConfigStatus> ValidateConfigAsync(
            string? userScope = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConnectorConfigStatus(true));

        public Task<AuthChallenge> BeginAuthAsync(
            NeuronId user,
            string? clientIdHint = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AuthResult> CompleteAuthAsync(
            OAuthCallback callback,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ConnectionHealth> TestConnectionAsync(
            NeuronId user,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
