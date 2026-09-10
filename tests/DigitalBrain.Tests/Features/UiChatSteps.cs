using System.Text.Json;
using System.Text.Json.Nodes;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.AI;
using DigitalBrain.Chat;
using DigitalBrain.Testing;
using DigitalBrain.UI;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;
using Xunit;

namespace DigitalBrain.Tests;

[Binding]
public sealed class UiChatSteps(BrainWorld world, BrainSteps brain)
{
    private Accepted<SignalId>? _lastSend;
    private SignalId? _previousTurn;
    private readonly FixtureChatSettlementCrashPoint _settlementCrashPoint = new();

    [Given("a running brain with AI and UI")]
    public async Task GivenAiAndUi()
        => world.Simulation = await BrainSimulation.StartAsync(new()
        {
            Modules = new([typeof(AIModule), typeof(UIModule)]),
            ConfigureSilo = silo =>
            {
                ScriptedAi.Configure(world)(silo);
                silo.Services.AddSingleton<IChatSettlementCrashPoint>(_settlementCrashPoint);
            },
        });

    [Given("the chat settlement fire is lost once")]
    public void LoseSettlementFire() => _settlementCrashPoint.CrashOnce = true;

    [When(@"chat ""(.*)"" sends ""(.*)""$")]
    public async Task Send(string name, string text)
        => _lastSend = await Chat(name).Send(new SendMessage(CommandId.New(), text));

    [When(@"chat ""(.*)"" sends ""(.*)"" with context")]
    public async Task SendWithContext(string name, string text, Table table)
        => _lastSend = await Chat(name).Send(new SendMessage(CommandId.New(), text,
            [.. table.Rows.Select(row => new ContextRef(row["Path"], row["SchemaHash"], row["PayloadJson"]))]));

    [When(@"chat ""(.*)"" sends ""(.*)"" naming the previous turn")]
    public async Task SendNamingPreviousTurn(string name, string text)
    {
        Assert.NotNull(_lastSend);
        _previousTurn = _lastSend.Receipt;
        _lastSend = await Chat(name).Send(new SendMessage(CommandId.New(), text,
            [new ContextRef($"chat.turn.{_previousTurn}", "chat.turn.v1")]));
    }

    [Then(@"chat ""(.*)"" turn inherits the previous turn's context digests")]
    public async Task InheritsPreviousContext(string name)
    {
        Assert.NotNull(_previousTurn);
        Assert.NotNull(_lastSend);
        var previous = await Chat(name).ReadTurn(new ReadTurn(_previousTurn.Value));
        Assert.NotNull(previous);
        Assert.NotNull(previous.Context);
        Assert.NotEmpty(previous.Context);
        var current = Assert.Single((await Chat(name).ReadTurns(new ReadTurns())).Turns, turn => turn.Turn == _lastSend.Receipt);
        Assert.NotNull(current.Context);
        Assert.Equal(previous.Context, current.Context.Take(previous.Context.Count));
        Assert.Equal(previous.Context.Count + 1, current.Context.Count);
        Assert.Equal($"chat.turn.{current.Turn}", current.Context[^1].Path);
    }

    [When(@"chat ""(.*)"" cancels its turn")]
    public async Task Cancel(string name)
    {
        Assert.NotNull(_lastSend);
        var accepted = await Chat(name).Cancel(new CancelTurn(CommandId.New(), _lastSend.Receipt));
        Assert.Equal(ChatTurnStatus.Cancelled, accepted.Receipt);
    }

    [When(@"chat ""(.*)"" waits up to (\d+) seconds for its turn to be ""(.*)""")]
    public async Task WaitForTurn(string name, int seconds, string status)
    {
        Assert.NotNull(_lastSend);
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            if (await Chat(name).ReadTurn(new ReadTurn(_lastSend.Receipt)) is { } turn && turn.Status.ToString() == status)
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.Fail($"Chat {name} did not settle as {status} within {seconds}s");
    }

    [Then(@"chat ""(.*)"" transcript contains (user|assistant) text ""(.*)""")]
    public async Task TranscriptContains(string name, string role, string text)
        => Assert.Contains((await Chat(name).ReadTranscript(new ReadTranscript())).Turns,
            turn => turn.FromUser == (role == "user") && turn.Text == text);

    [Then(@"chat ""(.*)"" transcript has (\d+) turns")]
    public async Task TranscriptCount(string name, int count)
        => Assert.Equal(count, (await Chat(name).ReadTranscript(new ReadTranscript())).Turns.Count);

    [Then(@"chat ""(.*)"" turn was answered by ""(.*)""")]
    public async Task AnsweredBy(string name, string author)
    {
        Assert.NotNull(_lastSend);
        var turn = Assert.Single((await Chat(name).ReadTurns(new ReadTurns())).Turns, item => item.Turn == _lastSend.Receipt);
        Assert.Equal(author, turn.Author);
        Assert.NotNull(turn.SettledAt);
    }

    [Then(@"the latest ""(.*)"" Responded offers ""(.*)"" card ""(.*)"" titled ""(.*)""")]
    public async Task Card(string name, string kind, string cardName, string title)
    {
        var body = (await brain.Journal(name, JournalKind.Incoming)).Delta.Last(d => d.Signal.Type == UIVocabulary.Responded).Signal.Body;
        var response = JsonSerializer.Deserialize(body, UIJson.Default.Responded);
        Assert.NotNull(response);
        Assert.NotNull(response.Cards);
        Assert.Contains(new KitCardOffer(kind, cardName, title), response.Cards);
    }

    [Then(@"""(.*)"" received no ""(\w+)""")]
    public async Task NoSignal(string name, string type)
        => Assert.DoesNotContain((await brain.Journal(name, JournalKind.Incoming)).Delta, d => d.Signal.Type == type);

    [Then(@"the latest ""(.*)"" incoming ""(\w+)"" text does not contain ""(.*)""")]
    public async Task TextDoesNotContain(string name, string type, string fragment)
    {
        var body = (await brain.Journal(name, JournalKind.Incoming)).Delta.Last(d => d.Signal.Type == type).Signal.Body;
        Assert.DoesNotContain(fragment, JsonNode.Parse(body)?["text"]?.GetValue<string>() ?? string.Empty, StringComparison.Ordinal);
    }

    private IChat Chat(string name)
        => brain.Brain.Grains.GetGrain<IChat>(new NeuronId(UIVocabulary.ChatType, name).ToGrainId());
}
