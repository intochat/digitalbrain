using Aspire.Hosting;
using DigitalBrain.Aspire.Hosting;
using Orleans.Hosting;

namespace DigitalBrain.Core.Tests.Unit;

public sealed class ModuleOptionOverlayFacts
{
    public sealed class OverlaidModule : IModule<OverlaidOptions> { public void Configure(ISiloBuilder silo) { } }
    public sealed class OverlaidOptions : IModuleOptions
    {
        public string Endpoint { get; set; } = "default";
        public int Delay { get; set; } = 7;
        public Dictionary<string, string> Bindings { get; set; } = [];
        public void Validate() { }
    }

    [Fact]
    public void The_app_hosts_own_configuration_overrides_a_code_declared_module_option()
    {
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions
        {
            Args =
            [
                "DigitalBrain:Modules:OverlaidModule:Options:Endpoint=from-args",
                "DigitalBrain:Modules:OverlaidModule:Options:Bindings:extra=added",
            ],
            DisableDashboard = true,
        });
        var brain = builder.AddDigitalBrain("modules", persistentStorage: false)
            .AddModules([ModuleOptionsSerialization.Compile<OverlaidModule, OverlaidOptions>(new() { Endpoint = "declared", Delay = 42 })]);

        var options = brain.GetModuleConfiguration<OverlaidModule>().GetModuleOptions<OverlaidOptions>(nameof(OverlaidModule));
        Assert.Equal("from-args", options.Endpoint);
        Assert.Equal(42, options.Delay);
        Assert.Equal("added", options.Bindings["extra"]);
    }
}
