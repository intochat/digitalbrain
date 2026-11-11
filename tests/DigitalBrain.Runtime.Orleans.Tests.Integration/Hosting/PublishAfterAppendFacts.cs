using System.Collections.Concurrent;
using DigitalBrain.Kernel;
using DigitalBrain.Runtime.Orleans.Synapses;
using DigitalBrain.Spi;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Hosting;

// 04 D-01, T-04-34: a signal is sent only for a durable entry.
public sealed class PublishAfterAppendFacts(StatefulSilo silo) : IClassFixture<StatefulSilo>
{
    [Fact]
    public async Task The_signal_of_a_save_arrives_only_after_its_entry_is_in_the_journal()
    {
        var ct = TestContext.Current.CancellationToken;
        var publisher = NeuronId.Parse("b1/probe.counter/after");
        var history = silo.History("b1");
        var seenAtArrival = new List<long>();
        var items = new ConcurrentQueue<(NeuronId Pub, Counted Sig)>();
        var clusterListeners = silo.Services.GetRequiredService<IClusterListeners>();
        await using var reg = await clusterListeners.Listen<Counted>("test.after", async (pub, sig, token) =>
        {
            seenAtArrival.Add((await history.Read(pub, 0, 10, token)).Last);
            items.Enqueue((pub, sig));
        });
        await silo.Get<ICounter>("b1/probe.counter/after").Add(7);
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (items.IsEmpty && DateTime.UtcNow < deadline) { await Task.Delay(50, ct); }

        var (itemPub, itemSig) = Assert.Single(items);
        Assert.Equal(publisher, itemPub);
        Assert.Equal(7, itemSig.Value);
        Assert.All(seenAtArrival, last => Assert.True(last >= 1));
    }

    [Fact]
    public async Task With_FailAfterAppend_the_journal_holds_the_entry_and_no_reference_is_sent()
    {
        var ct = TestContext.Current.CancellationToken;
        var faults = silo.Services.GetRequiredService<PublishFaults>();
        var items = new ConcurrentQueue<(NeuronId Pub, Counted Sig)>();
        var clusterListeners = silo.Services.GetRequiredService<IClusterListeners>();
        await using var reg = await clusterListeners.Listen<Counted>("test.faulted", (pub, sig, token) =>
        {
            items.Enqueue((pub, sig));
            return Task.CompletedTask;
        });
        faults.FailAfterAppend = true;
        try
        {
            await Assert.ThrowsAnyAsync<Exception>(() => silo.Get<ICounter>("b1/probe.counter/faulted").Add(1));
        }
        finally { faults.FailAfterAppend = false; }

        var page = await silo.History("b1").Read(NeuronId.Parse("b1/probe.counter/faulted"), 0, 10, ct);
        Assert.Equal(1, ((Counted)Assert.Single(page.Signals).Signal).Value);
        await Task.Delay(500, ct);
        Assert.Empty(items);
    }
}
