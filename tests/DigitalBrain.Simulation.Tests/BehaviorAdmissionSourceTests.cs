using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Scripting.Startup;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class BehaviorAdmissionSourceTests
{
    [Fact]
    public async Task Hydration_reads_only_current_revisions_and_removal_produces_empty_snapshot()
    {
        await using var simulation = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var behaviors = simulation.Brain.Get<IBehaviors>();
        var cancellation = TestContext.Current.CancellationToken;
        await behaviors.SendAsync(new AdmitBehavior("review", "return 1;"), cancellation);
        await behaviors.SendAsync(new AdmitBehavior("review", "return 2;"), cancellation);
        var source = new DigitalBrainBehaviorAdmissionSource(simulation.Brain, simulation.Grains);
        await using var snapshots = source.WatchAsync(cancellation).GetAsyncEnumerator(cancellation);

        Assert.True(await snapshots.MoveNextAsync());
        Assert.Equal("return 2;", Assert.Single(snapshots.Current).Source);

        await behaviors.SendAsync(new RemoveBehavior("review"), cancellation);
        Assert.True(await snapshots.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10), cancellation));
        Assert.Empty(snapshots.Current);
    }

    [Fact]
    public async Task Idle_journal_still_refreshes_current_definitions()
    {
        await using var simulation = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var cancellation = TestContext.Current.CancellationToken;
        var source = new DigitalBrainBehaviorAdmissionSource(simulation.Brain, simulation.Grains);
        await using var snapshots = source.WatchAsync(cancellation).GetAsyncEnumerator(cancellation);
        Assert.True(await snapshots.MoveNextAsync());
        Assert.Empty(snapshots.Current);

        // No event wakes the observer. This same refresh repairs an observer silently
        // lost when an activation or silo goes away.
        Assert.True(await snapshots.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(8), cancellation));
        Assert.Empty(snapshots.Current);
    }

    [Fact]
    public async Task Missed_journal_retention_rehydrates_current_program_and_restart_keeps_it()
    {
        await using var simulation = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var behaviors = simulation.Brain.Get<IBehaviors>();
        var cancellation = TestContext.Current.CancellationToken;
        var source = new DigitalBrainBehaviorAdmissionSource(simulation.Brain, simulation.Grains);
        await using var snapshots = source.WatchAsync(cancellation).GetAsyncEnumerator(cancellation);
        Assert.True(await snapshots.MoveNextAsync());
        Assert.Empty(snapshots.Current);

        // Cross both the snapshot/watch boundary and the bounded journal's retention.
        for (var revision = 0; revision < 520; revision++)
        {
            await behaviors.SendAsync(new AdmitBehavior("review", $"return {revision};"), cancellation);
        }
        var journal = await behaviors.ReadJournalAsync(JournalKind.Outgoing, 0, cancellation);
        Assert.NotNull(journal.ResetSnapshot);
        Assert.Empty(journal.Delta);

        Assert.True(await snapshots.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10), cancellation));
        Assert.Equal("return 519;", Assert.Single(snapshots.Current).Source);

        await using var restarted = source.WatchAsync(cancellation).GetAsyncEnumerator(cancellation);
        Assert.True(await restarted.MoveNextAsync());
        Assert.Equal("return 519;", Assert.Single(restarted.Current).Source);
    }
}
