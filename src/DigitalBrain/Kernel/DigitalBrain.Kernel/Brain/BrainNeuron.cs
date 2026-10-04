using DigitalBrain;
using DigitalBrain.Contracts;
using Orleans.Runtime;

namespace DigitalBrain.Kernel;

[GrainType("brain")]
internal sealed class BrainNeuron : Neuron<BrainState>, IBrain
{
    private readonly IPersistentState<BrainState> _store;

    public BrainNeuron(
        [PersistentState("brain", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<BrainState> store)
        : base(store)
        => _store = store;

    public Task<BrainSnapshot> Read() => Task.FromResult(ToSnapshot());

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        if (Snapshot.EstablishedAt != default)
        {
            await PublishAsync(new Activated(this.GetPrimaryKeyString(), Snapshot.OwnerAccountId, Snapshot.Name));
        }
    }

    public async Task<BrainSnapshot> Establish(EstablishBrain request)
    {
        if (Snapshot.EstablishedAt == default)
        {
            Snapshot.Name = request.Name;
            Snapshot.OwnerAccountId = request.OwnerAccountId;
            Snapshot.EstablishedAt = DateTimeOffset.UtcNow;
            await _store.WriteStateAsync();
            await PublishAsync(new Activated(this.GetPrimaryKeyString(), Snapshot.OwnerAccountId, Snapshot.Name));
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
