using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DigitalBrain;
using DigitalBrain.Runtime.Orleans.Log;
using DigitalBrain.Runtime.Orleans.Synapses;
using DigitalBrain.Runtime.Orleans.Tests.Integration.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Runtime;
using Xunit;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Synapses;

public sealed class SignalLogCompactionFacts
{
    private static BrainStore CreateStoreWithRetention(TempHome home, ManualTime time, FixedCursors cursors, int windowSignals = 1000)
        => new(home.Path, services =>
        {
            services.AddSingleton<TimeProvider>(time);
            services.AddSingleton<IRetentionPolicy>(new RetentionPolicy(BrainStore.Window(windowSignals), time, cursors));
        });

    [Fact]
    public void Compaction_CalculatesSnapshotWatermark_ProtectingSlowestActiveSubscriber()
    {
        var manualTime = new ManualTime { Now = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero) };
        var options = Options.Create(new RetentionOptions { StaleCursorAfter = TimeSpan.FromDays(7) });
        var signalLog = new BrainSignalLog("b1", null!, time: manualTime, retentionOptions: options);

        var subscribers = new List<(string SubscriberId, long Cursor, DateTimeOffset LastActiveAt)>
        {
            ("active-fast", 220, manualTime.Now.AddHours(-1)),
            ("active-slow", 100, manualTime.Now.AddDays(-2)),
            ("active-medium", 160, manualTime.Now.AddHours(-5))
        };

        const long logLatestPosition = 300;
        var compactionFloor = signalLog.ComputeCompactionFloor(logLatestPosition, subscribers, manualTime.Now);

