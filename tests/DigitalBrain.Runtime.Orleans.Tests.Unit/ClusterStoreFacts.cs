using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Orleans.Hosting;

namespace DigitalBrain.Runtime.Orleans.Tests.Unit;

public sealed class ClusterStoreFacts : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "db-cluster-" + Guid.NewGuid().ToString("N"));

    private static readonly Bindings HostBound = FakeTopology.AllBound with { ClusterStore = ClusterStoreBinding.HostBound };

    private HostApplicationBuilder Builder()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection([new(IntoChatOSNames.HomeKey, _home), new(IntoChatOSNames.TopologyKey, "local")]);
        return builder;
    }

    [Fact]
    public void Unbound_host_bound_cluster_store_fails_start_naming_the_hook()
    {
        var local = new FakeTopology(Topology.Local, HostBound);

        var thrown = Assert.Throws<InvalidOperationException>(() => Builder().UseTopology([], [], local));

        Assert.Contains("grain store cluster", thrown.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(ClusterStoreHosting.UseClusterStore), thrown.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(IClusterStoreRegistration), thrown.Message, StringComparison.Ordinal);
        Assert.False(local.ServicesConfigured);
    }

    [Fact]
    public void A_module_needing_cluster_is_reported_once_when_the_hook_is_missing()
    {
        var issues = TopologyValidator.ValidateNeeds(Topology.Local, HostBound, [Manifests.Needing("m", Need.State("cluster"))], clusterStoreRegistered: false);

        var issue = Assert.Single(issues);
        Assert.Contains(nameof(ClusterStoreHosting.UseClusterStore), issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_fake_registration_satisfies_the_cluster_need_and_runs_on_the_silo()
    {
        var builder = Builder();
        var registration = new FakeRegistration();
        builder.UseClusterStore(registration);
        var local = new FakeTopology(Topology.Local, HostBound);

        builder.UseTopology([Manifests.Needing("m", Need.State("cluster"))], [], local);
        Assert.True(local.ServicesConfigured);

        var silo = new FakeSilo(builder.Services, builder.Configuration);
        silo.ConfigureTopologySilo();
        Assert.Equal(1, registration.Calls);
    }

    [Fact]
    public void A_delegate_registration_counts_as_the_hook()
    {
        var builder = Builder();
        var called = 0;
        builder.UseClusterStore(_ => called++);

        builder.UseTopology([], [], new FakeTopology(Topology.Local, HostBound));
        new FakeSilo(builder.Services, builder.Configuration).ConfigureTopologySilo();

        Assert.Equal(1, called);
    }

    [Fact]
    public void Without_host_bound_binding_a_cluster_need_is_plainly_unbound()
    {
        var issues = TopologyValidator.ValidateNeeds(Topology.Local, FakeTopology.AllBound, [Manifests.Needing("m", Need.State("cluster"))], clusterStoreRegistered: true);

        Assert.Contains("module m", Assert.Single(issues).Key, StringComparison.Ordinal);
    }

    [Fact]
    public void Other_named_stores_still_follow_grain_store_bindings()
        => Assert.Empty(TopologyValidator.ValidateNeeds(Topology.Local, HostBound, [Manifests.Needing("m", Need.State("platform"), Need.State("pubsubstore"))], clusterStoreRegistered: true));

    public void Dispose()
    {
        if (Directory.Exists(_home)) { Directory.Delete(_home, recursive: true); }
    }

    private sealed class FakeRegistration : IClusterStoreRegistration
    {
        public int Calls { get; private set; }

        public void Register(ISiloBuilder silo) => Calls++;
    }

    private sealed class FakeSilo(Microsoft.Extensions.DependencyInjection.IServiceCollection services, IConfiguration configuration) : ISiloBuilder
    {
        public Microsoft.Extensions.DependencyInjection.IServiceCollection Services { get; } = services;

        public IConfiguration Configuration { get; } = configuration;
    }
}
