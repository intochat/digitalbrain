using Orleans;

namespace DigitalBrain.Kernel;

[GrainType("brain-document-index")]
internal sealed class DocumentIndexGrain([PersistentState("index", "Default")] IPersistentState<DocumentIndexState> state) : Grain, IDocumentIndexGrain
{
    public Task<string[]> ListAsync() => Task.FromResult(state.State.Ids.ToArray());

    public async Task AddAsync(string id)
    {
        if (state.State.Ids.Contains(id)) { return; }
        var previous = state.State;
        state.State = new DocumentIndexState { Ids = new(previous.Ids) { id } };
        try { await state.WriteStateAsync(); }
        catch { state.State = previous; throw; }
    }
}
