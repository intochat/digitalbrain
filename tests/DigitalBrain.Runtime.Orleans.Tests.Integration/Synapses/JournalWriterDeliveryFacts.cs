using System.Text;
using DigitalBrain.Runtime.Orleans.Tests.Integration.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Synapses;

// 04-17 (R4-10): signals an os adapter journals through IBrainJournalWriter reach plain [On] subscribers; unknown types are passed over.
public sealed class JournalWriterDeliveryFacts(SynapseSilo silo) : IClassFixture<SynapseSilo>
{
    private static JournalSignalWrite Write(string type, string json) => new(type, Encoding.UTF8.GetBytes(json));

    [Fact]
    public async Task Adapter_signals_of_a_registered_type_reach_a_plain_subscriber_and_unknown_types_are_skipped()
    {
        var ct = TestContext.Current.CancellationToken;
        const string Listener = "b1/probe.listener/adapter", Source = "b1/probe.counter/adapter";
        await silo.Get<IListener>(Listener).Follow(Source);
        var writer = silo.Services.GetRequiredService<IBrainJournalWriter>();

        // A legacy grain's state and its os signals in one entry, then a run of unknown-type signals longer than one history page.
        await writer.Append("b1", new JournalStateWrite("legacy.counter/b1/adapter", "state", JournalStateChange.Write, null, new byte[] { 0x01, 0xFF }, "IntoChat.OS.Legacy.CounterState"),
            Source, [Write("os.legacy.ticked", """{"at":1}"""), Write("probe.counted", """{"value":7}""")], ct);
        await writer.Append("b1", null, Source, [.. Enumerable.Range(0, SignalPage.MaxLimit + 5).Select(n => Write("os.legacy.ticked", $$"""{"at":{{n}}}"""))], ct);
        await writer.Append("b1", null, Source, [Write("probe.counted", """{"value":8}""")], ct);

        await Heard.Until(Listener, 2, ct);
        await Task.Delay(300, ct);
        Assert.Equal(["counted:7", "counted:8"], Heard.Of(Listener));
    }
}
