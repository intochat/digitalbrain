using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.Chat;
using DigitalBrain.Introspection;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.OS.Assistant.Tests;

public sealed class IntrospectionCapabilityTurn(OSBehaviorsFixture fixture)
{
    private const int TurnTimeout = 120_000;
    private const string AssistantName = "assistant";
    private const string HistoryChat = "history";
    private const string FinalAnswer = "You have sent me two messages.";
    private const string UserMessagedType = "DigitalBrain.Chat.UserMessaged";

    private static readonly string TallyTool =
        ValidatedCapability.ToolNameFor("introspection.tally-journal-request", 1);

    private static readonly string TopologyTool =
        ValidatedCapability.ToolNameFor("introspection.read-topology-request", 1);

    [Fact(Timeout = TurnTimeout, DisplayName =
        "mid-turn the model tallies the owner's own chat journal and the turn completes with one correlated request and response")]
    public async Task ModelTalliesOwnerChatJournalMidTurn()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var introspection = test.Neuron<IIntrospection>();
        var assistant = test.Neuron<IAssistant>(AssistantName);

        await SendOwnerMessagesAsync(test, HistoryChat, "first message", "second message");

        test.Chat().ReplyWithCapabilityCall(
            TallyTool,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["neuronType"] = "chat",
                ["neuronName"] = HistoryChat,
                ["direction"] = JournalDirection.Outgoing,
            });
        test.Chat().Reply(FinalAnswer);

        await test.Client.GetGrainProxy<IChat>("asking").Send(new SendMessage(
            CommandId.New(),
            $"how many messages have I sent you? use introspection.tally-journal-request on {HistoryChat}"));

        Assert.Contains(TallyTool, test.Chat().LastTools);

        var selected = await assistant.Outgoing.NextAsync<CapabilityToolSelected>(cancellationToken);
        Assert.Equal(TallyTool, selected.Synapse.Tool);

        var request = await introspection.Incoming.NextAsync<TallyJournalRequest>(cancellationToken);
        var tallied = await introspection.Outgoing.NextAsync<JournalTallied>(cancellationToken);

        Assert.Equal(request.CorrelationId, tallied.CorrelationId);
        Assert.Equal(HistoryChat, request.Synapse.NeuronName);
        Assert.Null(tallied.Synapse.Error);
        Assert.Equal(NeuronId.For<IChat>(test.Client.Owner, HistoryChat), tallied.Synapse.Subject);
        Assert.Equal(2, Recorded(tallied.Synapse, UserMessagedType));
        Assert.True(tallied.Synapse.TotalRecorded >= 2);
    }

    [Fact(Timeout = TurnTimeout, DisplayName =
        "tallying the owner session mid-turn is refused with the reentrancy reason instead of deadlocking the turn")]
    public async Task TallyingTheOwnerSessionIsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var introspection = test.Neuron<IIntrospection>();

        test.Chat().ReplyWithCapabilityCall(
            TallyTool,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["neuronType"] = ISessionNeuron.GrainTypeName,
                ["neuronName"] = ISessionNeuron.InstanceName,
                ["direction"] = JournalDirection.Outgoing,
            });
        test.Chat().Reply(FinalAnswer);

        await test.Client.GetGrainProxy<IChat>("session-target").Send(new SendMessage(
            CommandId.New(),
            "use introspection.tally-journal-request on my session"));

        var tallied = await introspection.Outgoing.NextAsync<JournalTallied>(cancellationToken);

        Assert.NotNull(tallied.Synapse.Error);
        Assert.Contains("deadlock", tallied.Synapse.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(tallied.Synapse.Tallies);
    }

    [Fact(Timeout = TurnTimeout, DisplayName =
        "tallying the conversation that is still asking is answered as occupied, bounded, never hanging the turn")]
    public async Task TallyingTheConversationInFlightIsBounded()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var introspection = test.Neuron<IIntrospection>();
        const string askingChat = "asking-about-itself";

        test.Chat().ReplyWithCapabilityCall(
            TallyTool,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["neuronType"] = "chat",
                ["neuronName"] = askingChat,
                ["direction"] = JournalDirection.Outgoing,
            });
        test.Chat().Reply(FinalAnswer);

        await test.Client.GetGrainProxy<IChat>(askingChat).Send(new SendMessage(
            CommandId.New(),
            "use introspection.tally-journal-request on this very conversation"));

        var tallied = await introspection.Outgoing.NextAsync<JournalTallied>(cancellationToken);

        Assert.NotNull(tallied.Synapse.Error);
        Assert.Contains("did not answer within", tallied.Synapse.Error, StringComparison.Ordinal);
        Assert.Empty(tallied.Synapse.Tallies);
    }

    [Fact(Timeout = TurnTimeout, DisplayName =
        "mid-turn the model reads this brain's own topology: composed modules and the owner's activated neurons")]
    public async Task ModelReadsBrainTopologyMidTurn()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var introspection = test.Neuron<IIntrospection>();

        test.Chat().ReplyWithCapabilityCall(
            TopologyTool,
            new Dictionary<string, object?>(StringComparer.Ordinal));
        test.Chat().Reply(FinalAnswer);

        await test.Client.GetGrainProxy<IChat>("topology").Send(new SendMessage(
            CommandId.New(),
            "use introspection.read-topology-request to describe yourself"));

        var topology = await introspection.Outgoing.NextAsync<TopologyRead>(cancellationToken);

        Assert.Null(topology.Synapse.Error);
        Assert.Contains(IntrospectionModule.Id.Value, topology.Synapse.Modules);
        Assert.Contains(ChatModule.Id.Value, topology.Synapse.Modules);
        Assert.Contains(topology.Synapse.Neurons, neuron => neuron.GrainType == "chat");
        Assert.All(
            topology.Synapse.Neurons,
            neuron => Assert.StartsWith($"{test.Client.Owner.Value}/", neuron.Identity, StringComparison.Ordinal));
    }

    private static async Task SendOwnerMessagesAsync(TestBrain test, string chatName, params string[] messages)
    {
        foreach (var message in messages)
        {
            test.Chat().Reply($"Noted: {message}");
            await test.Client.GetGrainProxy<IChat>(chatName).Send(new SendMessage(CommandId.New(), message));
        }
    }

    private static long Recorded(JournalTallied tallied, string synapseType)
        => tallied.Tallies
            .Where(tally => string.Equals(tally.SynapseType, synapseType, StringComparison.Ordinal))
            .Sum(tally => tally.Recorded);
}
