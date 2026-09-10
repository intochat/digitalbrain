using System.Text.Json;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Salesforce;
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

    private ISalesforce Salesforce => world.Brain.Grains.GetGrain<ISalesforce>(new NeuronId("salesforce", "salesforce").ToGrainId());

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

    [When("a short lived Salesforce connection expires")]
    public async Task Expire()
    {
        await Salesforce.Connect(new ConnectSalesforceAccount(CommandId.New(), "fake-access-token", null, 1,
            "https://fixture.my.salesforce.com"));
        await WaitForSignal(SalesforceSignals.SalesforceConnected);
        var connection = await Salesforce.ReadConnection();
        Assert.True(connection.Connected);
        var remaining = connection.ExpiresAt!.Value - DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(20);
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining);
        }
    }

    [Then("Salesforce user and query reads require reconnection without changing journals")]
    public async Task ExpiredReads()
    {
        var before = await JournalSizes();
        await Assert.ThrowsAsync<SalesforceNotConnectedException>(() => Salesforce.ReadUserInfo());
        await Assert.ThrowsAsync<SalesforceNotConnectedException>(() => Salesforce.Query(
            new SoqlQuery("SELECT Id FROM Account WHERE Name = 'intochat' LIMIT 5")));
        Assert.Equal(before, await JournalSizes());
    }

    [When("Salesforce disconnects")]
    public async Task Disconnect()
    {
        await Salesforce.Disconnect(new DisconnectSalesforce(CommandId.New()));
        await WaitForSignal(SalesforceSignals.SalesforceDisconnected);
    }

    [Then("Salesforce reports no connection")]
    public async Task Disconnected()
        => Assert.Equal(new SalesforceConnection(false, null, null), await Salesforce.ReadConnection());

    private async Task<(long Incoming, long Outgoing, long Commands)> JournalSizes()
        => ((await Salesforce.ReadJournal(JournalKind.Incoming, 0)).ResumeSequence,
            (await Salesforce.ReadJournal(JournalKind.Outgoing, 0)).ResumeSequence,
            (await Salesforce.ReadCommands(0)).ResumeSequence);

    private async Task<SignalDelivery> WaitForSignal(string type)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (true)
        {
            var journal = await Salesforce.ReadJournal(JournalKind.Outgoing, 0).WaitAsync(timeout.Token);
            var entry = journal.Delta.FirstOrDefault(entry => entry.Signal.Type == type);
            if (entry is not null)
            {
                return entry;
            }
            await Task.Delay(20, timeout.Token);
        }
    }
}
