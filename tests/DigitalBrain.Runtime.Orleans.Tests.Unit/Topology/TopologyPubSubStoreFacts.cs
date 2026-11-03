using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans.Hosting;
using Orleans.Providers.Streams.Common;
using Orleans.Storage;
using Orleans.Streams;

using OrleansSdk = Orleans;
namespace DigitalBrain.Runtime.Orleans.Tests.Unit;

// RT-06, 04 D-09: Phase 3 ships the stream and PubSubStore bindings; these facts pin them and the durability rule.
public sealed class TopologyPubSubStoreFacts
{
    private static ServiceProvider With(IGrainStorage? pubSub)
    {
        var services = new ServiceCollection();
        if (pubSub is not null) { services.AddKeyedSingleton(RuntimeOptions.Default.PubSubStore, pubSub); }
        return services.BuildServiceProvider();
    }

    private static MemoryGrainStorage Memory() => new(RuntimeOptions.Default.PubSubStore, new OrleansSdk.Configuration.MemoryGrainStorageOptions(),
        Microsoft.Extensions.Logging.Abstractions.NullLogger<MemoryGrainStorage>.Instance,
        null!, null!, null!);

    [Theory]
    [InlineData(Topology.Local)]
    [InlineData(Topology.LocalCloud)]
    [InlineData(Topology.Cloud)]
    public void A_missing_PubSubStore_is_an_issue_in_every_topology(Topology topology)
    {
        using var services = With(null);
        var issue = Assert.Single(TopologyValidator.ValidatePubSubStore(topology, services));
        Assert.Contains(RuntimeOptions.Default.PubSubStore, issue.Key, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Topology.Cloud, "cloud")]
    public void A_memory_PubSubStore_outside_local_is_an_issue_naming_key_and_topology(Topology topology, string value)
    {
        using var services = With(Memory());
        var issue = Assert.Single(TopologyValidator.ValidatePubSubStore(topology, services));
        Assert.Contains(RuntimeOptions.Default.PubSubStore, issue.Key, StringComparison.Ordinal);
        Assert.Contains("Topology=" + value, issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_startup_check_throws_naming_PubSubStore()
    {
        using var services = With(Memory());
        var check = new PubSubStoreCheck(Topology.Cloud, services);
        var error = Assert.Throws<InvalidOperationException>(check.Run);
        Assert.Contains(RuntimeOptions.Default.PubSubStore, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Local_profiles_bind_memory_signal_streams_over_the_memory_PubSubStore(bool localCloud)
    {
        using var home = new TempDirectory();
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { [RuntimeOptions.Default.HomeKey] = home.Path });
        var topology = localCloud ? Topology.LocalCloud : Topology.Local;
        var context = TopologyContext.Create(topology, StateHome.Resolve(builder.Configuration, RuntimeOptions.Default), builder.Configuration, []);
        ITopologyProfile profile = localCloud ? new LocalCloudProfile() : new LocalProfile();
        builder.UseOrleans(silo => profile.ConfigureSilo(silo, context));
        using var host = builder.Build();

        Assert.IsType<LocalMemoryGrainStorage>(host.Services.GetRequiredKeyedService<IGrainStorage>(RuntimeOptions.Default.PubSubStore));
        Assert.IsType<PersistentStreamProvider>(host.Services.GetRequiredKeyedService<IStreamProvider>(RuntimeOptions.Default.SignalStreams));
        Assert.Contains("Memory", host.Services.GetRequiredKeyedService<IQueueAdapterFactory>(RuntimeOptions.Default.SignalStreams).GetType().Name, StringComparison.Ordinal);
        Assert.Empty(TopologyValidator.ValidatePubSubStore(topology, host.Services));
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "db-ps-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(Path)) { Directory.Delete(Path, recursive: true); }
        }
    }
}
