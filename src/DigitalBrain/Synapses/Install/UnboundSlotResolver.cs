namespace DigitalBrain.Units;

/// <summary>An <see cref="ISlotResolver"/> that binds nothing: every slot is unbound, so gates refuse and reactions fail closed.</summary>
public sealed class UnboundSlotResolver : ISlotResolver
{
    /// <inheritdoc />
    public Task<object> Resolve(NeuronId state, SlotDescriptor slot, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(slot);
        throw new SlotUnboundException(state.Type, slot.Name);
    }
}
