using DigitalBrain.Core;
using DigitalBrain.Registry;
using DigitalBrain.Qdrant;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Xunit;

namespace DigitalBrain.Modules.Registry.Tests.Unit;

public sealed class RegistryFacts
{
    [Fact]
    public async Task StartupDiscoversPublicNeuronContractsByAlias()
    {
        await using var brain = await UnitTest.Create().WithModule<QdrantModule>().WithModule<RegistryModule>().WithModule<FixtureModule>()
            .StartAsync(TestContext.Current.CancellationToken);

        var registry = brain.SiloServices.GetRequiredService<NeuronRegistry>();
        var emitter = registry.Find("test.registry-emitter");
        Assert.Equal(typeof(IRegistryEmitter), emitter?.Interface);
        Assert.Equal(typeof(FixtureModule).FullName, emitter?.ModuleId);
        Assert.Null(registry.Find("missing"));
    }

    public sealed class FixtureModule : IModule
    {
        public void Configure(ISiloBuilder silo) { }
    }
}