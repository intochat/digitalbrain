using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.ModuleTests;

public sealed class StreamingReification(ModuleFixture fixture)
{
    private const string ModelName = "streaming-reification-model";
    private const string RelayName = "streaming-reification-relay";
    private const string UserPrompt = "hi";
    private const string ScriptedReply = "hello world";

    [Fact(DisplayName = "a Task-returning capability call is journaled with a terminal outcome")]
    public async Task NonStreamingCapabilityIsJournaled()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var relay = test.Neuron<IStreamingRelayProbe>(RelayName);
        test.Chat().Reply(ScriptedReply);

        var response = await relay.Reference.RelayRespond(ModelName, UserPrompt);

        Assert.Equal(ScriptedReply, response.Text);

        var requested = await relay.Outgoing.NextAsync<CapabilityRequested>(cancellationToken);
        Assert.Equal(nameof(ILLM.Respond), requested.Synapse.Method);

        var outcomes = await relay.Outgoing.ReadAsync<CapabilityCompleted>(afterSequence: 0, cancellationToken: cancellationToken);
        Assert.Contains(outcomes, fact => fact.Synapse.Request == requested.SynapseId);
    }

    [Fact(Explicit = true, DisplayName = "a streamed capability call is journaled like a Task capability call")]
    public async Task StreamedCapabilityIsJournaled()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var relay = test.Neuron<IStreamingRelayProbe>(RelayName);
        test.Chat().Reply(ScriptedReply);

        var updates = await relay.Reference.CollectStreamingUpdates(ModelName, UserPrompt);

        Assert.NotEmpty(updates);

        var requested = await relay.Outgoing.ReadAsync<CapabilityRequested>(afterSequence: 0, cancellationToken: cancellationToken);
        Assert.Contains(requested, fact => fact.Synapse.Method == nameof(ILLM.RespondStreaming));
    }
}
