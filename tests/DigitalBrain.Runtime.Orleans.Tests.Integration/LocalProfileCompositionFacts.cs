using DigitalBrain.Runtime.Orleans.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans;
using Orleans.Runtime;
using Orleans.Storage;

using OrleansSdk = Orleans;
namespace DigitalBrain.Runtime.Orleans.Tests.Integration;

public sealed class LocalProfileCompositionFacts
{
    private static InProcessHostOptions Options(string home, params Type[] modules) => new() { Home = home, Modules = modules };

    [Fact]
    public void With_the_host_registration_the_cluster_store_is_the_host_store_and_named_stores_are_files()
    {
        using var home = new TempHome();
        var fake = new FakeClusterStore();
        using var host = new LocalSiloHostFactory(fake).Build(Options(home.Path, typeof(DriveModule)), home.Path);

        Assert.Same(fake, host.Services.GetRequiredKeyedService<IGrainStorage>(IntoChatOSNames.ClusterGrainStorage));
        Assert.Equal(1, fake.Registered);
        foreach (var name in new[] { IntoChatOSNames.PlatformGrainStorage, IntoChatOSNames.PubSubStore, "drive" })
        {
            var store = Assert.IsType<FileGrainStorage>(host.Services.GetRequiredKeyedService<IGrainStorage>(name));
            Assert.Equal(Path.Combine(home.Path, "state", "files", name), store.RootDirectory);
        }
        Assert.IsType<FileReminderTable>(host.Services.GetRequiredService<IReminderTable>());
        Assert.NotNull(host.Services.GetRequiredKeyedService<OrleansSdk.Streams.IStreamProvider>(IntoChatOSNames.SignalStreams));
        Assert.False(string.IsNullOrEmpty(host.Services.GetRequiredService<IConfiguration>()[IntoChatOSNames.MasterKeyKey]));
    }

    [Fact]
    public async Task With_the_local_topology_alone_the_cluster_store_throws_naming_AddBrainStorage()
    {
        using var home = new TempHome();
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { [IntoChatOSNames.HomeKey] = home.Path });
        var context = TopologyContext.Create(Topology.Local, StateHome.Resolve(builder.Configuration), builder.Configuration, []);
        builder.UseOrleans(silo => new LocalProfile().ConfigureSilo(silo, context));
        using var host = builder.Build();

        var cluster = host.Services.GetRequiredKeyedService<IGrainStorage>(IntoChatOSNames.ClusterGrainStorage);

        Assert.IsType<UnregisteredClusterStorage>(cluster);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => cluster.WriteStateAsync("s", GrainId.Create("probe", "k"), new GrainState<int>()));
        Assert.Contains("AddBrainStorage", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("platform")]
    [InlineData("pubsubstore")]
    public void A_module_grain_store_with_a_reserved_name_is_an_issue_naming_the_module(string name)
    {
        var module = new ModuleManifest([], [], [Need.State(name)], [], []) { Id = "greedy" };
        var issue = Assert.Single(new LocalProfile().Validate(new ConfigurationBuilder().Build(), [module]));
        Assert.Contains("greedy", issue.Key, StringComparison.Ordinal);
        Assert.Contains(name, issue.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Every_need_kind_is_bound(NeedKind kind) => Assert.True(new LocalProfile().Bindings.Binds(kind));

    public static TheoryData<NeedKind> Kinds() => [.. Enum.GetValues<NeedKind>()];

    [Fact]
    public void The_cluster_store_is_host_bound() => Assert.Equal(ClusterStoreBinding.HostBound, new LocalProfile().Bindings.ClusterStore);

    [Fact]
    public void Services_describe_one_os_process_with_silo_and_gateway_ports_and_the_home_volume()
    {
        var os = Assert.Single(new LocalProfile().Services.Processes, p => p.Name == "os");
        Assert.Contains(11111, os.Ports);
        Assert.Contains(30000, os.Ports);
        Assert.Contains(LocalProfile.HttpPort, os.Ports);
        Assert.Equal(["<Home>"], os.Volumes);
        Assert.Contains("IntoChatOS__Topology", os.EnvironmentKeys);
        Assert.Contains("IntoChatOS__Home", os.EnvironmentKeys);
        Assert.Empty(new LocalProfile().Services.AzureResourceKinds);
    }

    [Fact]
    public void The_local_topology_is_built_in()
    {
        using var home = new TempHome();
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [IntoChatOSNames.TopologyKey] = "local",
            [IntoChatOSNames.HomeKey] = home.Path,
        });
        builder.UseClusterStore(new FakeClusterStore());
        Assert.IsType<LocalProfile>(builder.UseTopology([], []));
    }
}
