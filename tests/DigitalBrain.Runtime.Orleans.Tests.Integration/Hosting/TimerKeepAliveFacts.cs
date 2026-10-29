using System.Collections.Concurrent;
using DigitalBrain.Runtime.Orleans.Hosting;
using DigitalBrain.Runtime.Orleans.Testing;
using DigitalBrain.Spi;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Hosting;

// 04-18: an opt-in keep-alive timer holds its activation past the idle window until it fires.
public sealed class TimerKeepAliveFacts
{
    [Fact]
    public async Task A_keep_alive_one_shot_timer_due_after_the_collection_age_fires_on_the_same_activation()
    {
        var ct = TestContext.Current.CancellationToken;
        // The same local host SiloHost.Local() starts, with a one-second collection age so an idle activation is collected quickly.
        await using var host = await new LocalSiloHostFactory(null, services => services.Configure<GrainCollectionOptions>(options =>
        {
            options.CollectionAge = TimeSpan.FromSeconds(1);
            options.CollectionQuantum = TimeSpan.FromMilliseconds(250);
        })).Start(new InProcessHostOptions { Modules = [typeof(HostingModule)] }, ct);

        var id = "b1/probe.keepalive/" + Guid.NewGuid().ToString("N");
        await NeuronHosting.Get<IKeepAliveProbe>(host.Services, NeuronId.Parse(id)).Arm("late", 3000);

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromSeconds(30));
        while (!KeepAliveLog.Fired.ContainsKey(id + ":late")) { await Task.Delay(100, limit.Token); }

        Assert.Equal(1, KeepAliveLog.Fired[id + ":late"]);
    }
}

internal static class KeepAliveLog
{
    public static readonly ConcurrentDictionary<string, int> Fired = new();
}

public interface IKeepAliveProbe
{
    Task Arm(string name, int dueMs);
}

[Neuron("probe.keepalive")]
public sealed class KeepAliveProbe : Neuron, IKeepAliveProbe
{
    public Task Arm(string name, int dueMs)
    {
        Context.Timers.Start(name, TimeSpan.FromMilliseconds(dueMs), null, keepAlive: true);
        return Task.CompletedTask;
    }

    protected override Task OnReminder(string name, CancellationToken cancellationToken)
    {
        KeepAliveLog.Fired.AddOrUpdate($"{Id}:{name}", 1, static (_, count) => count + 1);
        return Task.CompletedTask;
    }
}
