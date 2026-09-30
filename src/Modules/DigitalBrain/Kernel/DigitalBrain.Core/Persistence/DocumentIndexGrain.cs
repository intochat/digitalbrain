using Orleans;

namespace DigitalBrain.Core;

[GrainType("brain-document-index")]
public sealed class DocumentIndexGrain([PersistentState("index", "Default")] IPersistentState<DocumentIndexState> state) : Grain, IDocumentIndexGrain
{
    public Task<string[]> ListAsync() => Task.FromResult(state.State.Ids.ToArray());

    public async Task AddAsync(string id)
    {
        if (state.State.Ids.Add(id)) { await state.WriteStateAsync(); }
    }
}
