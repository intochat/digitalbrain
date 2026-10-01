using DigitalBrain.Contracts;
using Orleans.Runtime;

namespace DigitalBrain.Core;

[GrainType("brain")]
internal sealed class BrainNeuron : Neuron<BrainState>, IBrain
{
    private readonly IPersistentState<BrainState> _store;

    public BrainNeuron(
        [PersistentState("brain", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<BrainState> store)
        : base(store)
        => _store = store;

    public Task<BrainSnapshot> Read() => Task.FromResult(ToSnapshot());

    public async Task<BrainSnapshot> Establish(EstablishBrain request)
    {
        if (Snapshot.EstablishedAt == default)
        {
            Snapshot.Name = request.Name;
            Snapshot.OwnerAccountId = request.OwnerAccountId;
            Snapshot.EstablishedAt = DateTimeOffset.UtcNow;
            await _store.WriteStateAsync();
        }
        return ToSnapshot();
    }

    private BrainSnapshot ToSnapshot() => new()
    {
        BrainId = this.GetPrimaryKeyString(),
        Name = Snapshot.Name,
        OwnerAccountId = Snapshot.OwnerAccountId,
        EstablishedAt = Snapshot.EstablishedAt,
    };
}
