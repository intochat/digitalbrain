using System;
using System.Collections.Concurrent;
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

public sealed class ChallengerSignalLogStressFacts
{
    private sealed record TestSubscriberState : NeuronState
    {
        public List<int> ReceivedCounts { get; init; } = [];
    }

    [Fact]
    public async Task HighConcurrency_BurstMonotonicity_WithMixedEmptyAndMultiSignalBatches()
    {
        var ct = TestContext.Current.CancellationToken;
        using var home = new TempHome();
        await using var store = new BrainStore(home.Path);

        var signalLog = new BrainSignalLog("b1", store.Logs);
        const int WriterCount = 30;
        const int IterationsPerWriter = 15;

        var allReturnedPositions = new ConcurrentBag<long>();
        var totalSignalsWritten = 0;

        var tasks = Enumerable.Range(1, WriterCount).Select(writerIndex => Task.Run(async () =>
        {
            var pub = new NeuronId(new BrainId("b1"), "stress.pub", writerIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
            for (var iter = 0; iter < IterationsPerWriter; iter++)
            {
                var batchSize = (writerIndex + iter) switch
                {
                    var n when n % 5 == 0 => 0, // Empty batch
                    var n when n % 5 == 1 => 1, // Single
                    var n when n % 5 == 2 => 2,
                    var n when n % 5 == 3 => 3,
                    _ => 4
                };

                if (batchSize == 0)
                {
                    var pos = await signalLog.AppendSignalsAsync(pub, [], ct);
                    // Appending empty batch should return current last position without incrementing
                    Assert.True(pos >= 0);
                }
                else
                {
                    var batch = Enumerable.Range(1, batchSize).Select(s => (Signal)new Note($"w{writerIndex}-i{iter}-s{s}")).ToArray();
                    var pos = await signalLog.AppendSignalsAsync(pub, batch, ct);
                    allReturnedPositions.Add(pos);
                    Interlocked.Add(ref totalSignalsWritten, batchSize);
                }
            }
        }, ct)).ToArray();

        await Task.WhenAll(tasks);

        var finalPos = await signalLog.GetLatestPositionAsync(ct);
        Assert.Equal((long)totalSignalsWritten, finalPos);

        var log = await signalLog.GetUnderlyingLogAsync(ct);
        Assert.Equal((long)totalSignalsWritten, log.LastPosition);

        // Read all signals back using prime page sizes to test arbitrary slice boundaries
        var retrievedPositions = new List<long>();
        long cursor = 0;
        const int PageLimit = 7; // prime page limit

        while (cursor < totalSignalsWritten)
        {
            var slice = await log.BrainSignals(cursor, PageLimit, ct);
            if (slice.Held.Length == 0) break;
            foreach (var s in slice.Held)
            {
                retrievedPositions.Add(s.Position);
                cursor = s.Position;
            }
        }

        Assert.Equal(totalSignalsWritten, retrievedPositions.Count);
        // Contiguous 1..totalSignalsWritten
        var expected = Enumerable.Range(1, totalSignalsWritten).Select(n => (long)n).ToList();
        Assert.Equal(expected, retrievedPositions);
    }

    [Fact]
    public async Task PaginationBounds_And_SliceIntegrity_BoundaryConditions()
    {
        var ct = TestContext.Current.CancellationToken;
        using var home = new TempHome();
        await using var store = new BrainStore(home.Path);

        var signalLog = new BrainSignalLog("b1", store.Logs);
        var pub = new NeuronId(new BrainId("b1"), "page.pub", "1");

        // Write exactly 123 signals
        const int SignalCount = 123;
        var batch = Enumerable.Range(1, SignalCount).Select(i => (Signal)new Count(i)).ToArray();
        await signalLog.AppendSignalsAsync(pub, batch, ct);

        var log = await signalLog.GetUnderlyingLogAsync(ct);

        // 1. Boundary: Negative since throws ArgumentOutOfRangeException
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => log.BrainSignals(-1, 10, ct));

        // 2. Boundary: Limit <= 0 throws ArgumentOutOfRangeException
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => log.BrainSignals(0, 0, ct));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => log.BrainSignals(0, -5, ct));

        // 3. Various page limits (1, 2, 5, 23, 50, 100, 200)
        int[] testLimits = [1, 2, 5, 23, 50, 100, 200];
        foreach (var limit in testLimits)
        {
            var readPositions = new List<long>();
            long cursor = 0;
            while (cursor < SignalCount)
            {
                var slice = await log.BrainSignals(cursor, limit, ct);
                Assert.True(slice.Held.Length <= limit);
                if (slice.Held.Length == 0) break;
                foreach (var s in slice.Held)
                {
                    readPositions.Add(s.Position);
                    cursor = s.Position;
                }
            }

            Assert.Equal(SignalCount, readPositions.Count);
            Assert.Equal(Enumerable.Range(1, SignalCount).Select(i => (long)i).ToList(), readPositions);
        }

        // 4. Query at or past end
        var endSlice = await log.BrainSignals(123, 50, ct);
        Assert.Empty(endSlice.Held);
        Assert.Equal(123L, endSlice.Last);

        var pastEndSlice = await log.BrainSignals(999, 50, ct);
        Assert.Empty(pastEndSlice.Held);
        Assert.Equal(123L, pastEndSlice.Last);
    }

    [Fact]
    public async Task Compaction_PhysicalSegmentDeletion_AcrossMultipleGenerations_LeavesNoOrphanFiles()
    {
        var ct = TestContext.Current.CancellationToken;
        using var home = new TempHome();
        await using var store = new BrainStore(home.Path);

        var log = await store.Log("b1");
        var logDir = store.DirectoryOf("b1");
        var grain = GrainId.Create("neuron.host", "b1/multi.gen.test/1");

        // Write entries before Checkpoint 1
        for (var i = 1; i <= 10; i++)
        {
            await store.Storage.AppendSignalsAsync("state", grain, grain.Key.ToString()!, [new Count(i)]);
        }

        var initialLogFile = Path.Combine(logDir, "initial.log");
        Assert.True(File.Exists(initialLogFile));

        // Checkpoint 1: creates Gen 1, deletes initial.log
        await log.Checkpoint(ct);
        var currentFile = Path.Combine(logDir, "current");
        var gen1 = File.ReadAllText(currentFile).Trim();
        Assert.False(File.Exists(initialLogFile));
        Assert.True(File.Exists(Path.Combine(logDir, gen1 + ".log")));
        Assert.True(File.Exists(Path.Combine(logDir, gen1 + ".snapshot")));

        // Write entries before Checkpoint 2
        for (var i = 11; i <= 20; i++)
        {
            await store.Storage.AppendSignalsAsync("state", grain, grain.Key.ToString()!, [new Count(i)]);
        }

        // Checkpoint 2: creates Gen 2, deletes Gen 1 files
        await log.Checkpoint(ct);
        var gen2 = File.ReadAllText(currentFile).Trim();
        Assert.NotEqual(gen1, gen2);
        Assert.False(File.Exists(Path.Combine(logDir, gen1 + ".log")));
        Assert.False(File.Exists(Path.Combine(logDir, gen1 + ".snapshot")));
        Assert.True(File.Exists(Path.Combine(logDir, gen2 + ".log")));
        Assert.True(File.Exists(Path.Combine(logDir, gen2 + ".snapshot")));

        // Write entries before Checkpoint 3
        for (var i = 21; i <= 30; i++)
        {
            await store.Storage.AppendSignalsAsync("state", grain, grain.Key.ToString()!, [new Count(i)]);
        }

        // Checkpoint 3: creates Gen 3, deletes Gen 2 files
        await log.Checkpoint(ct);
        var gen3 = File.ReadAllText(currentFile).Trim();
        Assert.NotEqual(gen2, gen3);
        Assert.False(File.Exists(Path.Combine(logDir, gen2 + ".log")));
        Assert.False(File.Exists(Path.Combine(logDir, gen2 + ".snapshot")));
        Assert.True(File.Exists(Path.Combine(logDir, gen3 + ".log")));
        Assert.True(File.Exists(Path.Combine(logDir, gen3 + ".snapshot")));

        // Enumerate directory: MUST contain ONLY current, gen3.log, gen3.snapshot among log/snapshot/manifest files
        var segmentFiles = Directory.GetFiles(logDir)
            .Select(f => Path.GetFileName(f) ?? "")
            .Where(f => f.EndsWith(".log", StringComparison.Ordinal) || f.EndsWith(".snapshot", StringComparison.Ordinal) || f == "current")
            .ToHashSet(StringComparer.Ordinal);
        var expectedFiles = new HashSet<string>(StringComparer.Ordinal) { "current", gen3 + ".log", gen3 + ".snapshot" };
        Assert.Equal(expectedFiles, segmentFiles);
    }

    [Fact]
    public void FloorCalculation_LaggingSubscribers_QuarantinePrecision_AndEmptySubscribers()
    {
        var manualTime = new ManualTime { Now = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero) };
        var options = Options.Create(new RetentionOptions { StaleCursorAfter = TimeSpan.FromDays(7) });
        var signalLog = new BrainSignalLog("b1", null!, time: manualTime, retentionOptions: options);
        const long head = 1000;

        // 1. Empty subscriber list: floor = head
        Assert.Equal(head, signalLog.ComputeCompactionFloor(head, [], manualTime.Now));

        // 2. Active lagging subscriber at 150 protects floor
        var subActive = ("sub-active", 150L, manualTime.Now.AddDays(-6).AddHours(-23));
        Assert.False(signalLog.IsQuarantined(subActive.Item3, manualTime.Now));
        Assert.Equal(150L, signalLog.ComputeCompactionFloor(head, [subActive], manualTime.Now));

        // 3. Stale subscriber at 50 (7 days + 1 second ago) is quarantined; active subscriber at 200 sets floor
        var subStale = ("sub-stale", 50L, manualTime.Now.AddDays(-7).AddSeconds(-1));
        var subActive2 = ("sub-active2", 200L, manualTime.Now.AddHours(-1));
        Assert.True(signalLog.IsQuarantined(subStale.Item3, manualTime.Now));
        Assert.Equal(200L, signalLog.ComputeCompactionFloor(head, [subStale, subActive2], manualTime.Now));

        // 4. All subscribers quarantined: floor unblocks and advances to head
        var subStale2 = ("sub-stale2", 80L, manualTime.Now.AddDays(-10));
        Assert.Equal(head, signalLog.ComputeCompactionFloor(head, [subStale, subStale2], manualTime.Now));
    }

    [Fact]
    public async Task MultiSubscriber_IndependentCatchUp_UnderConcurrency()
    {
        var ct = TestContext.Current.CancellationToken;
        using var home = new TempHome();
        await using var store = new BrainStore(home.Path);

        var signalLog = new BrainSignalLog("b1", store.Logs);
        var pub = new NeuronId(new BrainId("b1"), "pub", "1");

        // Write 60 signals
        var signals = Enumerable.Range(1, 60).Select(i => (Signal)new Count(i)).ToArray();
        await signalLog.AppendSignalsAsync(pub, signals, ct);

        // 4 subscribers concurrently catching up to different positions
        var tasks = Enumerable.Range(1, 4).Select(subIndex => Task.Run(async () =>
        {
            var target = subIndex * 15; // 15, 30, 45, 60
            var subId = new NeuronId(new BrainId("b1"), "sub", subIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var state = new TestSubscriberState { SignalCursor = 0 };

            var caughtUp = await signalLog.EnsureCaughtUpAsync(
                subId,
                state,
                (s, sig, _) => Task.FromResult(s with { ReceivedCounts = [.. s.ReceivedCounts, ((Count)sig).N] }),
                (s, _) => Task.CompletedTask,
                targetPosition: target,
                ct: ct);

            Assert.Equal(target, caughtUp.SignalCursor);
            Assert.Equal(target, caughtUp.ReceivedCounts.Count);
            Assert.Equal(Enumerable.Range(1, target).ToList(), caughtUp.ReceivedCounts);
        }, ct)).ToArray();

        await Task.WhenAll(tasks);
    }

    [Fact]
    public async Task LaggedSubscriber_WakesUpBelowCompactionFloor_EnsureCaughtUpAsync_AdvancesCleanly()
    {
        var ct = TestContext.Current.CancellationToken;
        using var home = new TempHome();
        var time = new ManualTime();
        var cursors = new FixedCursors(20);

        await using var store = new BrainStore(home.Path, services =>
        {
            services.AddSingleton<TimeProvider>(time);
            services.AddSingleton<IRetentionPolicy>(new RetentionPolicy(BrainStore.Window(10), time, cursors));
        });

        // Write 30 signals
        await store.Publish(BrainStore.P, 30, batch: 5);

        // Compact log below floor 20
        time.Now = time.Now.AddDays(30);
        await (await store.Log()).Checkpoint(ct);

        var log = await store.Log();
        var slice = await log.BrainSignals(5, 50, ct);
        Assert.True(slice.Floor >= 20);

        // Lagged subscriber waking up with cursor at 5, target at 30
        var signalLog = new BrainSignalLog("b1", store.Logs);
        var subId = new NeuronId(new BrainId("b1"), "lagged.sub", "1");
        var state = new TestSubscriberState { SignalCursor = 5 };

        var caughtUp = await signalLog.EnsureCaughtUpAsync(
            subId,
            state,
            (s, sig, _) => Task.FromResult(s with { ReceivedCounts = [.. s.ReceivedCounts, ((Count)sig).N] }),
            (s, _) => Task.CompletedTask,
            targetPosition: 30,
            ct: ct);

        // The subscriber successfully catches up to 30
        Assert.Equal(30L, caughtUp.SignalCursor);
        // It processed the available held signals post-floor (positions 21..30)
        Assert.NotEmpty(caughtUp.ReceivedCounts);
        Assert.All(caughtUp.ReceivedCounts, count => Assert.True(count > 20));
    }
}
