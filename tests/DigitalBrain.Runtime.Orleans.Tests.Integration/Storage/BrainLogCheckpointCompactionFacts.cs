using System.Text;
using DigitalBrain.Kernel;
using DigitalBrain.Runtime.Orleans.Storage;
using DigitalBrain.Runtime.Orleans.Synapses;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Storage;

public sealed class BrainLogCheckpointCompactionFacts
{
    private static readonly NeuronId Alpha = NeuronId.Parse("b1/chat/alpha");
    private static readonly NeuronId Beta = NeuronId.Parse("b1/chat/beta");
    private static readonly NeuronId Gamma = NeuronId.Parse("b1/worker/gamma");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ThousandsOfCommitsCompactViaThresholdAndReplaySnapshotPlusTailWithFullStateAndFloorFidelity()
    {
        using var home = new TempHome();
        var time = new ManualTime();
        var cursors = new FixedCursors(2100);

        await using var store = new BrainStore(home.Path, services =>
        {
            services.AddSingleton<TimeProvider>(time);
            services.AddSingleton<IRetentionPolicy>(new RetentionPolicy(BrainStore.Window(400), time, cursors));
            services.Configure<BrainStoreOptions>(o =>
            {
                o.CheckpointEntriesThreshold = 500;
                o.CheckpointBytesThreshold = 64 * 1024 * 1024;
            });
        });

        var log = await store.Log();
        var journal = new BrainLogJournal(store.Logs, store.History(), id => BrainStore.GrainOf(id).ToString());

        var expectedState = new Dictionary<(NeuronId Owner, string Name), (byte[] Bytes, long Version)>();
        var expectedCursors = new Dictionary<NeuronId, (byte[] Bytes, long Version)>();
        long alphaSignals = 0;
        long betaSignals = 0;

        // Append 2,535 commits across multiple owners, state mutations, clears, cursor side-writes, and signal publishers.
        // Because CheckpointEntriesThreshold = 500, 5 automatic checkpoints compact the log while leaving a 35-commit uncompacted tail.
        for (var i = 1; i <= 2535; i++)
        {
            // Advance clock past the 7-day retention window after commit 2000 so retention trims older signals on checkpoint.
            if (i == 2200)
            {
                time.Now = time.Now.AddDays(10);
            }
            else
            {
                time.Now = time.Now.AddSeconds(1);
            }

            var owner = (i % 3) switch
            {
                0 => Alpha,
                1 => Beta,
                _ => Gamma,
            };

            var stateName = $"slot-{i % 7}";
            var hasExisting = expectedState.TryGetValue((owner, stateName), out var existing);

            StateWrite stateWrite;
            byte[]? writtenStateBytes = null;
            bool isClear = hasExisting && i % 29 == 0;
            if (isClear)
            {
                stateWrite = new StateWrite(stateName, StateChange.Clear, ReadOnlyMemory<byte>.Empty, existing.Version);
            }
            else
            {
                writtenStateBytes = Encoding.UTF8.GetBytes($"{{\"commit\":{i},\"owner\":\"{owner}\",\"slot\":\"{stateName}\"}}");
                stateWrite = new StateWrite(stateName, StateChange.Write, writtenStateBytes, hasExisting ? existing.Version : null);
            }

            CursorMove? cursorMove = null;
            byte[]? writtenCursorBytes = null;
            if (i % 11 == 0)
            {
                writtenCursorBytes = Subscriptions.None(owner).Encode();
                var prevCursor = expectedCursors.TryGetValue(owner, out var cur) ? (long?)cur.Version : null;
                cursorMove = new CursorMove(Subscriptions.RecordName, writtenCursorBytes, prevCursor);
            }

            IReadOnlyList<Signal> signals = owner == Alpha
                ? [new Count((int)(++alphaSignals))]
                : owner == Beta && i % 2 == 1
                    ? [new Note($"beta-{++betaSignals}")]
                    : [];

            var result = await journal.Append(new Commit(owner, stateWrite, signals, cursorMove, new Stamp($"trace-{i % 5}", $"cause-{i}")), Ct);

            if (isClear)
            {
                expectedState.Remove((owner, stateName));
            }
            else
            {
                expectedState[(owner, stateName)] = (writtenStateBytes!, result.Version);
            }

            if (cursorMove is not null)
            {
                expectedCursors[owner] = (writtenCursorBytes!, result.Version);
            }

            if (i % 500 == 0)
            {
                await log.WaitForAutoCheckpointAsync(Ct);
            }
        }

        await log.WaitForAutoCheckpointAsync(Ct);

        Assert.True(log.CheckpointCount >= 5);
        Assert.NotEqual("initial", log.Generation);
        Assert.InRange(log.UncompactedEntries, 1, 100);

        var dir = store.DirectoryOf();
        var snapshotsOnDisk = Directory.GetFiles(dir, "*.snapshot");
        var logsOnDisk = Directory.GetFiles(dir, "*.log");
        Assert.Single(snapshotsOnDisk);
        Assert.Single(logsOnDisk);

        // Snapshot holds compacted state + retained signals while the tail .log file stays small.
        var snapshotBytes = new FileInfo(snapshotsOnDisk[0]).Length;
        var tailLogBytes = new FileInfo(logsOnDisk[0]).Length;
        Assert.True(snapshotBytes > 0);
        Assert.True(tailLogBytes < snapshotBytes);

        // Restart and replay from latest checkpoint + tail commits.
        await store.Reopen();

        var reopenedLog = await store.Log();
        var reopenedJournal = new BrainLogJournal(store.Logs, store.History(), id => BrainStore.GrainOf(id).ToString());

        Assert.Equal(2535L, reopenedLog.Sequence);
        Assert.Equal(alphaSignals + betaSignals, reopenedLog.LastPosition);

        // Verify every live and cleared state record + optimistic concurrency version across all owners and slots.
        foreach (var owner in new[] { Alpha, Beta, Gamma })
        {
            for (var s = 0; s < 7; s++)
            {
                var stateName = $"slot-{s}";
                var actual = await reopenedJournal.ReadRecord(owner, stateName, Ct);
                if (expectedState.TryGetValue((owner, stateName), out var expected))
                {
                    Assert.NotNull(actual);
                    Assert.Equal(expected.Version, actual.Version);
                    Assert.Equal(expected.Bytes, actual.Bytes.ToArray());
                }
                else
                {
                    Assert.Null(actual);
                }
            }

            var actualCursor = await reopenedJournal.ReadRecord(owner, Subscriptions.RecordName, Ct);
            if (expectedCursors.TryGetValue(owner, out var expectedCursor))
            {
                Assert.NotNull(actualCursor);
                Assert.Equal(expectedCursor.Version, actualCursor.Version);
                Assert.Equal(expectedCursor.Bytes, actualCursor.Bytes.ToArray());
            }
        }

        // Verify FloorOf on IJournalView was accurately restored from the compacted snapshot!
        var alphaSlice = await reopenedLog.PublisherSignals(Alpha.ToString(), 0, int.MaxValue, Ct);
        Assert.True(alphaSlice.Floor > 0);
        Assert.Equal(alphaSignals, alphaSlice.Last);
        Assert.Equal(alphaSlice.Floor + 1, alphaSlice.Held[0].Sequence);
        Assert.True(reopenedLog.FloorOf(Alpha).Value > 0);

        // Verify appending a new commit after restart continues sequence, optimistic concurrency ETag checks, and publisher numbering cleanly.
        var sampleKey = expectedState.Keys.First(k => k.Owner == Alpha);
        var sampleExpected = expectedState[sampleKey];
        var postRestartCommit = await reopenedJournal.Append(
            new Commit(
                Alpha,
                new StateWrite(sampleKey.Name, StateChange.Write, "after-restart"u8.ToArray(), sampleExpected.Version),
                [new Count((int)(alphaSignals + 1))],
                null,
                new Stamp("trace-post", null)),
            Ct);

        Assert.Equal(2536L, postRestartCommit.Version);
        Assert.Equal(alphaSignals + 1, postRestartCommit.FirstSequence);
    }

