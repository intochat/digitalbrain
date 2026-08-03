using DigitalBrain.Abstractions;
using DigitalBrain.Chat;
using DigitalBrain.Introspection;
using DigitalBrain.OS.AgentTools;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.OS.Assistant.Tests;

public sealed class IntrospectionToolVocabulary(OSBehaviorsFixture fixture)
{
    private const int ToolTimeout = 60_000;

    [Fact(DisplayName = "the introspection MCP tool names stay frozen")]
    public void ToolNamesAreFrozen()
    {
        Assert.Equal("list_active_neurons", AgentToolEndpoints.ListActiveNeuronsToolName);
        Assert.Equal("read_neuron_journal", AgentToolEndpoints.ReadNeuronJournalToolName);
        Assert.Equal("read_chat_transcript", AgentToolEndpoints.ReadChatTranscriptToolName);
    }

    [Fact(Timeout = ToolTimeout, DisplayName =
        "list_active_neurons answers from the introspection neuron, so that neuron is itself listed")]
    public async Task ListActiveNeuronsAnswersFromTheIntrospectionNeuron()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        const string chatName = "tool-topology";

        await test.Client.GetGrainProxy<IChat>(chatName).Read();

        var tools = new DigitalBrainIntrospectionTools(test.Client);
        var neurons = await tools.ListActiveNeuronsAsync();

        Assert.Contains(
            neurons,
            neuron => neuron.GrainType == "chat"
                && neuron.Identity == $"{test.Client.Owner.Value}/{chatName}");
        Assert.Contains(neurons, neuron => neuron.GrainType == "introspection");
    }

    [Fact(Timeout = ToolTimeout, DisplayName =
        "read_neuron_journal refuses a neuron that is not activated rather than activating it to look")]
    public async Task ReadNeuronJournalRefusesRatherThanActivating()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        const string ghost = "never-opened-by-mcp";

        await test.Client.GetGrainProxy<IChat>("tool-journal-anchor").Read();

        var tools = new DigitalBrainIntrospectionTools(test.Client);
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => tools.ReadNeuronJournalAsync("chat", ghost));

        Assert.Contains("never activates a neuron", refused.Message, StringComparison.Ordinal);

        var topology = await test.Client.Get<IIntrospection>()
            .SendAsync(new ReadTopologyRequest(), cancellationToken);
        Assert.DoesNotContain(
            topology.Neurons,
            neuron => neuron.Identity.EndsWith($"/{ghost}", StringComparison.Ordinal));
    }

    [Fact(Timeout = ToolTimeout, DisplayName =
        "read_neuron_journal and introspection.read-journal-request hand back the same causal facts")]
    public async Task ToolAndSynapseHandBackTheSamePage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        const string chatName = "tool-journal";

        test.Chat().Reply("Noted.");
        await test.Client.GetGrainProxy<IChat>(chatName).Send(new SendMessage(CommandId.New(), "hello"));

        var tools = new DigitalBrainIntrospectionTools(test.Client);
        var page = await tools.ReadNeuronJournalAsync("chat", chatName);

        var read = await test.Client.Get<IIntrospection>()
            .SendAsync(new ReadJournalRequest("chat", chatName), cancellationToken);

        Assert.Equal(nameof(JournalKind.Outgoing), page.Kind);
        Assert.Equal(read.Subject.ToString(), page.Neuron);
        Assert.NotEmpty(page.Entries);
        Assert.Equal(
            read.Entries.Select(static entry => (entry.Sequence, entry.Synapse)),
            page.Entries.Select(static entry => (entry.Sequence, entry.Synapse)));
    }
}
