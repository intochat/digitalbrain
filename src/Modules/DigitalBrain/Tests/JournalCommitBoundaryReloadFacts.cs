using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;
using Orleans.Serialization.Session;
using Xunit;
using JournalEntry = DigitalBrain.Core.JournalEntry;

namespace DigitalBrain.Tests;

public sealed class JournalCommitBoundaryReloadFacts
{
    [Fact]
    public void BoundariesCapturedBeforeReloadChangeNeitherCommittedCursorNorTallies()
    {
        var (services, retained, tallies, lastSequence, window, first, second) = CreateWindow();
        using var provider = services;

        window.Append(first);
        var boundaryA = window.CaptureCommitBoundary();
        window.Append(second);
        var boundaryB = window.CaptureCommitBoundary();
        Assert.Equal(0, window.Read(0).ResumeSequence);
        Assert.Empty(window.Read(0).Delta);

        window.NoteCommitted(boundaryB);
        Assert.Equal(2, window.Read(0).ResumeSequence);
        Assert.Equal([first, second], window.Read(0).Delta);
        Assert.Equal(2, window.Snapshot().TotalRecorded);

        // RevertPendingChangesAsync restores the durable collections before NoteReloaded runs.
        retained.RemoveAt(1);
        tallies.Remove(second.Signal.Type);
        lastSequence.Value = 1;
        window.NoteReloaded();

        window.NoteCommitted(boundaryA);
        AssertReloadedWindow();

        // A's sequence equals the restored sequence; B also proves an old boundary cannot advance it.
        window.NoteCommitted(boundaryB);
        AssertReloadedWindow();

        void AssertReloadedWindow()
        {
            var read = window.Read(0);
            Assert.Equal(lastSequence.Value, read.ResumeSequence);
            Assert.Equal(first, Assert.Single(read.Delta));
            Assert.All(read.Delta, delivery => Assert.True(delivery.Sequence <= lastSequence.Value));
            Assert.False(read.Gap);
            Assert.Equal(tallies.Values.Sum(), read.TotalRecorded);
            var snapshot = window.Snapshot();
            Assert.Equal(lastSequence.Value, snapshot.LastSequence);
            Assert.Equal(retained.Count, snapshot.RetainedCount);
            Assert.Equal(tallies.Values.Sum(), snapshot.TotalRecorded);
            Assert.Equal(new JournalTally(first.Signal.Type, tallies[first.Signal.Type]), Assert.Single(snapshot.Tallies));
        }
    }

    [Fact]
    public void BoundaryCapturedAfterReloadAdvancesCursorAndOlderSameGenerationBoundaryCannotMoveItBack()
    {
        var (services, retained, tallies, lastSequence, window, first, second) = CreateWindow();
        using var provider = services;
        window.Append(first);
        window.Append(second);
        window.NoteCommitted(window.CaptureCommitBoundary());

        // RevertPendingChangesAsync restores the durable collections before NoteReloaded runs.
        retained.RemoveAt(1);
        tallies.Remove(second.Signal.Type);
        lastSequence.Value = 1;
        window.NoteReloaded();

        var earlierCurrentBoundary = window.CaptureCommitBoundary();
        window.Append(second);
        var currentBoundary = window.CaptureCommitBoundary();
        Assert.Equal(1, window.Read(0).ResumeSequence);
        window.NoteCommitted(currentBoundary);
        Assert.Equal(2, window.Read(0).ResumeSequence);
        Assert.Equal([first, second], window.Read(0).Delta);
        Assert.Equal(2, window.Snapshot().TotalRecorded);

        window.NoteCommitted(earlierCurrentBoundary);
        Assert.Equal(2, window.Read(0).ResumeSequence);
        Assert.Equal([first, second], window.Read(0).Delta);
        Assert.Equal(2, window.Snapshot().TotalRecorded);
    }

    private static (ServiceProvider Services, MemoryDurableList<byte[]> Retained,
        MemoryDurableDictionary<string, long> Tallies, MemoryDurableSequence LastSequence,
        JournalWindow Window, SignalDelivery First, SignalDelivery Second) CreateWindow()
    {
        var services = new ServiceCollection();
        services.AddSerializer();
        var provider = services.BuildServiceProvider();
        var retained = new MemoryDurableList<byte[]>();
        var tallies = new MemoryDurableDictionary<string, long>();
        var lastSequence = new MemoryDurableSequence();
        var window = new JournalWindow(retained, tallies, lastSequence,
            provider.GetRequiredService<Serializer<JournalEntry>>(),
            provider.GetRequiredService<SerializerSessionPool>());
        window.NoteReloaded();
        var first = SignalDelivery.Create(Signal.Create("First", "{}"), NeuronId.Plain("source"), 1, TimeProvider.System);
        var second = SignalDelivery.Create(Signal.Create("Second", "{}"), NeuronId.Plain("source"), 2, TimeProvider.System);
        return (provider, retained, tallies, lastSequence, window, first, second);
    }

    private sealed class MemoryDurableList<T> : List<T>, IDurableList<T>;

    private sealed class MemoryDurableDictionary<TKey, TValue> : Dictionary<TKey, TValue>, IDurableDictionary<TKey, TValue>
        where TKey : notnull;

    private sealed class MemoryDurableSequence : IDurableValue<long>
    {
        public long Value { get; set; }
    }
}
