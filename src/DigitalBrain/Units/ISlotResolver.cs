namespace DigitalBrain.Units;

/// <summary>Resolves the capability behind a <c>[Slot]</c> parameter for one state neuron.</summary>
public interface ISlotResolver
{
    /// <summary>Resolves <paramref name="slot"/> for the state neuron <paramref name="state"/>.</summary>
    /// <param name="state">The state neuron running the synapse.</param>
    /// <param name="slot">The slot to resolve.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>An object implementing <see cref="SlotDescriptor.Contract"/>.</returns>
    /// <exception cref="SlotUnboundException">The slot has no binding, or the bound connection is not usable.</exception>
    Task<object> Resolve(NeuronId state, SlotDescriptor slot, CancellationToken ct);
}
