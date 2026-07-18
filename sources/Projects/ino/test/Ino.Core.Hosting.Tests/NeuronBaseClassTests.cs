using Ino.Core.Hosting.Tests.Fixtures;
using Ino.Testing;
using Xunit;

namespace Ino.Core.Hosting.Tests;


[Collection(nameof(InoTestCollection))]
public sealed class NeuronBaseClassTests
{
    private readonly InoTestSiloFixture _fixture;

    public NeuronBaseClassTests(InoTestSiloFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RaiseAsync_AppendsToJournal_VisibleViaGetAllEventsAsync()
    {
        var neuron = _fixture.Grains.GetGrain<ITestNeuron>("test-neuron-history");
        var correlationId = Guid.NewGuid().ToString("n");

        await neuron.ApplyEventAsync(new TestEvent("first", 1), correlationId);
        await neuron.ApplyEventAsync(new TestEvent("second", 2), correlationId);

        var history = await neuron.GetAllEventsAsync();

        Assert.Equal(2, history.Count);
        Assert.Equal("first", history[0].Text);
        Assert.Equal(1, history[0].Delta);
        Assert.Equal("second", history[1].Text);
        Assert.Equal(2, history[1].Delta);
    }

    [Fact]
    public async Task OnDemandProjections_ReflectAllRaisedEvents()
    {
        var neuron = _fixture.Grains.GetGrain<ITestNeuron>("test-neuron-state");
        var correlationId = Guid.NewGuid().ToString("n");

        await neuron.ApplyEventAsync(new TestEvent("first", 10), correlationId);
        await neuron.ApplyEventAsync(new TestEvent("second", 5), correlationId);
        await neuron.ApplyEventAsync(new TestEvent("third", -3), correlationId);

        Assert.Equal(3, await neuron.GetEventCountAsync());
        Assert.Equal(12, await neuron.GetTotalDeltaAsync());
        Assert.Equal("third", await neuron.GetLastTextAsync());
    }

    [Fact]
    public async Task RaiseAsync_PersistsEvents_VisibleOnSubsequentReference()
    {
        // Force a fresh grain key so prior tests don't pollute the run.
        var grainKey = $"test-neuron-persist-{Guid.NewGuid():n}";
        var correlationId = Guid.NewGuid().ToString("n");

        var first = _fixture.Grains.GetGrain<ITestNeuron>(grainKey);
        await first.ApplyEventAsync(new TestEvent("persisted", 42), correlationId);

        // True reactivation requires DeactivateOnIdleAsync (Phase 2 surface). For now,
        // a fresh grain reference with the same key proves the projection works against
        // confirmed events — Orleans may reuse the activation, which is fine for Phase 1.
        var second = _fixture.Grains.GetGrain<ITestNeuron>(grainKey);
        var history = await second.GetAllEventsAsync();
        var count = await second.GetEventCountAsync();
        var totalDelta = await second.GetTotalDeltaAsync();
        var lastText = await second.GetLastTextAsync();

        Assert.Equal(1, count);
        Assert.Equal(42, totalDelta);
        Assert.Equal("persisted", lastText);
        Assert.Single(history);
        Assert.Equal("persisted", history[0].Text);
    }

    [Fact]
    public async Task ZeroEvents_ProjectionsReturnDefaults_HistoryEmpty()
    {
        var neuron = _fixture.Grains.GetGrain<ITestNeuron>("test-neuron-empty");

        Assert.Equal(0, await neuron.GetEventCountAsync());
        Assert.Equal(0, await neuron.GetTotalDeltaAsync());
        Assert.Null(await neuron.GetLastTextAsync());
        Assert.Empty(await neuron.GetAllEventsAsync());
    }

    [Fact]
    public async Task RaiseAsync_propagates_causation_fields_from_context()
    {
        // I4: verify Neuron<T>.RaiseAsync copies CausedByEventId, CausedByStream,
        // CorrelationId, and TraceParent from the supplied NeuronContext into the
        // stored EventEnvelope. The grain builds the context server-side (context
        // carries non-serializable ILogger/Activity) and returns the activity id so
        // the test can assert TraceParent bytes-for-bytes.
        var grain = _fixture.Grains.GetGrain<ITestNeuron>($"causation-{Guid.NewGuid():n}");

        const string parentEventId = "parent-evt-42";
        const string parentStream = "stream/parent";
        const string correlationId = "corr-777";
        // W3C traceparent: 2-byte version + 16-byte trace-id + 8-byte span-id + 2-byte flags.
        const string parentTraceParent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";

        var expectedActivityId = await grain.RaiseViaContextAsync(
            new TestEvent("payload", 1),
            currentEventId: parentEventId,
            correlationId: correlationId,
            sourceStream: parentStream,
            traceParent: parentTraceParent);

        var envelopes = await grain.GetAllEnvelopesAsync();
        var env = Assert.Single(envelopes);
        Assert.Equal(parentEventId, env.CausedByEventId);
        Assert.Equal(parentStream, env.CausedByStream);
        Assert.Equal(correlationId, env.CorrelationId);
        Assert.Equal(expectedActivityId, env.TraceParent);
        Assert.False(string.IsNullOrEmpty(expectedActivityId));
    }

    [Fact]
    public async Task FindEventAsync_returns_null_for_unknown_id()
    {
        // I5: FindEventAsync scans the journal and must return null when no entry matches.
        var grain = _fixture.Grains.GetGrain<ITestNeuron>($"find-miss-{Guid.NewGuid():n}");
        await grain.ApplyEventAsync(new TestEvent("x", 1), Guid.NewGuid().ToString("n"));

        var info = await grain.FindEventAsync("no-such-event-id");

        Assert.Null(info);
    }

    [Fact]
    public async Task FindEventAsync_returns_null_for_null_or_empty_id()
    {
        // I5: the null/empty guard at the top of FindEventAsync short-circuits before
        // the scan. Both an empty string and a null reference are explicit rejects.
        var grain = _fixture.Grains.GetGrain<ITestNeuron>($"find-empty-{Guid.NewGuid():n}");
        await grain.ApplyEventAsync(new TestEvent("x", 1), Guid.NewGuid().ToString("n"));

        Assert.Null(await grain.FindEventAsync(string.Empty));
        Assert.Null(await grain.FindEventAsync(null!));
    }

    [Fact]
    public async Task FindEventAsync_returns_envelope_info_for_match()
    {
        // I5: the hit branch returns a JournaledEventInfo whose EventId matches, whose
        // PayloadTypeName contains the payload record name, and whose PayloadJson round-trips
        // the payload fields. Two events are raised to confirm the scan picks the right one.
        var grain = _fixture.Grains.GetGrain<ITestNeuron>($"find-hit-{Guid.NewGuid():n}");
        var correlationId = Guid.NewGuid().ToString("n");
        await grain.ApplyEventAsync(new TestEvent("first", 1), correlationId);
        await grain.ApplyEventAsync(new TestEvent("second", 2), correlationId);

        var envelopes = await grain.GetAllEnvelopesAsync();
        var targetId = envelopes[0].EventId;

        var info = await grain.FindEventAsync(targetId);

        Assert.NotNull(info);
        Assert.Equal(targetId, info!.EventId);
        Assert.Contains("TestEvent", info.PayloadTypeName);
        Assert.Contains("first", info.PayloadJson);
    }
}
