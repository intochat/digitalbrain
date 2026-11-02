using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DigitalBrain;
using DigitalBrain.Runtime.Orleans.Log;
using DigitalBrain.Runtime.Orleans.Synapses;
using DigitalBrain.Runtime.Orleans.Tests.Integration.Storage;
using Xunit;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Synapses;

public sealed class SignalLogPersistenceFacts
{
    [Fact]
    public async Task Append_SingleAndBatchSignals_IncrementsUnifiedBrainPositionMonotonically()
    {
        var ct = TestContext.Current.CancellationToken;
        using var home = new TempHome();
        await using var store = new BrainStore(home.Path);

        var signalLog = new BrainSignalLog("b1", store.Logs);
        var pub1 = new NeuronId(new BrainId("b1"), "test.pub", "1");
        var pub2 = new NeuronId(new BrainId("b1"), "test.pub", "2");

        var initialPos = await signalLog.GetLatestPositionAsync(ct);
        Assert.Equal(0L, initialPos);

        // Append a single signal
        var pos1 = await signalLog.AppendSignalAsync(pub1, new Note("sig-1"), ct);
        Assert.Equal(1L, pos1);

        // Append a batch of 3 signals
        var batch1 = new Signal[] { new Note("sig-2"), new Note("sig-3"), new Note("sig-4") };
        var pos2 = await signalLog.AppendSignalsAsync(pub2, batch1, ct);
        Assert.Equal(4L, pos2);

        // Append another single signal
        var pos3 = await signalLog.AppendSignalAsync(pub1, new Note("sig-5"), ct);
        Assert.Equal(5L, pos3);

        // Append another batch of 2 signals
        var batch2 = new Signal[] { new Note("sig-6"), new Note("sig-7") };
        var pos4 = await signalLog.AppendSignalsAsync(pub2, batch2, ct);
        Assert.Equal(7L, pos4);

        var finalPos = await signalLog.GetLatestPositionAsync(ct);
        Assert.Equal(7L, finalPos);
    }

    [Fact]
    public async Task Query_Pagination_RespectsLimitAndYieldsContiguousPagesAcrossSegments()
    {
        var ct = TestContext.Current.CancellationToken;
        using var home = new TempHome();
        await using var store = new BrainStore(home.Path);

        var signalLog = new BrainSignalLog("b1", store.Logs);
        var pub = new NeuronId(new BrainId("b1"), "test.pub", "p1");

        // Write 250 signals in batches of 50
        for (var b = 0; b < 5; b++)
        {
            var batch = Enumerable.Range(b * 50 + 1, 50).Select(n => (Signal)new Note($"note-{n}")).ToArray();
            await signalLog.AppendSignalsAsync(pub, batch, ct);
        }

        var log = await signalLog.GetUnderlyingLogAsync(ct);
        Assert.Equal(250L, log.LastPosition);

        // Query pages of 50
        var retrievedPositions = new List<long>();
        long cursor = 0;
        const int PageLimit = 50;

        while (cursor < 250)
        {
            var slice = await log.BrainSignals(cursor, PageLimit, ct);
            Assert.True(slice.Held.Length <= PageLimit, $"Slice length {slice.Held.Length} exceeded limit {PageLimit}");
            if (slice.Held.Length == 0)
            {
                break;
            }

            foreach (var s in slice.Held)
            {
                retrievedPositions.Add(s.Position);
                cursor = s.Position;
            }
        }

        Assert.Equal(250, retrievedPositions.Count);
        var expected = Enumerable.Range(1, 250).Select(n => (long)n).ToList();
        Assert.Equal(expected, retrievedPositions);
    }

    [Fact]
    public async Task Query_SincePastEnd_ReturnsEmptyHeldSliceWithoutThrowing()
    {
        var ct = TestContext.Current.CancellationToken;
        using var home = new TempHome();
        await using var store = new BrainStore(home.Path);

        var signalLog = new BrainSignalLog("b1", store.Logs);
        var pub = new NeuronId(new BrainId("b1"), "test.pub", "1");

        // Append 10 signals
        var signals = Enumerable.Range(1, 10).Select(n => (Signal)new Note($"note-{n}")).ToArray();
        var lastPos = await signalLog.AppendSignalsAsync(pub, signals, ct);
        Assert.Equal(10L, lastPos);

        var log = await signalLog.GetUnderlyingLogAsync(ct);

        // Query at exactly the end
        var sliceAtEnd = await log.BrainSignals(10, 50, ct);
        Assert.Empty(sliceAtEnd.Held);
        Assert.Equal(10L, sliceAtEnd.Last);

        // Query well past the end
        var slicePastEnd = await log.BrainSignals(100, 50, ct);
        Assert.Empty(slicePastEnd.Held);
        Assert.Equal(10L, slicePastEnd.Last);
    }

    [Fact]
    public async Task MonotonicSequenceMonotonicity_MultiGrainConcurrentBurst()
    {
        var ct = TestContext.Current.CancellationToken;
        using var home = new TempHome();
        await using var store = new BrainStore(home.Path);

        var signalLog = new BrainSignalLog("b1", store.Logs);
        const int GrainCount = 20;
        const int WritesPerGrain = 10;
        const int ExpectedTotal = GrainCount * WritesPerGrain;

        var returnedPositions = new ConcurrentBag<long>();
        var tasks = new List<Task>();

        for (var g = 0; g < GrainCount; g++)
        {
            var grainIndex = g + 1;
            var pub = new NeuronId(new BrainId("b1"), "test.grain", grainIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
            tasks.Add(Task.Run(async () =>
            {
                for (var w = 0; w < WritesPerGrain; w++)
                {
                    var pos = await signalLog.AppendSignalAsync(pub, new Note($"burst-{grainIndex}-{w}"), ct);
                    returnedPositions.Add(pos);
                }
            }, ct));
        }

        await Task.WhenAll(tasks);

        Assert.Equal(ExpectedTotal, returnedPositions.Count);
        var ordered = returnedPositions.OrderBy(p => p).ToList();
        var expected = Enumerable.Range(1, ExpectedTotal).Select(n => (long)n).ToList();
        Assert.Equal(expected, ordered);

        var finalPos = await signalLog.GetLatestPositionAsync(ct);
        Assert.Equal((long)ExpectedTotal, finalPos);
    }
}
