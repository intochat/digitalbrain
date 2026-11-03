using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans.Runtime;
using Orleans.Streams;
using Orleans.Storage;
using DigitalBrain.Runtime.Orleans.Testing;

namespace DigitalBrain.Runtime.Orleans.Tests.Unit.Hosting;

public sealed class RuntimeOptionsBehaviorFacts : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "db-rob-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { if (Directory.Exists(_home)) { Directory.Delete(_home, recursive: true); } }

    [Fact]
    public void Parse_reads_the_topology_from_the_key_the_options_name()
    {
        var settings = TopologySettings.Parse(Config.Of(("DigitalBrain:Topology", "cloud")), [], RuntimeOptions.Default);

        Assert.Equal(Topology.Cloud, settings.Topology);
        Assert.Contains(settings.Issues, issue => issue.Key == "DigitalBrain:Cloud:Storage");
    }

    [Fact]
    public void Parse_ignores_the_IntoChat_spelling_under_default_options()
    {
        var settings = TopologySettings.Parse(Config.Of(("IntoChatOS:Topology", "cloud"), ("IntoChatOS:Home", "relative")), [], RuntimeOptions.Default);

        Assert.Equal(Topology.Local, settings.Topology);
        Assert.True(settings.IsValid);
        Assert.EndsWith(".digitalbrain", settings.Home!.Path, StringComparison.Ordinal);
    }

    [Fact]
    public void UseTopology_resolves_the_home_from_the_options_home_key()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection([new("DigitalBrain:Home", _home), new("DigitalBrain:Topology", "local"), new("IntoChatOS:Home", "relative")]);
        builder.UseRuntimeOptions(RuntimeOptions.Default);
        builder.UseClusterStore(new BrainStorageRegistration());

        builder.UseTopology([], []);

        using var host = builder.Build();
        Assert.Equal(Path.GetFullPath(_home), host.Services.GetRequiredService<TopologySettings>().Home!.Path);
        Assert.Same(RuntimeOptions.Default, host.Services.GetRequiredService<RuntimeOptions>());
    }

    [Fact]
    public void LocalSiloHostFactory_takes_home_streams_and_stores_from_the_options()
    {
        var options = RuntimeOptions.Default with { SignalStreams = "sig-x", PlatformGrainStorage = "plat-x", ClusterGrainStorage = "clu-x" };
        var factory = new LocalSiloHostFactory(null, null, options);

        using var host = factory.Build(new InProcessHostOptions { Modules = [] }, _home);

        var services = host.Services;
        Assert.Equal(Path.GetFullPath(_home), services.GetRequiredService<TopologySettings>().Home!.Path);
        Assert.NotNull(services.GetKeyedService<IStreamProvider>("sig-x"));
        Assert.Null(services.GetKeyedService<IStreamProvider>("signals"));
        Assert.NotNull(services.GetKeyedService<IGrainStorage>("plat-x"));
        Assert.Null(services.GetKeyedService<IGrainStorage>("platform"));
        Assert.IsType<UnregisteredClusterStorage>(services.GetKeyedService<IGrainStorage>("clu-x"));
        Assert.Null(services.GetKeyedService<IGrainStorage>("cluster"));
        Assert.Same(options, services.GetRequiredService<RuntimeOptions>());
    }

    [Fact]
    public void Profile_services_name_the_options_environment_keys_and_images()
    {
        var os = Assert.Single(new LocalProfile(RuntimeOptions.Default).Services.Processes, p => p.Name == "os");

        Assert.Contains("DigitalBrain__Topology", os.EnvironmentKeys);
        Assert.Contains("DigitalBrain__Home", os.EnvironmentKeys);
        Assert.Equal(RuntimeOptions.Default.OsImage, os.Image);
        Assert.Equal("Open", new LocalProfile(RuntimeOptions.Default).Defaults["DigitalBrain:Auth:Posture"]);
    }

    [Fact]
    public void UseTopology_refuses_a_profile_built_with_other_options()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection([new("DigitalBrain:Home", _home)]);
        builder.UseRuntimeOptions(RuntimeOptions.Default);

        var other = new RuntimeOptions { TopologyKey = "IntoChatOS:Topology", DefaultHomeDirectoryName = ".intochat" };
        var thrown = Assert.Throws<InvalidOperationException>(() => builder.UseTopology([], [], new LocalProfile(other)));

        Assert.Contains("IntoChatOS:Topology", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("DigitalBrain:Topology", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UseTopology_accepts_a_profile_built_with_the_registered_options()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection([new("DigitalBrain:Home", _home)]);
        builder.UseRuntimeOptions(RuntimeOptions.Default);
        builder.UseClusterStore(new BrainStorageRegistration());

        Assert.IsType<LocalProfile>(builder.UseTopology([], [], new LocalProfile(RuntimeOptions.Default)));
    }
}
