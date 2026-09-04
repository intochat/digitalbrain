using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Synapses;
using DigitalBrain.Core;
using Orleans.Concurrency;
using Xunit;

namespace DigitalBrain.Substrate.Tests;

// Direct unit coverage of the guardrail that keeps a neuron's turns serialized: every neuron
// activation calls NeuronConcurrency.RequireSerializedTurns, and a hole here silently permits
// interleaving that breaks journal order and delivery lineage. These fixtures are never
// activated as real grains — RequireSerializedTurns only inspects the Type via reflection — so
// they are private nested stubs of INeuron rather than Neuron subclasses, kept out of the
// assembly's grain discovery so they cannot interfere with the routing tests in later tasks.
public sealed class NeuronConcurrencyTests
{
    private abstract class NeuronStub : INeuron, INeuronQuery
    {
        public Task HandleAsync(Subscribe signal, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task HandleAsync(Unsubscribe signal, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<JournalRead> ReadJournal(JournalKind kind, long afterSequence)
            => throw new NotSupportedException();

        public Task Watch(JournalKind kind, long afterSequence, IJournalObserver observer)
            => throw new NotSupportedException();

        public Task Unwatch(IJournalObserver observer)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<Synapse>> ReadSynapses()
            => throw new NotSupportedException();
    }

    private sealed class PlainNeuron : NeuronStub;

    [Reentrant]
    private sealed class ReentrantNeuron : NeuronStub;

    [StatelessWorker]
    private sealed class StatelessWorkerNeuron : NeuronStub;

    // AlwaysInterleave/ReadOnly must be declared on an INTERFACE method (ORLEANS0001 forbids
    // them on the grain class method directly), so each gets its own plain interface —
    // deliberately NOT extending INeuron/IGrain, so Orleans' codegen does not treat it as a
    // second grain interface needing its own RPC proxy, and IsKernelFreeRead does not exempt it.
    private interface IHasOwnAlwaysInterleaveMethod
    {
        [AlwaysInterleave]
        Task OwnAlwaysInterleaveMethod();
    }

    private interface IHasOwnReadOnlyMethod
    {
        [ReadOnly]
        Task OwnReadOnlyMethod();
    }

    private sealed class AlwaysInterleavingNeuron : NeuronStub, IHasOwnAlwaysInterleaveMethod
    {
        public Task OwnAlwaysInterleaveMethod() => Task.CompletedTask;
    }

    private sealed class ReadOnlyMethodNeuron : NeuronStub, IHasOwnReadOnlyMethod
    {
        public Task OwnReadOnlyMethod() => Task.CompletedTask;
    }

    [Fact]
    public void APlainNeuron_PassesWithoutThrowing()
    {
        var exception = Record.Exception(
            () => NeuronConcurrency.RequireSerializedTurns(typeof(PlainNeuron)));

        Assert.Null(exception);
    }

    [Fact]
    public void AReentrantNeuron_IsRefused()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => NeuronConcurrency.RequireSerializedTurns(typeof(ReentrantNeuron)));

        Assert.Contains(nameof(ReentrantAttribute), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AStatelessWorkerNeuron_IsRefused()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => NeuronConcurrency.RequireSerializedTurns(typeof(StatelessWorkerNeuron)));

        Assert.Contains(nameof(StatelessWorkerAttribute), exception.Message, StringComparison.Ordinal);
    }

    // Proves the INeuronQuery whitelist did not over-widen: a neuron's OWN method carrying
    // AlwaysInterleave — not a query-port declaration — must still be refused.
    [Fact]
    public void ANeuronDeclaringItsOwnAlwaysInterleaveMethod_IsRefused()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => NeuronConcurrency.RequireSerializedTurns(typeof(AlwaysInterleavingNeuron)));

        Assert.Contains(nameof(AlwaysInterleaveAttribute), exception.Message, StringComparison.Ordinal);
    }

    // Same proof for ReadOnly: a neuron's own ReadOnly method is not a query-port declaration
    // and must still be refused.
    [Fact]
    public void ANeuronDeclaringItsOwnReadOnlyMethod_IsRefused()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => NeuronConcurrency.RequireSerializedTurns(typeof(ReadOnlyMethodNeuron)));

        Assert.Contains(nameof(ReadOnlyAttribute), exception.Message, StringComparison.Ordinal);
    }
}
