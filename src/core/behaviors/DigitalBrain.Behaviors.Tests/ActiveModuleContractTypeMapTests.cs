using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using Xunit;

namespace DigitalBrain.Behaviors.Tests;

public sealed class ActiveModuleContractTypeMapTests
{
    [Fact(DisplayName = "type map prefers implementation [GrainType] and matches DispatchHarness grain identity")]
    public void PrefersImplementationGrainTypeForHarnessNeuron()
    {
        var module = new BehaviorDispatchHarnessModule();
        var catalog = ActiveCapabilityCatalog.Create([(ICompiledModule)module]);
        var map = ActiveModuleContractTypeMap.Create([(ICompiledModule)module], catalog);

        Assert.True(map.TryGetNeuronGrainType(DispatchHarness.NeuronContractId, out var grainType));
        Assert.Equal(DispatchHarness.GrainTypeName, grainType, ignoreCase: true);
        Assert.True(map.TryGetSynapseType(DispatchHarness.RequestContractId, 1, out var requestType));
        Assert.Equal(typeof(DispatchProbeRequest), requestType);
        Assert.True(map.TryGetSynapseType(DispatchHarness.ResponseContractId, 1, out var responseType));
        Assert.Equal(typeof(DispatchProbeResponse), responseType);
    }
}
