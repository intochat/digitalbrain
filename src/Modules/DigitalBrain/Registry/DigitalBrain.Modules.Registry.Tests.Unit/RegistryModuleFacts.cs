using DigitalBrain.AI.Agents;
using DigitalBrain.Core;
using DigitalBrain.Registry;
using DigitalBrain.Qdrant;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Modules.Registry.Tests.Unit;

public sealed class RegistryModuleFacts
{
    [Fact]
    public async Task KernelRunsWithoutRegistryServices()
    {
        await using var brain = await UnitTest.Create().WithModule<FixtureNeuronModule>()
            .StartAsync(TestContext.Current.CancellationToken);

        Assert.Null(brain.SiloServices.GetService<NeuronRegistry>());
        Assert.Null(brain.SiloServices.GetService<NeuronInvoker>());
        Assert.Equal([typeof(FixtureNeuronModule)], brain.SiloServices.GetRequiredService<ModuleInventory>().Types);
    }

    [Fact]
    public async Task SelectedModuleOffersCallableNeuronToolsWithoutTheAIImplementation()
    {
        await using var brain = await UnitTest.Create().WithModule<QdrantModule>().WithModule<RegistryModule>()
            .WithModule<FixtureNeuronModule>().StartAsync(TestContext.Current.CancellationToken);

        var tools = brain.SiloServices.GetServices<IAgentToolFactory>()
            .SelectMany(factory => factory.Create(() => new("scope", "run", "call")));

        Assert.Contains(tools, tool => tool.Name == "test_registry_emitter_EmitRegistrySignal");
        Assert.Contains(tools, tool => tool.Name == "find_capability");
    }

    [Fact]
    public async Task LoadedButUnselectedModuleContractsAreNotDiscovered()
    {
        await using var brain = await UnitTest.Create().WithModule<QdrantModule>().WithModule<RegistryModule>()
            .StartAsync(TestContext.Current.CancellationToken);

        var registry = brain.SiloServices.GetRequiredService<NeuronRegistry>();
        Assert.Equal(typeof(ICapabilityCatalog), registry.Find("capability-catalog")?.Interface);
        Assert.Null(registry.Find("test.registry-emitter"));
        Assert.DoesNotContain(registry.All, contract => contract.Interface == typeof(IRegistryEmitter));
    }

    [Fact]
    public async Task RegisteredNeuronToolInvokesTheSelectedNeuron()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<QdrantModule>().WithModule<RegistryModule>()
            .WithModule<NeuronInvokerFacts.TallyModule>().StartAsync(ct);
        var tools = brain.SiloServices.GetServices<IAgentToolFactory>()
            .SelectMany(factory => factory.Create(() => new("scope", "run", "call")));
        var add = Assert.Single(tools, tool => tool.Name == "test_tally_Add");

        var result = await add.InvokeAsync(new AIFunctionArguments
        {
            ["neuronId"] = "tool-tally",
            ["step"] = new TallyStep(7),
        }, ct);

        Assert.Null(Assert.IsType<NeuronCallResult>(result).Error);
        Assert.Equal(7, (await brain.Get<ITally>("tool-tally").Read()).Total);
    }
}