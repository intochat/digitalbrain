using DigitalBrain.Kernel.Contracts;

namespace DigitalBrain.Kernel.Capabilities;

[GrainType("digitalbrain.owner-connection-catalog")]
internal sealed class OwnerConnectionCatalogGrain(IOwnerConnectionCatalog catalog) : Grain, IOwnerConnectionCatalogGrain
{
    public Task<OwnerConnectionSnapshot[]> ReadAsync()
    {
        var ownerId = new BrainOwnerId(this.GetPrimaryKeyString());
        return catalog.ReadAsync(ownerId);
    }
}
