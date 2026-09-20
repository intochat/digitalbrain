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
        var dependency = new ModuleDefinition(typeof(Dependency));
        var module = new ModuleDefinition(typeof(Dependent), dependencies: [dependency]);
        await using var brain = await UnitTest.StartAsync(new()
        {
            Modules = [module, module],
            ConfigureSilo = silo => Assert.Single(silo.Services, s => s.ServiceType == typeof(Marker)),
        }, TestContext.Current.CancellationToken);
    }

    public sealed class Marker;
    public sealed class Dependency : IModule
    {
        public void Configure(ISiloBuilder silo) => silo.Services.AddSingleton<Marker>();
    }
    public sealed class Dependent : IModule
    {
        public void Configure(ISiloBuilder silo)
        {
            if (!silo.Services.Any(s => s.ServiceType == typeof(Marker)))
                { throw new InvalidOperationException("Dependency was not configured before dependent."); }
        }
    }
}
