using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.ModuleTests;

public sealed class CapabilityCatalogFallback
{
    [Fact(DisplayName = "when vector memory is unavailable exact lookup still resolves explicit module neuron and synapse terms")]
    public async Task ExactLookupWorksWhenVectorSearchFails()
    {
        var catalog = ActiveCapabilityCatalog.Create([GreeterModule()]);
        var router = new CapabilityRouter(catalog, new FailingCandidateSearch());

        var byModule = await router.SelectAsync(
            new OwnerId("owner-a"),
            "use digitalbrain.testing.greeter",
            CancellationToken.None);
        Assert.Contains(byModule, item => item.ContractId == "harness.say-hello");

        var byNeuron = await router.SelectAsync(
            new OwnerId("owner-a"),
            "talk to harness.greeter",
            CancellationToken.None);
        Assert.Contains(byNeuron, item => item.ContractId == "harness.say-hello");

        var bySynapse = await router.SelectAsync(
            new OwnerId("owner-a"),
            "send harness.say-hello",
            CancellationToken.None);
        var capability = Assert.Single(bySynapse);
        Assert.Equal("harness.say-hello", capability.ContractId);
        Assert.Equal(1, capability.SchemaVersion);
        Assert.Equal("harness.greeter", capability.NeuronContractId);
    }

    [Fact(DisplayName = "empty vector results fall back to exact term resolution without inventing capabilities")]
    public async Task EmptyVectorResultsDoNotInventCapabilities()
    {
        var catalog = ActiveCapabilityCatalog.Create([GreeterModule()]);
        var router = new CapabilityRouter(catalog, new EmptyCandidateSearch());

        var invented = await router.SelectAsync(
            new OwnerId("owner-a"),
            "do something unrelated to any catalog entry",
            CancellationToken.None);
        Assert.Empty(invented);

        var explicitTerm = await router.SelectAsync(
            new OwnerId("owner-a"),
            "please say hello using harness.say-hello",
            CancellationToken.None);
        Assert.Single(explicitTerm);
    }

    private static ScriptedModule GreeterModule()
    {
        var sayHello = new SynapseCapabilityDescriptor(
            "harness.say-hello",
            schemaVersion: 1,
            "Ask the greeter to say hello",
            """{"type":"object","properties":{"name":{"type":"string"}}}""",
            ["say hello to Alice"]);
        var greeted = new SynapseCapabilityDescriptor(
            "harness.greeted",
            schemaVersion: 1,
            "Greeter responded",
            """{"type":"object","properties":{"message":{"type":"string"}}}""",
            []);
        var neuron = new NeuronCapabilityDescriptor(
            "harness.greeter",
            "Greeter neuron",
            "default",
            [sayHello],
            [greeted]);
        return new ScriptedModule(
            new ModuleId("digitalbrain.testing.greeter"),
            new CapabilityManifest(
                new ModuleId("digitalbrain.testing.greeter"),
                "1.0.0",
                "Testing greeter module",
                [],
                [neuron]));
    }

    private sealed class ScriptedModule(ModuleId id, CapabilityManifest capabilities) : ICompiledModule
    {
        public ModuleId Id { get; } = id;

        public CapabilityManifest Capabilities { get; } = capabilities;

        public void PrepareSerialization(IServiceCollection services)
        {
        }

        public void Activate(ISiloBuilder builder)
        {
        }
    }

    private sealed class FailingCandidateSearch : ICapabilityCandidateSearch
    {
        public Task<IReadOnlyList<CapabilityCandidate>> SearchAsync(
            OwnerId owner,
            string prompt,
            int limit,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException("vector memory unavailable");
    }

    private sealed class EmptyCandidateSearch : ICapabilityCandidateSearch
    {
        public Task<IReadOnlyList<CapabilityCandidate>> SearchAsync(
            OwnerId owner,
            string prompt,
            int limit,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<CapabilityCandidate>>([]);
    }
}
