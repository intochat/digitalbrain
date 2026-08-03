using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;

namespace DigitalBrain.TestingTests.Harness;

public sealed class JournalHangProbe : Neuron, IJournalHangProbe
{
    public async Task EmitWhileObserverReenters(string greeterName, string watcherName, string guest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(greeterName);
        ArgumentException.ThrowIfNullOrWhiteSpace(watcherName);
        ArgumentException.ThrowIfNullOrWhiteSpace(guest);

        var greeter = Greeter(greeterName);
        var watcher = ReenteringWatcher(watcherName);
        await watcher.Arm(greeterName);
        await greeter.Watch(JournalKind.Outgoing, afterSequence: 0, watcher);
        // Greet emits on the greeter turn; awaiting ObserveAsync there is the hang cycle.
        await greeter.Greet(guest);
    }

    public async Task EmitWhileObserverIsStuck(
        string greeterName,
        string guest,
        IJournalObserver stuckObserver)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(greeterName);
        ArgumentException.ThrowIfNullOrWhiteSpace(guest);
        ArgumentNullException.ThrowIfNull(stuckObserver);

        var greeter = Greeter(greeterName);
        await greeter.Watch(JournalKind.Outgoing, afterSequence: 0, stuckObserver);
        await greeter.Greet(guest);
    }

    public Task<int> Reentries(string watcherName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(watcherName);
        return ReenteringWatcher(watcherName).Reentries();
    }

    private IGreeter Greeter(string name)
        => GrainFactory.GetGrain<IGreeter>(NeuronId.For<IGreeter>(Id.Owner, name).ToGrainId());

    private IReenteringJournalWatcher ReenteringWatcher(string name)
        => GrainFactory.GetGrain<IReenteringJournalWatcher>(
            NeuronId.For<IReenteringJournalWatcher>(Id.Owner, name).ToGrainId());
}
