using System.Collections.Concurrent;
using OS = DigitalBrain.Testing.TestOS;

namespace DigitalBrain.Testing.Tests.Unit.Hosting;

// 04-19: host-level listeners hear a signal of every brain, idempotently per listener, across a TestOS restart.
public sealed class ClusterListenerFacts
{
    private static Task<IAsyncDisposable> Record(OS os, string listener, ConcurrentQueue<string> into)
        => os.ClusterListeners.Listen<Number>(listener, (publisher, signal, _) =>
        {
            into.Enqueue($"{publisher.Brain}:{signal.Value}");
            return Task.CompletedTask;
        });

    [Fact]
    public async Task A_cluster_listener_hears_the_signals_of_every_brain_including_a_brain_new_after_it_registered()
    {
        await using var os = OS.Local().Register<NumberSource>();
        var heard = new ConcurrentQueue<string>();
        await using var registration = await Record(os, "boot", heard);

        await os.Get<INumberSource>(NeuronId.Parse("cl-a/rx.source/s")).Emit(1);
        await os.Get<INumberSource>(NeuronId.Parse("cl-new/rx.source/s")).Emit(2);
        await os.Get<INumberSource>(NeuronId.Parse("cl-a/rx.source/s")).EmitOther();
        await os.Idle();

        Assert.Equal(["cl-a:1", "cl-new:2"], heard);
    }

    [Fact]
    public async Task Listening_again_under_the_same_listener_replaces_the_handler_and_disposing_stops_it()
    {
        await using var os = OS.Local().Register<NumberSource>();
        var first = new ConcurrentQueue<string>();
        var second = new ConcurrentQueue<string>();
        var stale = await Record(os, "boot", first);
        var current = await Record(os, "boot", second);
        var source = os.Get<INumberSource>(NeuronId.Parse("cl-i/rx.source/s"));

        await source.Emit(1);
        await os.Idle();
        await stale.DisposeAsync(); // replaced already: a no-op
        await source.Emit(2);
        await os.Idle();
        await current.DisposeAsync();
        await source.Emit(3);
        await os.Idle();

        Assert.Empty(first);
        Assert.Equal(["cl-i:1", "cl-i:2"], second);
    }

    [Fact]
    public async Task After_a_restart_a_listener_registered_again_at_start_hears_each_signal_once()
    {
        await using var os = OS.Local().Register<NumberSource>();
        var heard = new ConcurrentQueue<string>();
        await Record(os, "boot", heard);
        await os.Get<INumberSource>(NeuronId.Parse("cl-r/rx.source/s")).Emit(1);
        await os.Restart();
        await Record(os, "boot", heard); // the host's start-up registration runs again
        await os.Get<INumberSource>(NeuronId.Parse("cl-r/rx.source/s")).Emit(2);
        await os.Idle();

        Assert.Equal(["cl-r:1", "cl-r:2"], heard);
    }

    [Fact]
    public async Task A_throwing_listener_does_not_stop_the_others()
    {
        await using var os = OS.Local().Register<NumberSource>();
        await using var boom = await os.ClusterListeners.Listen<Number>("boom", (_, _, _) => throw new InvalidOperationException("boom"));
        var heard = new ConcurrentQueue<string>();
        await using var good = await Record(os, "good", heard);
        await os.Get<INumberSource>(NeuronId.Parse("cl-t/rx.source/s")).Emit(4);
        await os.Idle();
        Assert.Equal(["cl-t:4"], heard);
    }

    [Fact]
    public async Task An_ephemeral_signal_is_delivered_to_cluster_listeners_without_writing_to_journal()
    {
        await using var os = OS.Local().Register<EphemeralSource>();
        var heard = new ConcurrentQueue<int>();
        await using var reg = await os.ClusterListeners.Listen<EphemeralTick>("ephemeral-test", (_, signal, _) =>
        {
            heard.Enqueue(signal.Value);
            return Task.CompletedTask;
        });

        var id = NeuronId.Parse("cl-eph/rx.ephemeral-source/s");
        var source = os.Get<IEphemeralSource>(id);
        await source.EmitEphemeral(42);
        await os.Idle();

        Assert.Equal([42], heard);
        // Verify journal has zero entries for this neuron
        Assert.Empty(os.Journal(id.Brain).Signals(id));
        Assert.Equal(0, os.Journal(id.Brain).Last(id));
    }
}

public interface IEphemeralSource
{
    Task EmitEphemeral(int value);
    Task EmitDirectEphemeral(int value);
}

[Signal("rx.ephemeral"), EphemeralSignal]
public sealed record EphemeralTick(int Value) : Signal;

[Neuron("rx.ephemeral-source")]
public sealed class EphemeralSource : Neuron, IEphemeralSource
{
    public Task EmitEphemeral(int value) => Publish(new EphemeralTick(value));
    public Task EmitDirectEphemeral(int value) => PublishEphemeral(new Number(value));
}
