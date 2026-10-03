using Orleans;

namespace DigitalBrain.Kernel;

[GrainType("brain-document")]
public sealed class DocumentGrain([PersistentState("document", "Default")] IPersistentState<DocumentState> state) : Grain, IDocumentGrain
{
    public Task<(long Version, string? Payload)> ReadAsync()
        => Task.FromResult((state.State.Version, state.State.Written ? state.State.Payload : null));

    public async Task<bool> TryWriteAsync(long expectedVersion, string payload)
    {
        var current = state.State.Written ? state.State.Version : 0;
        if (current != expectedVersion) { return false; }
        var previous = state.State;
        state.State = new DocumentState { Written = true, Version = current + 1, Payload = payload };
        try
        {
            await state.WriteStateAsync();
        }
        catch
        {
            state.State = previous;
            throw;
        }
        return true;
    }
}
