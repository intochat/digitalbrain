using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.Identity;

[GenerateSerializer, Alias("brain.state")]
internal sealed record BrainState
{
    [Id(0)] public string Name { get; set; } = "";
    [Id(1)] public string OwnerAccountId { get; set; } = "";
    [Id(2)] public DateTimeOffset EstablishedAt { get; set; }
}

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
