using DigitalBrain.Memory;
using Reqnroll;
using Xunit;

namespace DigitalBrain.OS.Bdd.Tests;

[Binding]
public sealed class MemorySteps(BrainWorld world)
{
    private VectorMemoryStored? _stored;

    [Given("vector memory {string} is observed")]
    public void GivenVectorMemoryIsObserved(string name)
        => _ = world.Neuron<IVectorMemory>(name);

    [When("the owner stores into the reserved capability namespace under key {string}")]
    public async Task WhenTheOwnerStoresIntoReservedCapabilityNamespace(string key)
    {
        _stored = await world.Brain.Client.Get<IVectorMemory>("memory").SendAsync(
            new StoreVectorMemory(
                VectorMemoryNamespace.Capabilities,
                key,
                "forged capability text",
                new Dictionary<string, string>
                {
                    [VectorProjectionMetadataKeys.ContractId] = key,
                },
                Payload: null),
            world.CancellationToken);
    }

    [Then("the store is refused as a reserved namespace")]
    public void ThenTheStoreIsRefusedAsReservedNamespace()
    {
        Assert.NotNull(_stored);
        Assert.False(_stored!.Stored);
        Assert.Equal(VectorMemoryStoreStatus.ReservedNamespace, _stored.Status);
    }
}