        // Slowest active subscriber cursor is 100; floor must protect it
        Assert.Equal(100, compactionFloor);
    }

    [Fact]
    public async Task Compaction_TruncatesPhysicalSegments_ReclaimingDiskSpace()
    {
        var ct = TestContext.Current.CancellationToken;
        using var home = new TempHome();
        await using var store = new BrainStore(home.Path);

        var log = await store.Log("b1");
        var logDir = store.DirectoryOf("b1");

        // Write entries to establish initial log entries
        var grain = GrainId.Create("neuron.host", "b1/truncation.test/1");
        for (var i = 1; i <= 20; i++)
        {
            await store.Storage.AppendSignalsAsync("state", grain, grain.Key.ToString()!, [new Count(i)]);
        }

        var initialLogFile = Path.Combine(logDir, "initial.log");
        Assert.True(File.Exists(initialLogFile), "initial.log must exist prior to first checkpoint");

        // First Checkpoint: creates Gen1 (.snapshot, .log, and manifest 'current'), and deletes initial.log
        await log.Checkpoint(ct);

        var currentFile = Path.Combine(logDir, "current");
        Assert.True(File.Exists(currentFile), "Manifest 'current' file must exist after first checkpoint");
        var gen1 = File.ReadAllText(currentFile).Trim();

        var gen1LogFile = Path.Combine(logDir, gen1 + ".log");
        var gen1SnapshotFile = Path.Combine(logDir, gen1 + ".snapshot");
        Assert.True(File.Exists(gen1LogFile), "Gen1 .log file must exist");
        Assert.True(File.Exists(gen1SnapshotFile), "Gen1 .snapshot file must exist");
        Assert.False(File.Exists(initialLogFile), "initial.log must be truncated/deleted to reclaim disk space");

        // Write more signals into Gen1 log
        for (var i = 21; i <= 30; i++)
        {
            await store.Storage.AppendSignalsAsync("state", grain, grain.Key.ToString()!, [new Count(i)]);
        }

        // Second Checkpoint: creates Gen2, and deletes Gen1 physical segments (.snapshot and .log)
        await log.Checkpoint(ct);

        var gen2 = File.ReadAllText(currentFile).Trim();
        Assert.NotEqual(gen1, gen2);

        var gen2LogFile = Path.Combine(logDir, gen2 + ".log");
        var gen2SnapshotFile = Path.Combine(logDir, gen2 + ".snapshot");
        Assert.True(File.Exists(gen2LogFile), "Gen2 .log file must exist");
        Assert.True(File.Exists(gen2SnapshotFile), "Gen2 .snapshot file must exist");

        // Verify Gen1 physical segments were truncated / deleted from disk
        Assert.False(File.Exists(gen1LogFile), $"Gen1 log file {gen1LogFile} must be deleted to reclaim disk space");
        Assert.False(File.Exists(gen1SnapshotFile), $"Gen1 snapshot file {gen1SnapshotFile} must be deleted to reclaim disk space");
    }

    [Fact]
    public async Task ReplayFromSnapshot_PreservesMonotonicContinuation_AndIgnoresTruncatedHistory()
    {
        var ct = TestContext.Current.CancellationToken;
        using var home = new TempHome();
        var time = new ManualTime();
        var cursors = new FixedCursors(20);

        // Create store with window of 10 signals and cursors at 20
        await using (var store1 = CreateStoreWithRetention(home, time, cursors, windowSignals: 10))
        {
            var log1 = await store1.Log("b1");
            await store1.Publish(BrainStore.P, 30, batch: 5);
            Assert.Equal(30L, log1.LastPosition);

            // Age the signals past 7 days so retention policy triggers floor raising
            time.Now = time.Now.AddDays(30);

            // Checkpoint trims signals below the floor (floor becomes 20)
            await log1.Checkpoint(ct);

            var slice = await log1.BrainSignals(0, 100, ct);
            Assert.True(slice.Floor >= 20, $"Floor {slice.Floor} must be at least 20 after checkpoint trimming");

            // Append 10 more signals after checkpoint (positions 31..40)
            await store1.Publish(BrainStore.P, 10, batch: 5);
            Assert.Equal(40L, log1.LastPosition);
        }

        // Reopen store from disk, simulating silo restart
        await using (var store2 = CreateStoreWithRetention(home, time, cursors, windowSignals: 10))
        {
            var log2 = await store2.Log("b1");
            Assert.Equal(40L, log2.LastPosition);

            // Verify replay starts from snapshot: signals below the floor are truncated
            var sliceAfterReopen = await log2.BrainSignals(0, 100, ct);
            Assert.True(sliceAfterReopen.Floor >= 20, "Reopened log must preserve floor watermark");
            Assert.Equal(40L, sliceAfterReopen.Last);
            Assert.DoesNotContain(sliceAfterReopen.Held, s => s.Position < sliceAfterReopen.Floor + 1);

            // Continuing writes maintains strict monotonic sequence progression
            await store2.Publish(BrainStore.P, 1);
            Assert.Equal(41L, log2.LastPosition);
            var latestSlice = await log2.BrainSignals(40, 10, ct);
            Assert.Single(latestSlice.Held);
            Assert.Equal(41L, latestSlice.Held[0].Position);
        }
    }

    [Fact]
    public void CompactionFloor_BlockedByLaggingActiveSubscriber_UnblockedUponStalenessQuarantine()
    {
        var manualTime = new ManualTime { Now = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero) };
        var options = Options.Create(new RetentionOptions { StaleCursorAfter = TimeSpan.FromDays(7) });
        var signalLog = new BrainSignalLog("b1", null!, time: manualTime, retentionOptions: options);

        const long logLatestPosition = 500;

        // Stage 1: Lagging subscriber is active (active 2 days ago)
        var subscriberList1 = new List<(string SubscriberId, long Cursor, DateTimeOffset LastActiveAt)>
        {
            ("lagging-subscriber", 50, manualTime.Now.AddDays(-2)),
            ("fast-subscriber", 450, manualTime.Now.AddHours(-1))
        };

        Assert.False(signalLog.IsQuarantined(subscriberList1[0].LastActiveAt, manualTime.Now));
        var blockedFloor = signalLog.ComputeCompactionFloor(logLatestPosition, subscriberList1, manualTime.Now);

        // Compaction floor is blocked at 50 to protect lagging subscriber
        Assert.Equal(50, blockedFloor);

        // Stage 2: Time advances by 6 days. Lagging subscriber has now been inactive for 8 days (> 7 days).
        var advancedTime = manualTime.Now.AddDays(6);

        Assert.True(signalLog.IsQuarantined(subscriberList1[0].LastActiveAt, advancedTime));
        var unblockedFloor = signalLog.ComputeCompactionFloor(logLatestPosition, subscriberList1, advancedTime);

        // Compaction floor is now unblocked and advances to fast subscriber's cursor (450)
        Assert.Equal(450, unblockedFloor);
    }

    [Fact]
    public async Task Compaction_AutoTriggers_WhenEntryThresholdExceeded()
    {
        var ct = TestContext.Current.CancellationToken;
        using var home = new TempHome();

        // Configure with a low entry threshold of 10
        await using var store = new BrainStore(home.Path, services =>
        {
            services.Configure<BrainStoreOptions>(opt =>
            {
                opt.CheckpointEntryThreshold = 10;
                opt.CheckpointBytesThreshold = 10 * 1024 * 1024;
            });
        });

        var log = await store.Log("b1");
        var logDir = store.DirectoryOf("b1");
        var currentFile = Path.Combine(logDir, "current");
        var initialLog = Path.Combine(logDir, "initial.log");

        // Publish 9 entries in batches of 1 (9 append operations)
        await store.Publish(BrainStore.P, 9, batch: 1);
        Assert.False(File.Exists(currentFile), "Checkpoint should not have triggered before reaching entry threshold");
        Assert.True(File.Exists(initialLog), "initial.log must hold entries before threshold is exceeded");

        // 10th entry triggers auto-compaction
        await store.Publish(BrainStore.P, 1, batch: 1);

        Assert.True(File.Exists(currentFile), "Manifest 'current' file must exist after auto-compaction");
        var gen = File.ReadAllText(currentFile).Trim();
        var snapshotFile = Path.Combine(logDir, gen + ".snapshot");
        var logFile = Path.Combine(logDir, gen + ".log");

        Assert.True(File.Exists(snapshotFile), "Generation .snapshot file must exist");
        Assert.True(File.Exists(logFile), "Generation .log file must exist");
        Assert.False(File.Exists(initialLog), "initial.log must be truncated after auto-compaction");

        // Verify all 10 signals are readable through replay
        var slice = await log.BrainSignals(0, 50, ct);
        Assert.Equal(10, slice.Held.Length);
        Assert.Equal(10L, slice.Last);
    }

    [Fact]
    public async Task Compaction_AutoTriggers_WhenBytesThresholdExceeded()
    {
        var ct = TestContext.Current.CancellationToken;
        using var home = new TempHome();

        // Configure a small byte threshold (e.g. 500 bytes) and high entry threshold
        await using var store = new BrainStore(home.Path, services =>
        {
            services.Configure<BrainStoreOptions>(opt =>
            {
                opt.CheckpointEntryThreshold = 1000;
                opt.CheckpointBytesThreshold = 500;
            });
        });

        var log = await store.Log("b1");
        var logDir = store.DirectoryOf("b1");
        var currentFile = Path.Combine(logDir, "current");

        Assert.False(File.Exists(currentFile), "Manifest 'current' file must not exist before writes");

        // Write entries until the byte threshold is breached
        await store.Publish(BrainStore.P, 10, batch: 1);

        Assert.True(File.Exists(currentFile), "Manifest 'current' file must exist after byte threshold exceeded");
        var gen = File.ReadAllText(currentFile).Trim();
        Assert.True(File.Exists(Path.Combine(logDir, gen + ".snapshot")), "Snapshot file must exist");
        Assert.True(File.Exists(Path.Combine(logDir, gen + ".log")), "Generation log file must exist");
    }
}
