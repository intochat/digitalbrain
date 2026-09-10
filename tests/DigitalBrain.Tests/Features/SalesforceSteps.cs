using System.Text.Json;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Salesforce;
using DigitalBrain.Core;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;
using Xunit;

namespace DigitalBrain.Tests;

[Binding]
public sealed class SalesforceSteps(BrainWorld world)
{
    private const string Arguments = """{ "sObject": "Account", "record": { "Name": "intochat" } }""";
    private SalesforceWritePreview? _preview;
    private Accepted<SalesforceWritePreview>? _prepared;
    private SignalId _confirmedWork;

    private SalesforceQueryResult? _salesforceRead;
    private SalesforceUnavailableException? _salesforceError;

    private ISalesforce Salesforce => world.Brain.Grains.GetGrain<ISalesforce>(new NeuronId("salesforce", "salesforce").ToGrainId());

    [Given("a running brain with the Salesforce module in fake mode")]
    public async Task StartSalesforce()
        => world.Simulation = await BrainSimulation.StartAsync(new()
        {
            ConfigureSilo = silo => silo.Services.AddSingleton<TimeProvider>(world.Clock),
            Modules = new([typeof(SalesforceModule)]),
            Configuration = new Dictionary<string, string?>
            {
                ["DigitalBrain:Fakes:Enabled"] = "true",
                ["DigitalBrain:Salesforce:OAuth:ConsumerKey"] = "fake-consumer-key",
                ["DigitalBrain:Salesforce:OAuth:ConsumerSecret"] = "fake-consumer-secret",
                ["DigitalBrain:Salesforce:OAuth:PublicOrigin"] = "http://localhost:5080",
            },
        });

    [When("the Salesforce account connects")]
    public Task ConnectSalesforce() => ConnectSalesforce(3600);

    private async Task ConnectSalesforce(int lifetime)
    {
        var nonce = world.Brain.SiloServices.GetRequiredService<TokenHandoff>()
            .Deposit(new OAuthTokens("fake-access-token", "fake-refresh-token"));
        await Salesforce.Connect(new ConnectSalesforceAccount(CommandId.New(), "https://fixture.my.salesforce.com", lifetime, nonce));
    }

