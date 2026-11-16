using DigitalBrain.Kernel;
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
        // Subscribe synchronously before Task.Run so threadpool scheduling never races ahead of Emit.
        var deliveries = os.Deliveries(token);
        var reading = Task.Run(async () =>
        {
            await foreach (var delivery in deliveries)
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

    [Fact]
    public async Task Deliveries_registers_listener_synchronously_before_first_move_next()
    {
        var probe = new Probe();
        await using var os = OS.Local(options => options.Services = probe).Register(typeof(NumberSink).Assembly);
        var token = TestContext.Current.CancellationToken;

        await os.Get<INumberSink>(Sink).Subscribe(Source);
        var deliveries = os.Deliveries(token);

        // Emit and settle completely before GetAsyncEnumerator / MoveNextAsync is ever touched.
        await os.Get<INumberSource>(Source).Emit(7);
        await os.Idle();

        var seen = new List<SignalDelivery>();
        await foreach (var delivery in deliveries)
        {
            seen.Add(delivery);
            break;
        }

        Assert.Equal([new SignalDelivery(Source.ToString(), Sink.ToString(), "test.number", 1)], seen);
    }

    [Fact]
    public async Task Deliveries_canceled_before_enumeration_throws_when_iterated()
    {
        var probe = new Probe();
        await using var os = OS.Local(options => options.Services = probe).Register(typeof(NumberSink).Assembly);
        using var cts = new CancellationTokenSource();

        var deliveries = os.Deliveries(cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in deliveries)
            {
            }
        });
    }
}
