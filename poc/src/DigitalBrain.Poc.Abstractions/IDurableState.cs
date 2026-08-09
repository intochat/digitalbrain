namespace DigitalBrain.Poc.Abstractions;

public interface IDurableState<TState>
{
    TState Value { get; }

    void Replace(TState next);
}