    [Then("the Salesforce connection is reported")]
    public async Task SalesforceConnection()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        SalesforceConnection? connection = null;
        await ReactionWait.UntilAsync(async () =>
        {
            connection = await Salesforce.ReadConnection().WaitAsync(timeout.Token);
            return connection.Connected;
        }, timeout.Token);
        Assert.NotNull(connection);
        Assert.Equal("https://fixture.my.salesforce.com", connection.InstanceUrl);
        Assert.True(connection.ExpiresAt > world.Clock.GetUtcNow());
    }

    [When("the guarded query {string} runs")]
    public async Task QuerySalesforce(string query)
    {
        _salesforceRead = null;
        _salesforceError = null;
        try { _salesforceRead = await Salesforce.Query(new(query)); }
        catch (SalesforceUnavailableException error) { _salesforceError = error; }
    }

    [Then("the Salesforce query returns {int} records")]
    public void SalesforceRecords(int count)
    {
        Assert.Null(_salesforceError);
        Assert.NotNull(_salesforceRead);
        Assert.True(_salesforceRead.Content.GetProperty("untrustedData").GetBoolean());
        Assert.Equal(count, _salesforceRead.TotalSize);
        Assert.Equal(count, _salesforceRead.Content.GetProperty("records").GetArrayLength());
    }

    [Then("the Salesforce query was refused")]
    public void SalesforceRefused()
    {
        Assert.Null(_salesforceRead);
        Assert.NotNull(_salesforceError);
        Assert.Equal("Use one SELECT with an outer WHERE and positive LIMIT. Comments, multiple statements and locking queries are not allowed.",
            _salesforceError.Message);
    }

    [Then("no Salesforce journal contains the OAuth tokens")]
    public async Task SalesforceJournalsContainNoTokens()
    {
        var commands = await Salesforce.ReadCommands(0);
        Assert.NotEmpty(commands.Delta);
        var json = commands.Delta.Select(record => JsonSerializer.Serialize(record)).ToList();
        foreach (var kind in new[] { JournalKind.Incoming, JournalKind.Outgoing })
        {
            var journal = await Salesforce.ReadJournal(kind, 0);
            Assert.NotEmpty(journal.Delta);
            json.AddRange(journal.Delta.Select(delivery => JsonSerializer.Serialize(delivery)));
            json.Add(JsonSerializer.Serialize(journal.ResetSnapshot));
        }
        foreach (var raw in json)
        {
            Assert.DoesNotContain("fake-access-token", raw, StringComparison.Ordinal);
            Assert.DoesNotContain("fake-refresh-token", raw, StringComparison.Ordinal);
        }
    }

    [When("the Salesforce account connects with a one-second token")]
    public async Task ConnectSalesforceWithShortLifetime()
    {
        await ConnectSalesforce(1);
        await SalesforceConnection();
    }

    [When("the Salesforce token lifetime elapses")]
    public void ExpireSalesforceToken() => world.Clock.Advance(TimeSpan.FromSeconds(2));

    [Then("the Salesforce read succeeds after a single refresh")]
    public async Task ReadAfterRefresh()
    {
        var read = await world.Brain.SiloServices.GetRequiredService<SalesforceNativeTools>()
            .GetUserInfo(TestContext.Current.CancellationToken);
        Assert.Equal("Salesforce fixture", read.Content.GetProperty("user").GetString());
        Assert.True(read.Content.GetProperty("untrustedData").GetBoolean());
        Assert.True((await Salesforce.ReadConnection()).ExpiresAt > world.Clock.GetUtcNow().AddMinutes(59));
        Assert.Single((await Salesforce.ReadCommands(0)).Delta.Where(command => command.Method == "refresh").Select(command => command.Id).Distinct());
    }

    [When("a native Salesforce record creation is prepared")]
    public async Task Prepare()
        => _prepared = await world.Brain.SiloServices.GetRequiredService<SalesforceNativeTools>().CreateRecord(Arguments);

    [Then("the exact Salesforce preview is published without writing")]
    public async Task Preview()
    {
        var signal = await WaitForSignal(SalesforceSignals.SalesforceWritePrepared);
        _preview = JsonSerializer.Deserialize(signal.Signal.Body, SalesforceJson.Default.SalesforceWritePrepared)!.Preview;
        Assert.NotNull(_prepared);
        Assert.Equal(_prepared.Receipt.PreviewId, _preview.PreviewId);
        Assert.Equal(_prepared.Work, signal.CausationId);
        Assert.Equal(Arguments, _preview.Arguments);
        Assert.Equal("createRecord", _preview.Tool);
        Assert.NotEmpty(_preview.ToolSchemaHash);
        Assert.Null(_preview.RecordId);
        Assert.DoesNotContain((await Salesforce.ReadJournal(JournalKind.Outgoing, 0)).Delta,
            entry => entry.Signal.Type == SalesforceSignals.RecordWritten);
    }

    [Then("Salesforce refuses a confirmation with another schema")]
    public async Task WrongSchema()
    {
        Assert.NotNull(_preview);
        await Assert.ThrowsAsync<SalesforceUnavailableException>(() => Salesforce.ConfirmWrite(
            new ConfirmSalesforceWrite(CommandId.New(), _preview.PreviewId, "another-schema")));
    }

    [When("the reviewed Salesforce write is confirmed twice")]
    public async Task ConfirmTwice()
    {
        Assert.NotNull(_preview);
        var command = new ConfirmSalesforceWrite(CommandId.New(), _preview.PreviewId, _preview.ToolSchemaHash);
        var first = await Salesforce.ConfirmWrite(command);
        await WaitForSignal(SalesforceSignals.RecordWritten);
        var duplicate = await Salesforce.ConfirmWrite(command);
        Assert.Equal(first, duplicate);
        _confirmedWork = first.Work;
        var scheduled = Assert.Single((await Salesforce.ReadJournal(JournalKind.Incoming, 0)).Delta,
            entry => entry.SignalId == first.Work);
        using var body = JsonDocument.Parse(scheduled.Signal.Body);
        Assert.Equal(["previewId", "toolSchemaHash"], body.RootElement.EnumerateObject().Select(property => property.Name));
    }

    [Then("Salesforce reports one written record")]
    public async Task Written()
    {
        var entry = Assert.Single((await Salesforce.ReadJournal(JournalKind.Outgoing, 0)).Delta,
            entry => entry.Signal.Type == SalesforceSignals.RecordWritten);
        var written = JsonSerializer.Deserialize(entry.Signal.Body, SalesforceJson.Default.RecordWritten)!.Preview;
        Assert.Equal(_confirmedWork, entry.CausationId);
        Assert.Equal(_preview!.PreviewId, written.PreviewId);
        Assert.Equal(Arguments, written.Arguments);
        Assert.Equal("record-intochat", written.RecordId);
    }

    [Then("Salesforce refuses another confirmation of the consumed preview")]
    public async Task Consumed()
    {
        Assert.NotNull(_preview);
        await Assert.ThrowsAsync<SalesforceUnavailableException>(() => Salesforce.ConfirmWrite(
            new ConfirmSalesforceWrite(CommandId.New(), _preview.PreviewId, _preview.ToolSchemaHash)));
    }

    private Task<SignalDelivery> WaitForSignal(string type)
        => ReactionWait.ForSignalAsync(Salesforce, type, TestContext.Current.CancellationToken);
}
