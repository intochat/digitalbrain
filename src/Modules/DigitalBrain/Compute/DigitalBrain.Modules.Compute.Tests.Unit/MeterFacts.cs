using DigitalBrain.Compute;
using DigitalBrain.Compute.Metering;
using DigitalBrain.Compute.Reconciliation;
using DigitalBrain.Compute.Storage;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class MeterFacts
{
    [Fact]
    public async Task SameKeyTwiceRecordsOneEvent()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new InMemoryMeterStore();
        var sink = new DurableMeterSink(store);
        var meterEvent = Event();

        await sink.RecordAsync(meterEvent, ct);
        await sink.RecordAsync(meterEvent with { Quantity = 999 }, ct);

        var stored = await store.ReadAsync(ct);
        var single = Assert.Single(stored);
        Assert.Equal(10m, single.Quantity);
    }

    [Fact]
    public async Task BatchAppendWritesOneEventPerKeyAndIgnoresDuplicates()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new InMemoryMeterStore();
        var sink = new DurableMeterSink(store);
        var input = Event();
        var output = input with { MeterId = "llm.gpt.output_tokens", Step = "output_tokens" };

        var inserted = await sink.RecordBatchAsync([input, output, input], ct);

        Assert.Equal(2, inserted);
        Assert.Equal(2, (await store.ReadAsync(ct)).Count);
    }

    [Fact]
    public async Task ConcurrentDuplicateAppendsRecordOneEvent()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new InMemoryMeterStore();

        var results = await Task.WhenAll(Enumerable.Range(0, 64)
            .Select(_ => store.AppendAsync(Event(), ct).AsTask()));

        Assert.Single(results, inserted => inserted);
        Assert.Single(await store.ReadAsync(ct));
    }

    [Fact]
    public async Task StorageSamplerKeepsTheDailyPeakAndOneEventPerDay()
    {
        var ct = TestContext.Current.CancellationToken;
        var probe = new FakeProbe(1L * 1024 * 1024 * 1024);
        var store = new InMemoryMeterStore();
        var sampler = new StorageSampler(probe, store);
        var day1 = new DateTimeOffset(2026, 9, 1, 6, 0, 0, TimeSpan.Zero);

        var first = await sampler.SampleAsync("workspace-a", day1, ct);
        probe.Bytes = 512L * 1024 * 1024;
        var lower = await sampler.SampleAsync("workspace-a", day1.AddHours(6), ct);
        probe.Bytes = 3L * 1024 * 1024 * 1024;
        var second = await sampler.SampleAsync("workspace-a", day1.AddDays(1), ct);

        Assert.NotNull(first);
        Assert.Null(lower);
        Assert.NotNull(second);
        Assert.Equal(2m, sampler.AverageDailyPeakGigabytes);
        Assert.Equal(2, (await store.ReadAsync(ct)).Count);
        Assert.All(await store.ReadAsync(ct), stored => Assert.Equal(StorageSampler.MeterId, stored.MeterId));
        Assert.Equal(MeterSource.StorageSampler, second.Source);
    }

    [Fact]
    public void ReconciliationFlagsOnlyDeltasBeyondOnePercent()
    {
        Assert.True(MeterReconciliation.Compare(100.5m, 100m).WithinTolerance);
        Assert.False(MeterReconciliation.Compare(102m, 100m).WithinTolerance);
        Assert.True(MeterReconciliation.Compare(0m, 0m).WithinTolerance);
        Assert.Equal(0.5m, MeterReconciliation.Compare(100.5m, 100m).DeltaPercent);
    }

    private static MeterEvent Event() => new()
    {
        IntentId = "intent-1",
        MeterId = "llm.gpt.input_tokens",
        Step = "input_tokens",
        WorkspaceId = "workspace-a",
        Quantity = 10,
        Unit = "tokens",
        Source = MeterSource.ChatClient,
        OccurredAt = DateTimeOffset.UnixEpoch,
    };

    private sealed class FakeProbe(long bytes) : IStorageUsageProbe
    {
        public long Bytes { get; set; } = bytes;

        public ValueTask<long> ReadBytesAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Bytes);
    }
}
