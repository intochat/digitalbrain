using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.Chat;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.OS.Assistant.Tests;

public sealed class ChatReadTranscriptCapability(OSBehaviorsFixture fixture)
{
    private const int RequestTimeout = 60_000;
    private const int TurnTimeout = 120_000;
    private const string FinalAnswer = "Here is what I found.";
    private const string DefaultInstance = "default";

    private static readonly string ReadTranscriptTool =
        ValidatedCapability.ToolNameFor("chat.read-transcript-request", 1);

    [Fact(Timeout = RequestTimeout, DisplayName =
        "a directed read-transcript request returns another conversation's own turns")]
    public async Task DirectedReadReturnsAnotherConversationsTurns()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        const string chatName = "direct-transcript-read";

        test.Chat().Reply("Noted.");
        await test.Client.GetGrainProxy<IChat>(chatName).Send(new SendMessage(CommandId.New(), "hello there"));

        var read = await test.Client.Get<IChat>()
            .SendAsync(new ReadTranscriptRequest(chatName), cancellationToken);

        Assert.Equal(NeuronId.For<IChat>(test.Client.Owner, chatName), read.Subject);
        Assert.Collection(
            read.Transcript.Turns,
            turn => Assert.Equal(new ChatTurn(FromUser: true, "hello there"), turn),
            turn => Assert.Equal(new ChatTurn(FromUser: false, "Noted."), turn));
    }

    [Fact(Timeout = RequestTimeout, DisplayName =
        "a directed read-transcript request naming this capability's own addressed instance answers locally, with no grain call")]
    public async Task DirectedReadOfTheCapabilitysOwnInstanceAnswersLocally()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);

        test.Chat().Reply("Noted: hello default");
        await test.Client.GetGrainProxy<IChat>(DefaultInstance).Send(
            new SendMessage(CommandId.New(), "hello default"));

        var read = await test.Client.Get<IChat>()
            .SendAsync(new ReadTranscriptRequest(DefaultInstance), cancellationToken);

        Assert.Equal(NeuronId.For<IChat>(test.Client.Owner, DefaultInstance), read.Subject);
        Assert.Collection(
            read.Transcript.Turns,
            turn => Assert.Equal(new ChatTurn(FromUser: true, "hello default"), turn),
            turn => Assert.Equal(new ChatTurn(FromUser: false, "Noted: hello default"), turn));
    }

    [Fact(Timeout = RequestTimeout, DisplayName =
        "a read-transcript request honours an optional cap, keeping only the newest turns")]
    public async Task ReadTranscriptRequestHonoursTheMaxTurnsCap()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        const string chatName = "capped-transcript-read";

        foreach (var message in new[] { "first", "second", "third" })
        {
            test.Chat().Reply($"Noted: {message}");
            await test.Client.GetGrainProxy<IChat>(chatName).Send(new SendMessage(CommandId.New(), message));
        }

        var read = await test.Client.Get<IChat>()
            .SendAsync(new ReadTranscriptRequest(chatName, maxTurns: 2, CommandId.New()), cancellationToken);

        Assert.Equal(2, read.Transcript.Turns.Count);
        Assert.Equal(new ChatTurn(FromUser: true, "third"), read.Transcript.Turns[0]);
        Assert.Equal(new ChatTurn(FromUser: false, "Noted: third"), read.Transcript.Turns[1]);
    }

    [Fact(Timeout = TurnTimeout, DisplayName =
        "mid-turn the model reads another conversation's transcript and the turn completes with one correlated request and response")]
    public async Task ModelReadsAnotherConversationsTranscriptMidTurn()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        const string historyChat = "history-for-model-read";
        const string askingChat = "asking-about-history";
        var chatBroker = test.Neuron<IChat>();

        test.Chat().Reply("Noted: the launch is on Friday");
        await test.Client.GetGrainProxy<IChat>(historyChat).Send(
            new SendMessage(CommandId.New(), "the launch is on Friday"));

        test.Chat().ReplyWithCapabilityCall(
            ReadTranscriptTool,
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["chatName"] = historyChat });
        test.Chat().Reply(FinalAnswer);

        await test.Client.GetGrainProxy<IChat>(askingChat).Send(new SendMessage(
            CommandId.New(),
            $"use chat.read-transcript-request on {historyChat} and tell me what was said"));

        var read = await chatBroker.Outgoing.NextAsync<TranscriptRead>(cancellationToken);

        Assert.Equal(NeuronId.For<IChat>(test.Client.Owner, historyChat), read.Synapse.Subject);
        Assert.Contains(
            read.Synapse.Transcript.Turns,
            turn => turn.FromUser && turn.Text == "the launch is on Friday");

        var answered = await test.Neuron<IChat>(askingChat).Outgoing.NextAsync<AssistantResponded>(cancellationToken);
        Assert.Equal(FinalAnswer, answered.Synapse.Text);
    }

    [Fact(Timeout = TurnTimeout, DisplayName =
        "mid-turn the model reads the fixed broker's own addressed conversation while it is idle, and the turn completes")]
    public async Task ModelReadsTheBrokersOwnConversationMidTurn()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        const string askingChat = "asking-to-read-default";
        var chatBroker = test.Neuron<IChat>();

        test.Chat().Reply("Noted: default conversation note");
        await test.Client.GetGrainProxy<IChat>(DefaultInstance).Send(
            new SendMessage(CommandId.New(), "default conversation note"));

        test.Chat().ReplyWithCapabilityCall(
            ReadTranscriptTool,
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["chatName"] = DefaultInstance });
        test.Chat().Reply(FinalAnswer);

        await test.Client.GetGrainProxy<IChat>(askingChat).Send(new SendMessage(
            CommandId.New(),
            "use chat.read-transcript-request on default"));

        var read = await chatBroker.Outgoing.NextAsync<TranscriptRead>(cancellationToken);

        Assert.Equal(NeuronId.For<IChat>(test.Client.Owner, DefaultInstance), read.Synapse.Subject);
        Assert.Contains(
            read.Synapse.Transcript.Turns,
            turn => turn.FromUser && turn.Text == "default conversation note");

        var answered = await test.Neuron<IChat>(askingChat).Outgoing.NextAsync<AssistantResponded>(cancellationToken);
        Assert.Equal(FinalAnswer, answered.Synapse.Text);
    }

    [Fact(DisplayName =
        "a read-transcript request refuses a turn cap outside the bounds the description advertises, and an unaddressable conversation name")]
    public void ReadTranscriptRequestValidatesItsArguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReadTranscriptRequest(
            "main", ReadTranscriptRequest.MaximumMaxTurns + 1, CommandId.New()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReadTranscriptRequest(
            "main", ReadTranscriptRequest.MinimumMaxTurns - 1, CommandId.New()));

        var unaddressable = Assert.Throws<ArgumentException>(() => new ReadTranscriptRequest("other-owner/main"));
        Assert.Contains("not addressable", unaddressable.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => new ReadTranscriptRequest("chat name"));
    }
}
