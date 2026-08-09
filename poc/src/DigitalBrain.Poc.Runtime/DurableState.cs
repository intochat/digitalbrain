using DigitalBrain.Poc.Abstractions;

namespace DigitalBrain.Poc.Runtime;

public sealed class DurableState<TState>(TState value) : IDurableState<TState>
{
    public TState Value { get; private set; } = value;

    public void Replace(TState next)
    {
        ArgumentNullException.ThrowIfNull(next);
        Value = next;
    }
}
