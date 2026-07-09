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
    public Task<ISalesforceApiClient> CreateAsync(NeuronScope scope, CancellationToken cancellationToken = default) =>
        Task.FromResult<ISalesforceApiClient>(client);
}

public class SalesforceInoToolProviderTests : NeuronTestBase
{
    private readonly RecordingSalesforceApiClient _salesforce = new();

    protected override void ConfigureSilo(ISiloBuilder builder) =>
        builder.ConfigureServices(services =>
        {
            services.AddPackConfigStore(blobsForKeyRing: null);
            services.AddSingleton<ISalesforceApiClientFactory>(new TestSalesforceApiClientFactory(_salesforce));
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
}
