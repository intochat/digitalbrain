using System.Text.Json;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using DigitalBrain.Google;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;
using Xunit;

namespace DigitalBrain.Tests;

[Binding]
public sealed class GmailSteps(BrainWorld world)
{
    private const string GmailAccessToken = "gmail-journal-secret-access-a7f491b380";
    private const string GmailRefreshToken = "gmail-journal-secret-refresh-c9e285d671";
    private GmailContentRead? _read;

    [Given("a running brain with the Google module in fake mode")]
    public async Task Start()
        => world.Simulation = await BrainSimulation.StartAsync(new()
        {
            ConfigureSilo = silo => silo.Services.AddSingleton<TimeProvider>(world.Clock),
            Modules = new([typeof(GoogleModule)]),
            Configuration = new Dictionary<string, string?> { ["DigitalBrain:Fakes:Enabled"] = "true" },
        });

    [When("the Gmail account {string} connects")]
    public Task Connect(string email) => Connect(email, 3600);

    private async Task Connect(string email, int lifetime)
    {
        var nonce = world.Brain.SiloServices.GetRequiredService<TokenHandoff>()
            .Deposit(new OAuthTokens(GmailAccessToken, GmailRefreshToken));
        await Gmail.Connect(new ConnectGmailAccount(CommandId.New(), "google-subject", email,
            "openid email https://www.googleapis.com/auth/gmail.readonly https://www.googleapis.com/auth/gmail.compose", lifetime, nonce));
    }

