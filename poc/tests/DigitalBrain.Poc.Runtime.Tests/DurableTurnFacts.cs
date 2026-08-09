using DigitalBrain.Poc.Runtime;
using Xunit;

namespace DigitalBrain.Poc.Runtime.Tests;

public sealed class DurableTurnFacts : IAsyncLifetime
{
    private PocDataRoot _run = null!;
    private DurableTurn _turns = null!;
    private DurableProbeNeuron _probe = null!;

    public ValueTask InitializeAsync()
    {
        _run = PocDataRoot.Create(TestPocRoot.Find());
        _turns = new DurableTurn(_run);
        _probe = new DurableProbeNeuron(_turns);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => _run.DisposeAsync();

    [Fact]
    public async Task StateAndOutgoingSynapseCommitTogether()
    {
        await _probe.HandleAsync(new IncrementAndEmit(), TestContext.Current.CancellationToken);

        Assert.Equal(1, await _probe.ReadCountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(
            ["IncrementAndEmit", "Emitted"],
            await new JournalStore(_run).ReadKindsAsync(TestContext.Current.CancellationToken));
        Assert.Single(await new Outbox(_run).ReadCommittedAsync(TestContext.Current.CancellationToken));
        Assert.True(await new JournalStore(_run).HasAcknowledgedReceiptAsync(
            "incrementing-input-1",
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ThrowAfterStateAndEmitLeavesNoAcknowledgedTurnOrOutboxWork()
    {
        await Assert.ThrowsAsync<ProbeFailureException>(
            () => _probe.HandleAsync(
                new ThrowAfterStateAndEmit(),
                TestContext.Current.CancellationToken));

        Assert.Equal(0, await _probe.ReadCountAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await new Outbox(_run).ReadPendingAsync(TestContext.Current.CancellationToken));
        Assert.False(await new JournalStore(_run).HasAcknowledgedReceiptAsync(
            "throwing-input-1",
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task StateOverTheConfiguredByteLimitRollsBackTheWholeTurn()
    {
        await Assert.ThrowsAsync<StateTooLargeException>(
            () => _probe.HandleAsync(
                new ReplaceProbeState(new string('x', 65_537)),
                TestContext.Current.CancellationToken));

        Assert.Equal(0, await _probe.ReadCountAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await new Outbox(_run).ReadPendingAsync(TestContext.Current.CancellationToken));
        Assert.False(await new JournalStore(_run).HasAcknowledgedReceiptAsync(
            "oversized-input-1",
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReplayingACommittedReceiptDoesNotRunTheHandlerTwice()
    {
        var input = new IncrementAndEmit("same-input");

        await _probe.HandleAsync(input, TestContext.Current.CancellationToken);
        await _probe.HandleAsync(input, TestContext.Current.CancellationToken);

        Assert.Equal(1, await _probe.ReadCountAsync(TestContext.Current.CancellationToken));
        Assert.Single(await new Outbox(_run).ReadCommittedAsync(TestContext.Current.CancellationToken));
    }
}
