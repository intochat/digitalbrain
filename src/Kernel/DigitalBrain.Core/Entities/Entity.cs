using DigitalBrain.Abstractions.Entities;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;

namespace DigitalBrain.Core;

public abstract class Entity<TState> : DurableGrain, IEntity<TState>, IOwnerBoundGrain
    where TState : class
{
    private const string StateName = "entity.state";

    private readonly IDurableValue<byte[]> _state;
    private readonly Serializer<TState> _serializer;
    private TState? _snapshot;

    protected Entity()
    {
        _state = ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>(StateName);
        _serializer = ServiceProvider.GetRequiredService<Serializer<TState>>();
    }

    protected TState? State
        => _snapshot ??= _state.Value is { Length: > 0 } bytes ? _serializer.Deserialize(bytes) : null;

    public Task<TState?> Read() => Task.FromResult(State);

    protected async Task SaveAsync(TState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        _state.Value = _serializer.SerializeToArray(state);
        _snapshot = state;
        await WriteStateAsync(cancellationToken).ConfigureAwait(true);
    }
}
