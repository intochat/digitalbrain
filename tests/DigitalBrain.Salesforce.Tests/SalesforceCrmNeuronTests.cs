using DigitalBrain.Core;
using DigitalBrain.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Salesforce.Tests;

public class SalesforceCrmNeuronTests : NeuronTestBase
{
    private readonly RecordingSalesforceApiClient _client = new();

    protected override void ConfigureSilo(ISiloBuilder builder) =>
        builder.ConfigureServices(services =>
            services.AddSingleton<ISalesforceApiClientFactory>(new FakeSalesforceApiClientFactory(_client)));

    [Fact]
    public async Task QueryAsync_Delegates_To_Api_Client_For_Its_Own_Scope()
    {
        var crm = Grain<ISalesforceCrmNeuron>("alice");

        var records = await crm.QueryAsync("SELECT Id, Name FROM Account LIMIT 1");

        Assert.Equal("SELECT Id, Name FROM Account LIMIT 1", _client.Queries.Single());
        Assert.Equal(["{\"Name\":\"Acme\"}"], records);
        Assert.Equal("alice", _client.ScopesRequested.Single().UserId.Value);
    }

    [Fact]
    public async Task ListAccountsAsync_Delegates_To_Api_Client_For_Its_Own_Scope()
    {
        var crm = Grain<ISalesforceCrmNeuron>("bob");

        await crm.ListAccountsAsync(3);

        Assert.Equal(3, _client.AccountListLimits.Single());
        Assert.Equal("bob", _client.ScopesRequested.Single().UserId.Value);
    }
}

internal sealed class FakeSalesforceApiClientFactory(ISalesforceApiClient client) : ISalesforceApiClientFactory
{
    public Task<ISalesforceApiClient> CreateAsync(NeuronScope scope, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (client is RecordingSalesforceApiClient recording)
        {
            recording.ScopesRequested.Add(scope);
        }

        return Task.FromResult(client);
    }
}

internal sealed class RecordingSalesforceApiClient : ISalesforceApiClient
{
    public List<string> Queries { get; } = [];
    public List<int> AccountListLimits { get; } = [];
    public List<NeuronScope> ScopesRequested { get; } = [];

    public Task<string[]> QueryAsync(string soql, CancellationToken ct)
    {
        Queries.Add(soql);
        return Task.FromResult(new[] { "{\"Name\":\"Acme\"}" });
    }

    public Task<string[]> ListAccountsAsync(int maxResults, CancellationToken ct)
    {
        AccountListLimits.Add(maxResults);
        return Task.FromResult(new[] { "{\"Name\":\"Acme\"}" });
    }
}
