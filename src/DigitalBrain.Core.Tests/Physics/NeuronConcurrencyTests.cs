using System.Diagnostics.CodeAnalysis;
using Orleans.Concurrency;

namespace DigitalBrain.Core.Tests.Physics;

public sealed class NeuronConcurrencyTests
{
    [Fact(DisplayName =
        "A neuron type annotated Reentrant or MayInterleave fails RequireSerializedTurns with a message naming serialized turns")]
    public void ReentrantAndMayInterleaveFailSerializedTurnsContract()
    {
        // Touch construction so CA1812 does not treat the carriers as dead; activation is never used.
        _ = new ReentrantProbe();
        _ = new MayInterleaveProbe();

        var reentrant = Assert.Throws<InvalidOperationException>(
            () => NeuronConcurrency.RequireSerializedTurns(typeof(ReentrantProbe)));
        Assert.Contains(nameof(ReentrantAttribute), reentrant.Message, StringComparison.Ordinal);
        Assert.Contains("serialized turns", reentrant.Message, StringComparison.OrdinalIgnoreCase);

        var mayInterleave = Assert.Throws<InvalidOperationException>(
            () => NeuronConcurrency.RequireSerializedTurns(typeof(MayInterleaveProbe)));
        Assert.Contains(nameof(MayInterleaveAttribute), mayInterleave.Message, StringComparison.Ordinal);
        Assert.Contains("serialized turns", mayInterleave.Message, StringComparison.OrdinalIgnoreCase);
    }
}

// Attribute carriers only — not Neuron grains (public Neuron simple names collide in the Orleans manifest).
[Reentrant]
[SuppressMessage("Performance", "CA1812", Justification = "Type is the attribute carrier for RequireSerializedTurns.")]
file sealed class ReentrantProbe;

[MayInterleave(nameof(AllowAll))]
[SuppressMessage("Performance", "CA1812", Justification = "Type is the attribute carrier for RequireSerializedTurns.")]
file sealed class MayInterleaveProbe
{
    public static bool AllowAll(IIncomingGrainCallContext _) => true;
}
