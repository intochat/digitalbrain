using Xunit;

namespace Ino.Core.Tests;

public sealed class EventEnvelopeTests
{
    private sealed record DummyEvent(string Text) : ISynapse;

    [Fact]
    public void RootEvent_HasNoCausedByPointer()
    {
        var payload = new DummyEvent("first");
        var envelope = new EventEnvelope<DummyEvent>(
            Payload: payload,
            EventId: "evt-001",
            CausedByEventId: null,
            CausedByStream: null,
            CorrelationId: "corr-001",
            Timestamp: DateTimeOffset.UtcNow,
            TraceParent: null);

        Assert.Null(envelope.CausedByEventId);
        Assert.Null(envelope.CausedByStream);
        Assert.Equal(payload, envelope.Payload);
    }

    [Fact]
    public void CausedEvent_CarriesParentPointers()
    {
        var envelope = new EventEnvelope<DummyEvent>(
            Payload: new DummyEvent("child"),
            EventId: "evt-002",
            CausedByEventId: "evt-001",
            CausedByStream: "parent-stream",
            CorrelationId: "corr-001",
            Timestamp: DateTimeOffset.UtcNow,
            TraceParent: "00-trace-span-01");

        Assert.Equal("evt-001", envelope.CausedByEventId);
        Assert.Equal("parent-stream", envelope.CausedByStream);
        Assert.Equal("00-trace-span-01", envelope.TraceParent);
    }

    [Fact]
    public void TwoEnvelopesWithSameFields_AreEqualByValue()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var a = new EventEnvelope<DummyEvent>(
            new DummyEvent("same"), "e1", null, null, "c1", timestamp, null);
        var b = new EventEnvelope<DummyEvent>(
            new DummyEvent("same"), "e1", null, null, "c1", timestamp, null);

        Assert.Equal(b, a);
    }
}
