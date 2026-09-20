using System.Text.Json;
using DigitalBrain.Salesforce;
using DigitalBrain.Salesforce.Signals;
using DigitalBrain.Sdk;
using DigitalBrain.Testing.Unit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class SalesforceFacts
{
    [Fact]
    public async Task ConnectPublishesSalesforceConnectedAndConsumesNonce()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = await StartAsync(new FakeSalesforceProvider(), null, ct);
        var nonce = fixture.Handoff.Deposit(new OAuthTokens("access-token", "refresh-token"));
        var salesforce = fixture.Brain.Get<ISalesforce>("salesforce");
        await using var connected = await fixture.Brain.Observe<SalesforceConnected>(salesforce, ct);

        var connection = await salesforce.Connect(new("https://acme.my.salesforce.com", 3600, nonce));

        Assert.True(connection.Connected);
        Assert.Equal("https://acme.my.salesforce.com", connection.InstanceUrl);
        var published = await connected.NextAsync(ct: ct);
        Assert.True(published.Connection.Connected);
        Assert.Equal(connection.InstanceUrl, published.Connection.InstanceUrl);
        Assert.False(fixture.Handoff.TryPeek(nonce, out _));
    }

    [Fact]
    public async Task ConnectWithoutNoncePublishesRejection()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = await StartAsync(new FakeSalesforceProvider(), null, ct);
        var salesforce = fixture.Brain.Get<ISalesforce>("salesforce");
        await using var rejected = await fixture.Brain.Observe<SalesforceConnectionRejected>(salesforce, ct);

        await Assert.ThrowsAsync<SalesforceUnavailableException>(
            () => salesforce.Connect(new("https://acme.my.salesforce.com", 3600, "missing-nonce")));

        Assert.False(string.IsNullOrWhiteSpace((await rejected.NextAsync(ct: ct)).Reason));
    }

    [Fact]
    public async Task RefreshPublishesSalesforceRefreshed()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = await StartAsync(new FakeSalesforceProvider(), new FakeTokenExchange(), ct);
        await ConnectAsync(fixture);
        var salesforce = fixture.Brain.Get<ISalesforce>("salesforce");
        await using var refreshed = await fixture.Brain.Observe<SalesforceRefreshed>(salesforce, ct);

        var connection = await salesforce.Refresh(new());

        Assert.True(connection.Connected);
        var published = await refreshed.NextAsync(ct: ct);
        Assert.True(published.Connection.Connected);
    }

    [Fact]
    public async Task DisconnectPublishesSalesforceDisconnected()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = await StartAsync(new FakeSalesforceProvider(), null, ct);
        var salesforce = await ConnectAsync(fixture);
        await using var disconnected = await fixture.Brain.Observe<SalesforceDisconnected>(salesforce, ct);

        var connection = await salesforce.Disconnect(new());

        Assert.False(connection.Connected);
        await disconnected.NextAsync(ct: ct);
        Assert.False((await salesforce.ReadConnection()).Connected);
    }

    [Fact]
    public async Task PrepareThenConfirmWritePublishesRecordWritten()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new FakeSalesforceProvider
        {
            SchemaHash = "schema-hash",
            Result = JsonSerializer.SerializeToElement(new { id = "001xx000003DGbY" }),
        };
        await using var fixture = await StartAsync(provider, null, ct);
        var salesforce = await ConnectAsync(fixture);
        await using var prepared = await fixture.Brain.Observe<SalesforceWritePrepared>(salesforce, ct);

        var preview = await salesforce.PrepareWrite(new("createRecord", """{"Name":"Acme"}"""));

        Assert.Equal("schema-hash", preview.ToolSchemaHash);
        Assert.Equal("createRecord", (await prepared.NextAsync(ct: ct)).Preview.Tool);

        await using var written = await fixture.Brain.Observe<RecordWritten>(salesforce, ct);
        var confirmed = await salesforce.ConfirmWrite(new(preview.PreviewId, preview.ToolSchemaHash));

        Assert.Equal("001xx000003DGbY", confirmed.RecordId);
        Assert.Equal("001xx000003DGbY", (await written.NextAsync(ct: ct)).Preview.RecordId);
    }

    [Fact]
    public async Task PrepareWriteRejectsAnUnadmittedTool()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = await StartAsync(new FakeSalesforceProvider(), null, ct);
        var salesforce = await ConnectAsync(fixture);

        await Assert.ThrowsAsync<SalesforceUnavailableException>(
            () => salesforce.PrepareWrite(new("deleteRecord", "{}")));
    }

    [Fact]
    public async Task QueryRunsBoundedSoqlThroughTheProvider()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new FakeSalesforceProvider
        {
            Result = JsonSerializer.SerializeToElement(new { totalSize = 2, records = Array.Empty<object>() }),
        };
        await using var fixture = await StartAsync(provider, null, ct);
        var salesforce = await ConnectAsync(fixture);

        var result = await salesforce.Query(new("SELECT Id FROM Account WHERE Name != null LIMIT 10"), ct);

        Assert.Equal(2, result.TotalSize);
        Assert.Equal("soqlQuery", provider.AttemptedTool);
    }

    [Fact]
    public async Task QueryRejectsUnboundedSoql()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = await StartAsync(new FakeSalesforceProvider(), null, ct);
        var salesforce = fixture.Brain.Get<ISalesforce>("salesforce");

        await Assert.ThrowsAsync<SalesforceUnavailableException>(() => salesforce.Query(new("SELECT Id FROM Account"), ct));
    }

    private static async Task<ISalesforce> ConnectAsync(Fixture fixture)
    {
        var nonce = fixture.Handoff.Deposit(new OAuthTokens("access-token", "refresh-token"));
        var salesforce = fixture.Brain.Get<ISalesforce>("salesforce");
        await salesforce.Connect(new("https://acme.my.salesforce.com", 3600, nonce));
        return salesforce;
    }

    private static async Task<Fixture> StartAsync(FakeSalesforceProvider provider, FakeTokenExchange? exchange, CancellationToken cancellationToken)
    {
        var handoff = new TokenHandoff(TimeProvider.System);
        var brain = await UnitTest.StartAsync(new()
        {
            Modules = [new SalesforceModule()],
            ConfigureSilo = silo =>
            {
                silo.Services.AddSingleton<ISalesforceProvider>(provider);
                silo.Services.AddSingleton(handoff);
                if (exchange is not null)
                {
                    silo.Services.AddSingleton<ISalesforceTokenExchange>(exchange);
                }
            },
        }, cancellationToken);
        return new Fixture(brain, handoff);
    }

    private sealed class Fixture(UnitBrain brain, TokenHandoff handoff) : IAsyncDisposable
    {
        internal UnitBrain Brain { get; } = brain;

        internal TokenHandoff Handoff { get; } = handoff;

        public ValueTask DisposeAsync() => Brain.DisposeAsync();
    }
}

internal sealed class FakeSalesforceProvider : ISalesforceProvider
{
    public string SchemaHash { get; set; } = "schema-hash";

    public JsonElement Result { get; set; } = JsonSerializer.SerializeToElement(new { });

    public string? AttemptedTool { get; private set; }

    public Task<JsonElement> InvokeAsync(string tool, JsonElement arguments, string accessToken, CancellationToken cancellationToken)
    {
        AttemptedTool = tool;
        return Task.FromResult(Result);
    }

    public Task<string> ReadToolSchemaHashAsync(string tool, string accessToken, CancellationToken cancellationToken)
    {
        AttemptedTool = tool;
        return Task.FromResult(SchemaHash);
    }
}

internal sealed class FakeTokenExchange : ISalesforceTokenExchange
{
    public Task<SalesforceTokenGrant> ExchangeAsync(string refreshToken, CancellationToken cancellationToken)
        => Task.FromResult(new SalesforceTokenGrant("fresh-access-token", "fresh-refresh-token", 3600));
}