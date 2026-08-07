using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using DigitalBrain.TestingTests.Harness;
using Orleans.Concurrency;
using Xunit;

namespace DigitalBrain.TestingTests;

public sealed class NeuronConcurrencyContracts
{
    [Fact(DisplayName = "the journal read is the only neuron contract method the kernel lets interleave")]
    public void OnlyTheJournalReadCarriesInterleavingAttributes()
    {
        var read = typeof(INeuron).GetMethod(nameof(INeuron.ReadJournal))!;

        Assert.True(read.IsDefined(typeof(AlwaysInterleaveAttribute), inherit: true));
        Assert.True(read.IsDefined(typeof(ReadOnlyAttribute), inherit: true));

        var mutating = typeof(INeuron)
            .GetMethods()
            .Where(method => method != read);

        Assert.All(mutating, method =>
        {
            Assert.False(method.IsDefined(typeof(AlwaysInterleaveAttribute), inherit: true));
            Assert.False(method.IsDefined(typeof(ReadOnlyAttribute), inherit: true));
        });
    }

    [Fact(DisplayName = "a neuron inherits the interleaving journal read without tripping the serialized-turn guard")]
    public void TheInterleavingJournalReadPassesTheGuard()
        => NeuronConcurrency.RequireSerializedTurns(typeof(Greeter));

    [Fact(DisplayName = "AlwaysInterleave on any method other than the journal read is still refused")]
    public void InterleavingAnyOtherMethodIsRefused()
    {
        var refusal = Assert.Throws<InvalidOperationException>(
            () => NeuronConcurrency.RequireSerializedTurns(typeof(InterleavingHandlerProbe)));

        Assert.Contains(nameof(AlwaysInterleaveAttribute), refusal.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(INeuron.ReadJournal), refusal.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "ReadOnly on any method other than the journal read is still refused")]
    public void ReadOnlyOnAnyOtherMethodIsRefused()
    {
        var refusal = Assert.Throws<InvalidOperationException>(
            () => NeuronConcurrency.RequireSerializedTurns(typeof(ReadOnlyHandlerProbe)));

        Assert.Contains(nameof(ReadOnlyAttribute), refusal.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "a neuron that redeclares ReadJournal on its own contract does not inherit the interleave licence")]
    public void RedeclaredJournalReadIsRefused()
    {
        var refusal = Assert.Throws<InvalidOperationException>(
            () => NeuronConcurrency.RequireSerializedTurns(typeof(RedeclaredJournalReadProbe)));

        Assert.Contains(nameof(AlwaysInterleaveAttribute), refusal.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "a reentrant neuron class is refused whatever its methods declare")]
    public void ReentrantClassIsRefused()
    {
        var refusal = Assert.Throws<InvalidOperationException>(
            () => NeuronConcurrency.RequireSerializedTurns(typeof(ReentrantProbe)));

        Assert.Contains(nameof(ReentrantAttribute), refusal.Message, StringComparison.Ordinal);
    }

    private interface IInterleavingHandler
    {
        [AlwaysInterleave]
        Task HandleAsync();
    }

    private interface IReadOnlyHandler
    {
        [ReadOnly]
        Task HandleAsync();
    }

    private interface IRedeclaredJournalRead
    {
        [AlwaysInterleave]
        Task<JournalRead> ReadJournal(JournalKind kind, long afterSequence);
    }

    private abstract class InterleavingHandlerProbe : IInterleavingHandler
    {
        public Task HandleAsync() => Task.CompletedTask;
    }

    private abstract class ReadOnlyHandlerProbe : IReadOnlyHandler
    {
        public Task HandleAsync() => Task.CompletedTask;
    }

    private abstract class RedeclaredJournalReadProbe : IRedeclaredJournalRead
    {
        public Task<JournalRead> ReadJournal(JournalKind kind, long afterSequence)
            => Task.FromResult(new JournalRead(0, [], null));
    }

    [Reentrant]
    private abstract class ReentrantProbe;
}
