using System.Text.RegularExpressions;
using DigitalBrain;
using DigitalBrain.Contracts;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Clock.Signals;
using DigitalBrain.Kernel;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.Flutter.Clock;

[GrainType(UIVocabulary.ClockType)]
internal sealed class ClockNeuron([PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<ClockState> store)
    : Neuron<ClockState>(store), IClock
{
    public Task Set(string label, DateTimeOffset? dueAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        var next = Snapshot;
        next.Name = this.GetPrimaryKeyString();
        next.Version++;
        next.Label = label.Trim();
        next.DueAt = dueAt;
        return Save(next, new ClockChanged(this.GetPrimaryKeyString(), next.Version));
    }

    [ReadOnly] public Task<ClockState> Read() { Snapshot.Name = this.GetPrimaryKeyString(); return Task.FromResult(Snapshot); }
}
