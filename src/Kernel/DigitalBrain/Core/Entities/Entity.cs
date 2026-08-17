namespace DigitalBrain.Core.Neurons;

public abstract class Entity<T>(IPersistentState<T> state)
    : Grain, IEntity<T>, IGrainWithStringKey
{
    public async Task SaveAsync(T data, CancellationToken cancellationToken)
    {
        state.State = data;
        await state.WriteStateAsync(cancellationToken);
    }

    public Task<T> ReadAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(state.State);
    }
}
