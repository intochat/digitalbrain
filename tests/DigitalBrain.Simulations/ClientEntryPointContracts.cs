using System.Reflection;
using DigitalBrain.Abstractions;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Simulations;

public sealed class ClientEntryPointContracts
{
    [Fact(DisplayName = "an unattributed caller cannot reach a neuron interface that is not a client entry point")]
    public async Task AnInterfaceWithoutTheMarkerIsClosedToUnattributedCallers()
    {
        await SimulationCluster.StartAsync();

        var probe = SimulationCluster.Grains.GetGrain<IEchoProbe>(
            new NeuronId(nameof(Echo), new OwnerId("entry-points"), "probe").ToGrainId());

        var refusal = await Assert.ThrowsAsync<NeuronAuthorizationException>(probe.PokeAsync);

        Assert.Contains("is not a client entry point", refusal.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "the session is the only client entry point the framework ships")]
    public void OnlyDeliberateContractsAreClientEntryPoints()
    {
        var shipped = typeof(INeuron).Assembly.GetExportedTypes()
            .Where(type => type.IsInterface)
            .Where(type => type.GetCustomAttribute<ClientEntryPointAttribute>() is not null)
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal([nameof(ISessionNeuron)], shipped);
    }
}
