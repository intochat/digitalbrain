using Aspire.Hosting;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Contracts;
using Orleans.Hosting;

namespace DigitalBrain.Aspire.Hosting.Tests.Unit;

public sealed class ModuleOptionOverlayFacts
{
    public sealed class OverlaidOptions : IModuleOptions
    {
        public string Endpoint { get; set; } = "default";
        public int Delay { get; set; } = 7;
        public Dictionary<string, string> Bindings { get; set; } = [];
        public void Validate() { }
    }

    [Fact]
    public void ReplacementOptionsRemoveCodeDeclaredCollectionsAndRestoreTypeDefaults()
    {
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions { Args = [], DisableDashboard = true });
        builder.Configuration["DigitalBrain:Modules:example:ReplaceOptions"] = "true";
        builder.Configuration["DigitalBrain:Modules:example:Options:Endpoint"] = "fixture";
        var brain = builder.AddDigitalBrain("modules", persistentStorage: false)
            .WithModule("example", settings: HostingModuleOptions.Flatten(new OverlaidOptions
            {
                Endpoint = "production",
                Delay = 42,
                Bindings = new() { ["live"] = "service" }
            }, "example"));
        var options = HostingModuleOptions.GetModuleOptions<OverlaidOptions>(brain.GetModuleConfiguration("example"), "example");
        Assert.Equal("fixture", options.Endpoint);
        Assert.Equal(7, options.Delay);
        Assert.Empty(options.Bindings);
    }

    [Fact]
    public void TwoBrainsScopeInfrastructureAndAllowIndependentConfiguration()
    {
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions { Args = [], DisableDashboard = true });
        builder.Configuration["DigitalBrain:Modules:postgres:Options:Hosting:Enabled"] = "true";
        builder.Configuration["DigitalBrain:Brains:first:Modules:postgres:Options:ConnectionName"] = "first-db";
        builder.Configuration["DigitalBrain:Brains:second:Modules:postgres:Options:ConnectionName"] = "second-db";
        var first = builder.AddDigitalBrain("first", persistentStorage: false, options: new() { UseAzureStorage = true })
            .WithModule<DigitalBrain.Postgres.Aspire.Hosting.PostgresModuleHosting>();
        var second = builder.AddDigitalBrain("second", persistentStorage: false, options: new() { UseAzureStorage = true })
            .WithModule<DigitalBrain.Postgres.Aspire.Hosting.PostgresModuleHosting>();
        Assert.Equal("first-db", first.GetModuleConfiguration("postgres")["DigitalBrain:Modules:postgres:Options:ConnectionName"]);
        Assert.Equal("second-db", second.GetModuleConfiguration("postgres")["DigitalBrain:Modules:postgres:Options:ConnectionName"]);
        var names = builder.Resources.Select(r => r.Name).ToArray();
        Assert.Equal(names.Length, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains("first-postgres-server", names);
        Assert.Contains("second-postgres-server", names);
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
        var brain = builder.AddDigitalBrain("modules", persistentStorage: false, options: new() { UseAzureStorage = true })
            .WithModule("OverlaidModule", settings: HostingModuleOptions.Flatten(new OverlaidOptions { Endpoint = "declared", Delay = 42 }, "OverlaidModule"));

        var options = HostingModuleOptions.GetModuleOptions<OverlaidOptions>(brain.GetModuleConfiguration("OverlaidModule"), "OverlaidModule");
        Assert.Equal("from-args", options.Endpoint);
        Assert.Equal(42, options.Delay);
        Assert.Equal("added", options.Bindings["extra"]);
    }
}
