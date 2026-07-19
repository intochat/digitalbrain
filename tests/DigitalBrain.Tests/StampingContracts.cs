using DigitalBrain;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class StampingContracts
{
    private static readonly NeuronId Caller = new("Greeter", new OwnerId("acme"), "polite");
    private static readonly NeuronId Receiver = new("Echo", new OwnerId("acme"), "first");

    [Fact]
    public void SendWithoutCauseStartsANewConversation()
    {
        var metadata = SynapseMetadata.ForSend(Caller, Receiver);

        Assert.Equal(Caller, metadata.Caller);
        Assert.Equal(Receiver, metadata.Receiver);
        Assert.Equal(RoutingMode.PointToPoint, metadata.RoutingMode);
        Assert.Null(metadata.CausationId);
        Assert.NotEqual(default, metadata.CorrelationId);
        Assert.NotEqual(default, metadata.SynapseId);
    }

    [Fact]
    public void SendWithCauseInheritsTheConversationAndPointsAtItsParent()
    {
        var cause = SynapseMetadata.ForSend(Receiver, Caller);

        var metadata = SynapseMetadata.ForSend(Caller, Receiver, cause);

        Assert.Equal(cause.CorrelationId, metadata.CorrelationId);
        Assert.Equal(cause.SynapseId, metadata.CausationId);
        Assert.NotEqual(cause.SynapseId, metadata.SynapseId);
    }

    [Fact]
    public void BroadcastHasNoReceiver()
    {
        var metadata = SynapseMetadata.ForBroadcast(Caller);

        Assert.Null(metadata.Receiver);
        Assert.Equal(RoutingMode.Broadcast, metadata.RoutingMode);
    }

    [Fact]
    public void ReplyAddressesTheCallerOfTheSynapseItAnswers()
    {
        var cause = SynapseMetadata.ForSend(Caller, Receiver);

        var metadata = SynapseMetadata.ForReply(Receiver, cause);

        Assert.Equal(Caller, metadata.Receiver);
        Assert.Equal(Receiver, metadata.Caller);
        Assert.Equal(cause.CorrelationId, metadata.CorrelationId);
        Assert.Equal(cause.SynapseId, metadata.CausationId);
    }

    [Fact]
    public void CorrelationSurvivesEveryHopWhileCausationTracksTheParent()
    {
        var first = SynapseMetadata.ForSend(Caller, Receiver);
        var second = SynapseMetadata.ForReply(Receiver, first);
        var third = SynapseMetadata.ForReply(Caller, second);

        Assert.Equal(first.CorrelationId, third.CorrelationId);
        Assert.Equal(second.SynapseId, third.CausationId);
    }

    [Fact]
    public void TimestampComesFromTheSuppliedTimeProvider()
    {
        var time = new FixedTime(DateTimeOffset.Parse("2026-07-19T10:30:00Z", System.Globalization.CultureInfo.InvariantCulture));

        var metadata = SynapseMetadata.ForSend(Caller, Receiver, cause: null, timeProvider: time);

        Assert.Equal(time.GetUtcNow(), metadata.Timestamp);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
