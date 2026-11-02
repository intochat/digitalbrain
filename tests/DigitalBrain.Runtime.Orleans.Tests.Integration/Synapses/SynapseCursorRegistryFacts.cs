using DigitalBrain.Runtime.Orleans.Synapses;
using DigitalBrain.Runtime.Orleans.Testing;
using DigitalBrain.Spi;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Hosting;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Synapses;

public sealed class SynapseCursorRegistryFacts
{
    private static readonly NeuronId P = NeuronId.Parse("b1/chat/p");

    internal sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 25, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>The cluster store on files under the home, plus synapse retention on a manual clock.</summary>
    private sealed class FileClusterWithRetention(string home, ManualTime time) : IClusterStoreRegistration
    {
        public void Register(ISiloBuilder silo)
        {
            silo.AddFileGrainStorage(IntoChatOSNames.ClusterGrainStorage, o => o.RootDirectory = Path.Combine(home, "state", "files", IntoChatOSNames.ClusterGrainStorage));
            silo.Services.AddSingleton<TimeProvider>(time);
            silo.AddSynapseRetention();
        }
    }

    private static Task<IInProcessHost> Start(TempHome home, ManualTime time, CancellationToken ct)
        => new LocalSiloHostFactory(new FileClusterWithRetention(home.Path, time)).Start(new InProcessHostOptions { Home = home.Path }, ct);

    private static IGrainFactory Grains(IInProcessHost host) => host.Services.GetRequiredService<IGrainFactory>();

    [Fact]
    public async Task LowestLive_is_the_lowest_reported_cursor()
    {
        var ct = TestContext.Current.CancellationToken;
        using var home = new TempHome();
        await using var host = await Start(home, new ManualTime(), ct);
        var registry = Grains(host).GetGrain<ISynapseCursorRegistry>("b1");
        await registry.ReportCursor("a", P, 5);
        await registry.ReportCursor("b", P, 9);
        Assert.Equal(5, await registry.LowestLive(P));
        Assert.Equal(5, await host.Services.GetRequiredService<IRetentionPolicy>().FloorFor("b1", P, 20_000, _ => 0, ct));
    }

    [Fact]
    public async Task A_report_older_than_StaleCursorAfter_stops_pinning()
    {
        var ct = TestContext.Current.CancellationToken;
        using var home = new TempHome();
        var time = new ManualTime();
        await using var host = await Start(home, time, ct);
        var registry = Grains(host).GetGrain<ISynapseCursorRegistry>("b1");
        await registry.ReportCursor("a", P, 5);
        time.Now += TimeSpan.FromDays(8);
        await registry.ReportCursor("b", P, 9);
        Assert.Equal(9, await registry.LowestLive(P));
        Assert.Empty(await registry.Behind(P, 8));
    }

    [Fact]
    public async Task Forget_removes_a_subscriber_and_Behind_lists_live_cursors_below_last()
    {
        var ct = TestContext.Current.CancellationToken;
        using var home = new TempHome();
        await using var host = await Start(home, new ManualTime(), ct);
        var registry = Grains(host).GetGrain<ISynapseCursorRegistry>("b1");
        var q = NeuronId.Parse("b1/chat/q");
        await registry.ReportCursor("a", P, 5);
        await registry.ReportCursor("a", q, 1);
        await registry.ReportCursor("b", P, 7);
        await registry.ReportCursor("c", P, 9);
        Assert.Equal(["a", "b"], (await registry.Behind(P, 8)).Order());
        await registry.Forget("a");
        Assert.Equal(["b"], await registry.Behind(P, 8));
        Assert.Null(await registry.LowestLive(q));
        Assert.Equal([P], await registry.Publishers());
    }

    [Fact]
    public async Task A_publisher_of_another_brain_is_refused()
    {
        var ct = TestContext.Current.CancellationToken;
        using var home = new TempHome();
        await using var host = await Start(home, new ManualTime(), ct);
        var registry = Grains(host).GetGrain<ISynapseCursorRegistry>("b1");
        var foreign = NeuronId.Parse("b2/chat/p");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => registry.ReportCursor("a", foreign, 1));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => registry.LowestLive(foreign));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => registry.Behind(foreign, 1));
    }

    [Fact]
    public async Task GetCursor_returns_reported_cursor_or_null()
    {
        var ct = TestContext.Current.CancellationToken;
        using var home = new TempHome();
        await using var host = await Start(home, new ManualTime(), ct);
        var registry = Grains(host).GetGrain<ISynapseCursorRegistry>("b1");
        Assert.Null(await registry.GetCursor("a", P));

        await registry.ReportCursor("a", P, 42);
        Assert.Equal(42L, await registry.GetCursor("a", P));
    }

    [Fact]
    public async Task Registry_survives_a_silo_restart_on_the_file_cluster_store()
    {
        var ct = TestContext.Current.CancellationToken;
        using var home = new TempHome();
        await using var host = await Start(home, new ManualTime(), ct);
        await Grains(host).GetGrain<ISynapseCursorRegistry>("b1").ReportCursor("a", P, 5);
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(home.Path, "state", "files", IntoChatOSNames.ClusterGrainStorage)));

        await host.Restart(ct);

        Assert.Equal(5, await Grains(host).GetGrain<ISynapseCursorRegistry>("b1").LowestLive(P));
        Assert.Equal(5L, await Grains(host).GetGrain<ISynapseCursorRegistry>("b1").GetCursor("a", P));
    }
}
