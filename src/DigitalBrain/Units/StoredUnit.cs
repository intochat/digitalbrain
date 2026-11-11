namespace DigitalBrain.Units;

/// <summary>
/// What a unit neuron stores: its declared state record and its current published view.
/// </summary>
internal sealed record StoredUnit<TState>(TState? State, PublishedView? View) where TState : class
{
    public StoredUnit() : this(null, null) { }
}
