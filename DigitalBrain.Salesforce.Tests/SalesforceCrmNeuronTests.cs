using DigitalBrain.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Salesforce.Tests;

public class SalesforceCrmNeuronTests : NeuronTestBase
{
    private readonly RecordingSalesforceApiClient _client = new();

    protected override void ConfigureSilo(ISiloBuilder builder) =>
        builder.ConfigureServices(services => services.AddSingleton<ISalesforceApiClient>(_client));

    [Fact]
    public async Task QueryAsync_Delegates_To_Api_Client()
    {
        var crm = Grain<ISalesforceCrmNeuron>("salesforce-main");

        var records = await crm.QueryAsync("SELECT Id, Name FROM Account LIMIT 1");

        Assert.Equal("SELECT Id, Name FROM Account LIMIT 1", _client.Queries.Single());
        Assert.Equal(["{\"Name\":\"Acme\"}"], records);
    }

    [Fact]
    public async Task ListAccountsAsync_Delegates_To_Api_Client()
    {
        var crm = Grain<ISalesforceCrmNeuron>("salesforce-main");

        await crm.ListAccountsAsync(3);

        Assert.Equal(3, _client.AccountListLimits.Single());
    }
}

internal sealed class RecordingSalesforceApiClient : ISalesforceApiClient
{
    public List<string> Queries { get; } = [];
    public List<int> AccountListLimits { get; } = [];

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
