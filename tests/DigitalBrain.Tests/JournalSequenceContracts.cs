using System.Reflection;
using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using Orleans.Serialization;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class JournalSequenceContracts
{
    [Fact]
    public void JournalCursorSequenceIsIndependentFromDeliveryOriginSequence()
    {
        var caller = new NeuronId("Emitter", new OwnerId("acme"), "first");
        var delivery = SynapseDelivery.Create(new JournalProbe(), caller, sequence: 1);
        var entry = new JournalEntry(2, delivery);

        Assert.Equal(2, entry.Sequence);
        Assert.Equal(1, entry.Delivery.Sequence);

        var serializer = typeof(NeuronFeed).GetField(
            "_entries",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(serializer);
        Assert.Equal(typeof(Serializer<JournalEntry>), serializer.FieldType);
    }

    private sealed record JournalProbe : Synapse;
}
