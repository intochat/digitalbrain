using DigitalBrain.Core;
using DigitalBrain.Testing.Unit;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;

namespace DigitalBrain.Tests;

public sealed class ModuleRegistrationFacts
{
    [Fact]
    public void DistinctModulesCannotDisagreeOnSharedSettings()
    {
        var first = new ModuleDefinition(typeof(Dependency), new Dictionary<string, string?> { ["Shared:Value"] = "first" });
        var second = new ModuleDefinition(typeof(Dependent), new Dictionary<string, string?> { ["Shared:Value"] = "second" });
        Assert.Throws<InvalidOperationException>(() => ModuleComposition.Resolve([first, second]));
    }

    [Fact]
    public async Task TransitiveDependencyRegistersExactlyOnce()
    {
        await using var brain = await UnitTest.Create().WithModule<Dependency>().WithModule<Dependent>()
            .ConfigureSilo(silo => Assert.Single(silo.Services, s => s.ServiceType == typeof(Marker)))
            .StartAsync(TestContext.Current.CancellationToken);
    }

    public sealed class Marker;
    public sealed class Dependency : IModule
    {
        public void Configure(ISiloBuilder silo) => silo.Services.AddSingleton<Marker>();
    }
    [ModuleConfiguration(typeof(DependentContract))]
    public sealed class Dependent : IModule
    {
        public void Configure(ISiloBuilder silo)
        {
            if (!silo.Services.Any(s => s.ServiceType == typeof(Marker)))
                { throw new InvalidOperationException("Dependency was not configured before dependent."); }
        }
    }
    public sealed class DependentOptions;
    public sealed class DependentContract() : ModuleConfigurationContract<Dependent, DependentOptions>()
    {
        protected override ModuleDefinition Compile(DependentOptions options)
            => new(typeof(Dependent), dependencies: [new(typeof(Dependency))]);
    }
}
