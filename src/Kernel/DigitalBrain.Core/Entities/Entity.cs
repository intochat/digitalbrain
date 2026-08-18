using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Entities;
using Orleans.Runtime;

namespace DigitalBrain.Core;

public abstract class Entity<TState>(
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<TState> state)
    : Grain, IEntity<TState>, IOwnerBoundGrain
    where TState : class
{
    protected TState? State => state.RecordExists ? state.State : null;

    public Task<TState?> Read() => Task.FromResult(State);

    protected async Task SaveAsync(TState value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        state.State = value;
        await state.WriteStateAsync(cancellationToken).ConfigureAwait(true);
    }
}
