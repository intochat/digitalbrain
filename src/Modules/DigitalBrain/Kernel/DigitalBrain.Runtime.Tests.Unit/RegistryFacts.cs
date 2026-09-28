using DigitalBrain.Core;
using DigitalBrain.Core.Registry;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class RegistryFacts
{
    [Fact]
    public async Task StartupDiscoversPublicNeuronContractsByAlias()
    {
        await using var brain = await UnitTest.Create().WithModule<FixtureModule>()
            .StartAsync(TestContext.Current.CancellationToken);

        var registry = brain.SiloServices.GetRequiredService<NeuronRegistry>();
        var emitter = registry.Find("test.emitter");
        Assert.Equal(typeof(ITestEmitter), emitter?.Interface);
        Assert.Equal(typeof(FixtureModule).FullName, emitter?.ModuleId);
        Assert.Null(registry.Find("missing"));
    }

    public sealed class FixtureModule : IModule
    {
        public void Configure(ISiloBuilder silo) { }
    }
}
