using System.Text.RegularExpressions;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Progress.Signals;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.Flutter.Progress;

[GrainType(UIVocabulary.ProgressType)]
internal sealed class ProgressNeuron([PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<ProgressState> store)
    : Neuron<ProgressState>(store), IProgress
{
    public Task Set(bool determinate, double value, string label)
    {
        ArgumentNullException.ThrowIfNull(label);
        var next = Snapshot;
        next.Name = this.GetPrimaryKeyString();
        next.Version++;
        next.Determinate = determinate;
        next.Value = Math.Clamp(value, 0, 1);
        next.Label = label;
        return Save(next, new ProgressChanged(this.GetPrimaryKeyString(), next.Value));
    }

    [ReadOnly] public Task<ProgressState> Read() { Snapshot.Name = this.GetPrimaryKeyString(); return Task.FromResult(Snapshot); }
}