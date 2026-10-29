using DigitalBrain.Runtime.Orleans.Synapses;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Hosting;

// 04 D-01, T-04-34: a reference is sent only for a durable entry.
public sealed class PublishAfterAppendFacts(StatefulSilo silo) : IClassFixture<StatefulSilo>
{
    [Fact]
    public async Task The_reference_of_a_save_arrives_only_after_its_entry_is_in_the_journal()
    {
        var ct = TestContext.Current.CancellationToken;
        var publisher = NeuronId.Parse("b1/probe.counter/after");
        var history = silo.History("b1");
        var seenAtArrival = new List<long>();
        var watched = await silo.Watch("b1", "probe");
        await silo.Get<ICounter>("b1/probe.counter/after").Add(7);
        await watched.Until(items => items.Count > 0, ct);
        foreach (var reference in watched.Items) { seenAtArrival.Add((await history.Read(reference.Publisher, 0, 10, ct)).Last); }

        Assert.Equal(new SignalReference(publisher, 1), Assert.Single(watched.Items));
        Assert.All(seenAtArrival, last => Assert.True(last >= 1));
    }

    [Fact]
    public async Task With_FailAfterAppend_the_journal_holds_the_entry_and_no_reference_is_sent()
    {
        var ct = TestContext.Current.CancellationToken;
        var faults = silo.Services.GetRequiredService<PublishFaults>();
        var watched = await silo.Watch("b1", "probe");
        faults.FailAfterAppend = true;
        try
        {
            await Assert.ThrowsAnyAsync<Exception>(() => silo.Get<ICounter>("b1/probe.counter/faulted").Add(1));
        }
        finally { faults.FailAfterAppend = false; }

        var page = await silo.History("b1").Read(NeuronId.Parse("b1/probe.counter/faulted"), 0, 10, ct);
        Assert.Equal(1, ((Counted)Assert.Single(page.Signals).Signal).Value);
        await Task.Delay(500, ct);
        Assert.DoesNotContain(watched.Items, reference => reference.Publisher == NeuronId.Parse("b1/probe.counter/faulted"));
    }
}