    [Fact]
    public async Task PeriodicCheckpointIntervalTriggersCompactionBelowEntryAndByteThresholdsAndSurvivesRestart()
    {
        using var home = new TempHome();
        var time = new ManualTime();

        await using var store = new BrainStore(home.Path, services =>
        {
            services.AddSingleton<TimeProvider>(time);
            services.Configure<BrainStoreOptions>(o =>
            {
                o.CheckpointEntriesThreshold = 100_000;
                o.CheckpointBytesThreshold = 100 * 1024 * 1024;
                o.CheckpointInterval = TimeSpan.FromMinutes(5);
            });
        });

        var log = await store.Log();
        var journal = new BrainLogJournal(store.Logs, store.History(), id => BrainStore.GrainOf(id).ToString());

        // Write 5 commits within 2 minutes (< 5 minute CheckpointInterval): no auto-checkpoint yet.
        long? prevVersion = null;
        for (var i = 1; i <= 5; i++)
        {
            time.Now = time.Now.AddSeconds(20);
            var res = await journal.Append(
                new Commit(
                    Alpha,
                    new StateWrite("periodic", StateChange.Write, Encoding.UTF8.GetBytes($"step-{i}"), prevVersion),
                    [new Note($"n-{i}")],
                    null,
                    new Stamp(null, null)),
                Ct);
            prevVersion = res.Version;
        }

        await log.WaitForAutoCheckpointAsync(Ct);
        Assert.Equal(0, log.CheckpointCount);
        Assert.Equal("initial", log.Generation);
        Assert.Equal(5, log.UncompactedEntries);

        // Advance past the 5-minute CheckpointInterval and append 1 more commit: periodic checkpoint triggers automatically!
        time.Now = time.Now.AddMinutes(5);
        var sixth = await journal.Append(
            new Commit(
                Alpha,
                new StateWrite("periodic", StateChange.Write, "step-6"u8.ToArray(), prevVersion),
                [new Note("n-6")],
                null,
                new Stamp(null, null)),
            Ct);

        await log.WaitForAutoCheckpointAsync(Ct);
        Assert.Equal(1, log.CheckpointCount);
        Assert.NotEqual("initial", log.Generation);
        Assert.Equal(0, log.UncompactedEntries);

        // Append 2 tail commits after the periodic checkpoint without crossing another 5-minute window.
        time.Now = time.Now.AddSeconds(10);
        var seventh = await journal.Append(
            new Commit(
                Alpha,
                new StateWrite("periodic", StateChange.Write, "step-7"u8.ToArray(), sixth.Version),
                [new Note("n-7")],
                null,
                new Stamp(null, null)),
            Ct);
        var eighth = await journal.Append(
            new Commit(
                Alpha,
                new StateWrite("periodic", StateChange.Write, "step-8"u8.ToArray(), seventh.Version),
                [new Note("n-8")],
                null,
                new Stamp(null, null)),
            Ct);

        await log.WaitForAutoCheckpointAsync(Ct);
        Assert.Equal(1, log.CheckpointCount);
        Assert.Equal(2, log.UncompactedEntries);

        // Restart and verify checkpoint + 2 tail commits replay seamlessly.
        await store.Reopen();
        var reopenedJournal = new BrainLogJournal(store.Logs, store.History(), id => BrainStore.GrainOf(id).ToString());
        var reopenedLog = await store.Log();

        var record = await reopenedJournal.ReadRecord(Alpha, "periodic", Ct);
        Assert.NotNull(record);
        Assert.Equal(eighth.Version, record.Version);
        Assert.Equal("step-8", Encoding.UTF8.GetString(record.Bytes.Span));

        var slice = await reopenedLog.PublisherSignals(Alpha.ToString(), 0, 20, Ct);
        Assert.Equal(8, slice.Last);
        Assert.Equal(Enumerable.Range(1, 8).Select(n => $"n-{n}"), slice.Held.Select(s => ((Note)store.Payloads.DecodeSignal(s.Payload)).Text));
    }
}
