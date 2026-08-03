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

    [Fact(Timeout = 10_000, DisplayName =
        "mid-turn the fixed broker's own conversation calling read-transcript contends with its own occupied turn at the delivery layer, unlike any other conversation making the identical call")]
    public async Task DefaultConversationsOwnMidTurnCallContendsAtDelivery()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        const string otherAsker = "another-conversation-not-default";
        const string readTarget = "some-other-conversation";

        // Control: a DIFFERENT conversation making the identical call resolves promptly, because the
        // delivery target for this capability - the fixed default instance - is a distinct, idle
        // activation from the one asking.
        test.Chat().ReplyWithCapabilityCall(
            ReadTranscriptTool,
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["chatName"] = readTarget });
        test.Chat().Reply(FinalAnswer);

        var control = test.Client.GetGrainProxy<IChat>(otherAsker).Send(new SendMessage(
            CommandId.New(), $"use chat.read-transcript-request on {readTarget}"));
        await control.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);

        // Dispatch for this capability always targets the fixed default instance, so when the
        // ASKING conversation is that instance, the delivery this very call depends on has to queue
        // behind the turn asking for it - before HandleAsync, before subject==Id/!=Id is even
        // evaluated.
        test.Chat().ReplyWithCapabilityCall(
            ReadTranscriptTool,
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["chatName"] = readTarget });

        var contended = test.Client.GetGrainProxy<IChat>(DefaultInstance).Send(new SendMessage(
            CommandId.New(), $"use chat.read-transcript-request on {readTarget}"));

        // A window a small fraction of the delivery attempt's own 30-second bound
        // (DeliveryPolicy.DeliveryAttemptTimeout) is enough to observe the contended call still
        // pending, given the control above already proves the identical call resolves near-instantly
        // when it isn't contended. Waiting out the eventual resolution
        // (SynapseCapabilityTool.ToolResponseWait, 90s) would only re-confirm bounds already read
        // from DeliveryPolicy, at a much higher cost, for the same conclusion. Peeking chat/default's
        // own journal to prove non-delivery directly is not a cheaper alternative: that read is
        // itself routed through the owner session (INeuron.ReadJournal is not a client entry point),
        // and session's own outbox-drain turn is what is actually blocked awaiting the very Deliver
        // call this test is about - the peek would queue behind it too.
        await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);

        Assert.Contains(ReadTranscriptTool, test.Chat().LastTools);
        Assert.False(
            contended.IsCompleted,
            "The default conversation's own mid-turn call should still be blocked on delivery contention, unlike the control call above.");
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
