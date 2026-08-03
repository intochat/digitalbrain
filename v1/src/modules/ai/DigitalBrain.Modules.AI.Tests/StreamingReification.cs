using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.AI.Ollama;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.ModuleTests;

public sealed class StreamingReification(ModuleFixture fixture)
{
    private const string ModelName = "streaming-reification-model";
    private const string RelayName = "streaming-reification-relay";
    private const string ContractlessTargetName = "streaming-reification-contractless";
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

    [Fact(DisplayName = "a streamed capability call is journaled like a Task capability call")]
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

    [Fact(DisplayName = "a streamed capability whose whole stream arrives in the first batch terminates once as Completed")]
    public async Task SingleBatchStreamTerminatesOnceAsCompleted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var relay = test.Neuron<IStreamingRelayProbe>(RelayName);
        var model = test.Neuron<ILlama32>(ModelName);
        test.Chat().Reply(ScriptedReply);

        var updates = await relay.Reference.CollectStreamingUpdates(ModelName, UserPrompt);

        Assert.NotEmpty(updates);

        var streamed = await StreamedRequestOf(relay, cancellationToken);
        var completed = await relay.Outgoing.ReadAsync<CapabilityCompleted>(afterSequence: 0, cancellationToken: cancellationToken);

        Assert.Single(completed, fact => fact.Synapse.Request == streamed.SynapseId);
        Assert.Empty(await relay.Outgoing.ReadAsync<CapabilityAbandoned>(afterSequence: 0, cancellationToken: cancellationToken));
        Assert.Empty(await relay.Outgoing.ReadAsync<CapabilityFailed>(afterSequence: 0, cancellationToken: cancellationToken));
        Assert.Equal(0, await relay.Reference.CountPendingStreamedRequests());

        var received = await model.Incoming.ReadAsync<CapabilityRequested>(afterSequence: 0, cancellationToken: cancellationToken);
        Assert.Single(received, fact => fact.SynapseId == streamed.SynapseId);
    }

    [Fact(DisplayName = "a streamed capability drained over several batches terminates once, with no fact per chunk")]
    public async Task MultiBatchStreamTerminatesOnceWithoutJournalingChunks()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var relay = test.Neuron<IStreamingRelayProbe>(RelayName);
        test.Chat().Reply(ScriptedReply);

        var drained = await relay.Reference.DrainStreamingOneUpdatePerBatch(ModelName, UserPrompt);

        Assert.NotEqual(0, drained);

        var requested = await relay.Outgoing.ReadAsync<CapabilityRequested>(afterSequence: 0, cancellationToken: cancellationToken);
        var completed = await relay.Outgoing.ReadAsync<CapabilityCompleted>(afterSequence: 0, cancellationToken: cancellationToken);
        var streamed = Assert.Single(requested, fact => fact.Synapse.Method == nameof(ILLM.RespondStreaming));

        Assert.Single(completed, fact => fact.Synapse.Request == streamed.SynapseId);
        Assert.Empty(await relay.Outgoing.ReadAsync<CapabilityAbandoned>(afterSequence: 0, cancellationToken: cancellationToken));
        Assert.Equal(0, await relay.Reference.CountPendingStreamedRequests());
    }

    [Fact(DisplayName = "a streamed capability abandoned before its end journals CapabilityAbandoned and leaves nothing pending")]
    public async Task AbandonedStreamTerminatesAsAbandonedAndLeavesNothingPending()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var relay = test.Neuron<IStreamingRelayProbe>(RelayName);
        test.Chat().Reply(ScriptedReply);

        await relay.Reference.AbandonStreamingAfterFirstUpdate(ModelName, UserPrompt);

        var streamed = await StreamedRequestOf(relay, cancellationToken);
        var abandoned = await relay.Outgoing.ReadAsync<CapabilityAbandoned>(afterSequence: 0, cancellationToken: cancellationToken);

        Assert.Single(abandoned, fact => fact.Synapse.Request == streamed.SynapseId);
        Assert.Empty(await relay.Outgoing.ReadAsync<CapabilityCompleted>(afterSequence: 0, cancellationToken: cancellationToken));
        Assert.Equal(0, await relay.Reference.CountPendingStreamedRequests());
    }

    [Fact(DisplayName = "a streamed call whose target does not implement the resolved contract begins no request on that target")]
    public async Task ContractlessTargetBeginsNoIncomingRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var relay = test.Neuron<IStreamingRelayProbe>(RelayName);
        var contractless = test.Neuron<IStreamingRelayProbe>(ContractlessTargetName);
        test.Chat().Reply(ScriptedReply);

        await Assert.ThrowsAnyAsync<Exception>(
            () => relay.Reference.StreamFromTargetWithoutTheContract(ContractlessTargetName));

        var streamed = await StreamedRequestOf(relay, cancellationToken);
        var failed = await relay.Outgoing.ReadAsync<CapabilityFailed>(afterSequence: 0, cancellationToken: cancellationToken);

        Assert.Single(failed, fact => fact.Synapse.Request == streamed.SynapseId);
        Assert.Empty(await contractless.Incoming.ReadAsync<CapabilityRequested>(afterSequence: 0, cancellationToken: cancellationToken));
        Assert.Equal(0, await relay.Reference.CountPendingStreamedRequests());
    }

    private static async Task<ObservedSynapse<CapabilityRequested>> StreamedRequestOf(
        TestNeuron<IStreamingRelayProbe> relay,
        CancellationToken cancellationToken)
    {
        var requested = await relay.Outgoing.ReadAsync<CapabilityRequested>(afterSequence: 0, cancellationToken: cancellationToken);

        return Assert.Single(requested, fact => fact.Synapse.Method == nameof(ILLM.RespondStreaming));
    }
}
