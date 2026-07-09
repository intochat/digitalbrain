using DigitalBrain.Core;
using DigitalBrain.Core.Config;
using DigitalBrain.Salesforce;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Config;
using DigitalBrain.Tests.Ino;
using DigitalBrain.TestKit;
using DigitalBrain.Ui.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Tests.Salesforce;

internal sealed class RecordingSalesforceApiClient : ISalesforceApiClient
{
    public List<string> QueryCalls { get; } = [];
    public List<int> ListAccountsCalls { get; } = [];

    public Task<string[]> QueryAsync(string soql, CancellationToken ct)
    {
        QueryCalls.Add(soql);
        return Task.FromResult(new[] { "Acme Corp - $50k (proposal)" });
    }

    public Task<string[]> ListAccountsAsync(int maxResults, CancellationToken ct)
    {
        ListAccountsCalls.Add(maxResults);
        return Task.FromResult(new[] { "Acme Corp", "Globex" });
    }
}

internal sealed class TestSalesforceApiClientFactory(RecordingSalesforceApiClient client) : ISalesforceApiClientFactory
{
    public List<NeuronScope> Scopes { get; } = [];

    public Task<ISalesforceApiClient> CreateAsync(NeuronScope scope, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Scopes.Add(scope);
        return Task.FromResult<ISalesforceApiClient>(client);
    }
}

public class SalesforceInoToolProviderTests : NeuronTestBase
{
    private readonly RecordingSalesforceApiClient _salesforce = new();
    private readonly TestSalesforceApiClientFactory _salesforceFactory;

    public SalesforceInoToolProviderTests()
    {
        _salesforceFactory = new TestSalesforceApiClientFactory(_salesforce);
    }

    protected override void ConfigureSilo(ISiloBuilder builder) =>
        builder.ConfigureServices(services =>
        {
            services.AddPackConfigStore(blobsForKeyRing: null);
            services.AddSingleton<ISalesforceApiClientFactory>(_salesforceFactory);
        });

    [Fact]
    public async Task BuildTools_returns_one_gated_salesforce_tool()
    {
        var provider = new SalesforceInoToolProvider(Cluster.GrainFactory);

        var tools = provider.BuildTools("session-sf-tool", CancellationToken.None);

        var tool = Assert.Single(tools);
        Assert.Equal("salesforce_query", tool.Name);
    }

    [Fact]
    public async Task Tool_returns_unauthorized_message_and_never_calls_salesforce_api_when_not_connected()
    {
        var provider = new SalesforceInoToolProvider(Cluster.GrainFactory);
        var tool = provider.BuildTools("session-sf-tool-unauth", CancellationToken.None)[0];

        var result = await tool.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?> { ["soqlOrQuery"] = "opportunities", ["maxResults"] = 5 }),
            CancellationToken.None);

        Assert.Contains("Salesforce", result?.ToString());
        Assert.Empty(_salesforce.ListAccountsCalls);
    }

    [Fact]
    public async Task Tool_calls_salesforce_api_and_returns_accounts_when_connected()
    {
        const string clientId = "session-sf-tool-auth";
        var session = Grain<IUserSessionNeuron>("session-main");
        await session.HandleAsync(new LoginRequest("sf-tool-user", "correct horse battery staple", clientId));
        var config = Grain<ISalesforceConfigWriter>("salesforce-config-writer");
        await config.StoreSalesforceCredentialAsync();

        var provider = new SalesforceInoToolProvider(Cluster.GrainFactory);
        var tool = provider.BuildTools(clientId, CancellationToken.None)[0];

        var result = await tool.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?> { ["soqlOrQuery"] = "opportunities", ["maxResults"] = 5 }),
            CancellationToken.None);

        Assert.Contains("Salesforce accounts:", result?.ToString());
        Assert.Single(_salesforce.ListAccountsCalls);
    }

    [Fact]
    public async Task Tool_uses_logged_in_user_scope_when_salesforce_credential_is_user_scoped()
    {
        const string userId = "sf-tool-oauth-user";
        const string clientId = "session-sf-tool-oauth-user";
        var session = Grain<IUserSessionNeuron>("session-main");
        await session.HandleAsync(new LoginRequest(userId, "correct horse battery staple", clientId));
        await StoreOAuthShapedSalesforceCredentialAsync(userId);

        var provider = new SalesforceInoToolProvider(Cluster.GrainFactory);
        var tool = provider.BuildTools(clientId, CancellationToken.None)[0];

        var result = await tool.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?> { ["soqlOrQuery"] = "opportunities", ["maxResults"] = 5 }),
            CancellationToken.None);

        Assert.Contains("Salesforce accounts:", result?.ToString());
        Assert.Contains(_salesforceFactory.Scopes, scope => scope.UserId.Value == userId);
        Assert.DoesNotContain(_salesforceFactory.Scopes, scope => scope.UserId.Value == "salesforce-capability-main");
    }

    private async Task StoreOAuthShapedSalesforceCredentialAsync(string userId)
    {
        var store = ((InProcessSiloHandle)Cluster.Silos[0]).SiloHost.Services.GetRequiredService<IPackConfigStore>();
        await store.SetAsync(SalesforceClientFactory.DefaultScope, SalesforceClientFactory.PackName, new Dictionary<string, string>
        {
            [SalesforceClientFactory.ClientIdKey] = "client-id",
            [SalesforceClientFactory.ClientSecretKey] = "client-secret",
            [SalesforceClientFactory.LoginUrlKey] = SalesforceClientFactory.DefaultLoginUrl,
            [SalesforceClientFactory.ApiVersionKey] = SalesforceClientFactory.DefaultApiVersion,
            [SalesforceClientFactory.RedirectUriKey] = SalesforceClientFactory.DefaultRedirectUri
        });
        await store.SetAsync(PackConfigScopes.ForUser(new UserId(userId)), SalesforceClientFactory.PackName, new Dictionary<string, string>
        {
            [SalesforceClientFactory.RefreshTokenKey] = "refresh-token"
        });
    }
}
