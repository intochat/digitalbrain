using DigitalBrain.Spi;
using OS = DigitalBrain.Testing.TestOS;

namespace DigitalBrain.Testing.Tests.Unit.Hosting;

public sealed class ObserverFacts
{
    private static readonly BrainId Brain = new("ada");
    private static readonly NeuronId Source = new(Brain, "rx.source", "s");
    private static readonly NeuronId Sink = new(Brain, "rx.sink", "k");

    [Fact]
    public async Task Synapses_lists_each_signal_alias_and_Deliveries_yields_a_handed_signal()
    {
        var probe = new Probe();
        await using var os = OS.Local(options => options.Services = probe).Register(typeof(NumberSink).Assembly);
        var seen = new List<SignalDelivery>();
        var token = TestContext.Current.CancellationToken;
        var reading = Task.Run(async () =>
        {
            await foreach (var delivery in os.Deliveries(token))
            {
                seen.Add(delivery);
                break;
            }
        }, token);

        await os.Get<INumberSink>(Sink).Subscribe(Source);
        var observer = (IBrainObserver)os;
        var rows = await observer.Synapses(token);
        Assert.Contains(rows, row => row == new ObservedSynapse(Source.ToString(), Sink.ToString(), "test.number"));

        await os.Get<INumberSource>(Source).Emit(4);
        await os.Idle();
        await reading.WaitAsync(token);
        Assert.Equal([new SignalDelivery(Source.ToString(), Sink.ToString(), "test.number", 1)], seen);
    }
}