    [Then("the Gmail connection reports {string}")]
    public async Task Connection(string email)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        GmailConnection connection;
        do
        {
            connection = await Gmail.ReadConnection().WaitAsync(timeout.Token);
            if (!connection.Connected)
            {
                await Task.Delay(20, timeout.Token);
            }
        } while (!connection.Connected);
        Assert.Equal(email, connection.Email);
    }

    [Then("neither Gmail journal contains the OAuth tokens")]
    public async Task JournalsContainNoTokens()
    {
        var commands = await Gmail.ReadCommands(0);
        var incoming = await Gmail.ReadJournal(JournalKind.Incoming, 0);
        Assert.NotEmpty(commands.Delta);
        Assert.NotEmpty(incoming.Delta);
        var json = commands.Delta.Select(record => JsonSerializer.Serialize(record))
            .Concat(incoming.Delta.Select(delivery => JsonSerializer.Serialize(delivery)))
            .Append(JsonSerializer.Serialize(incoming.ResetSnapshot));
        foreach (var raw in json)
        {
            Assert.DoesNotContain(GmailAccessToken, raw, StringComparison.Ordinal);
            Assert.DoesNotContain(GmailRefreshToken, raw, StringComparison.Ordinal);
        }
    }

    [When("the {string} Gmail tool runs for {string}")]
    public async Task Search(string tool, string query)
    {
        Assert.Equal("search_threads", tool);
        _read = await world.Brain.SiloServices.GetRequiredService<GmailNativeTools>().SearchThreads(new(query));
    }

    [Then("the Gmail read returns thread {string}")]
    public void Thread(string id)
    {
        Assert.NotNull(_read);
        Assert.Equal(id, _read.Content.GetProperty("threads")[0].GetProperty("id").GetString());
    }

    [When("the Gmail account {string} connects with a one-second token")]
    public async Task ConnectWithShortLifetime(string email)
    {
        await Connect(email, 1);
        await WaitForSignal(GmailSignals.GmailConnected);
    }

    [When("the Gmail token lifetime elapses")]
    public void ExpireToken() => world.Clock.Advance(TimeSpan.FromSeconds(2));

    [Then("the Gmail read succeeds after a single refresh")]
    public async Task ReadAfterRefresh()
    {
        var read = await world.Brain.SiloServices.GetRequiredService<GmailNativeTools>().ListLabels(TestContext.Current.CancellationToken);
        Assert.Equal("INBOX", read.Content.GetProperty("labels")[0].GetProperty("labelId").GetString());
        var connection = await Gmail.ReadConnection();
        Assert.True(connection.ExpiresAt > world.Clock.GetUtcNow().AddMinutes(59));
        Assert.Single((await Gmail.ReadCommands(0)).Delta.Where(command => command.Method == "refresh").Select(command => command.Id).Distinct());
    }

    private Accepted<GmailDraftPreview>? _prepared;
    private GmailDraftPreview? _preview;
    private SignalId _confirmedWork;

    private IGmail Gmail => world.Brain.Grains.GetGrain<IGmail>(new NeuronId("gmail", "gmail").ToGrainId());

    [When("a native Gmail draft is prepared")]
    public async Task Prepare()
        => _prepared = await world.Brain.SiloServices.GetRequiredService<GmailNativeTools>().CreateDraft(
            new PrepareGmailDraft(CommandId.New(), ["recipient@example.com"], [], [], "Company information", "Here is the requested information."));

    [Then("the Gmail preview is returned and only its identifiers are published")]
    public async Task Preview()
    {
        var signal = await WaitForSignal(GmailSignals.GmailDraftPrepared);
        var prepared = JsonSerializer.Deserialize(signal.Signal.Body, GmailJson.Default.GmailDraftPrepared)!;
        Assert.NotNull(_prepared);
        Assert.Equal(_prepared.Receipt.PreviewId, prepared.PreviewId);
        using var body = JsonDocument.Parse(signal.Signal.Body);
        Assert.Equal(["previewId", "toolSchemaHash"], body.RootElement.EnumerateObject().Select(property => property.Name));
        _preview = _prepared.Receipt with { ToolSchemaHash = prepared.ToolSchemaHash };
        Assert.Equal(_prepared.Work, signal.CausationId);
        Assert.Equal(["recipient@example.com"], _preview.To);
        Assert.Empty(_preview.Cc);
        Assert.Empty(_preview.Bcc);
        Assert.Equal("Company information", _preview.Subject);
        Assert.Equal("Here is the requested information.", _preview.Body);
        Assert.NotEmpty(_preview.ToolSchemaHash);
        Assert.Null(_preview.DraftId);
        Assert.DoesNotContain((await Gmail.ReadJournal(JournalKind.Outgoing, 0)).Delta,
            entry => entry.Signal.Type == GmailSignals.GmailDraftCreated);
    }

    [Then("Gmail refuses a confirmation with another schema")]
    public async Task WrongSchema()
    {
        Assert.NotNull(_preview);
        await Assert.ThrowsAsync<GmailUnavailableException>(() => Gmail.ConfirmDraft(
            new ConfirmGmailDraft(CommandId.New(), _preview.PreviewId, "another-schema")));
    }

    [When("the reviewed Gmail draft is confirmed")]
    public async Task Confirm()
    {
        Assert.NotNull(_preview);
        var accepted = await Gmail.ConfirmDraft(new ConfirmGmailDraft(CommandId.New(), _preview.PreviewId, _preview.ToolSchemaHash));
        _confirmedWork = accepted.Work;
        await WaitForSignal(GmailSignals.GmailDraftCreated);
    }

    [Then("Gmail reports one created fake draft")]
    public async Task Created()
    {
        var signal = Assert.Single((await Gmail.ReadJournal(JournalKind.Outgoing, 0)).Delta,
            entry => entry.Signal.Type == GmailSignals.GmailDraftCreated);
        var created = JsonSerializer.Deserialize(signal.Signal.Body, GmailJson.Default.GmailDraftCreated)!;
        Assert.Equal(_confirmedWork, signal.CausationId);
        Assert.Equal(_preview!.PreviewId, created.PreviewId);
        using var body = JsonDocument.Parse(signal.Signal.Body);
        Assert.Equal(["previewId", "draftId"], body.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal("draft-intochat", created.DraftId);
    }

    [Then("Gmail refuses another confirmation of the consumed preview")]
    public async Task Consumed()
    {
        Assert.NotNull(_preview);
        await Assert.ThrowsAsync<GmailUnavailableException>(() => Gmail.ConfirmDraft(
            new ConfirmGmailDraft(CommandId.New(), _preview.PreviewId, _preview.ToolSchemaHash)));
        await Created();
    }

    private async Task<SignalDelivery> WaitForSignal(string type)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (true)
        {
            var journal = await Gmail.ReadJournal(JournalKind.Outgoing, 0).WaitAsync(timeout.Token);
            var signal = journal.Delta.FirstOrDefault(entry => entry.Signal.Type == type);
            if (signal is not null)
            {
                return signal;
            }
            await Task.Delay(20, timeout.Token);
        }
    }
}
