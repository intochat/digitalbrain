using System.Text.RegularExpressions;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Slider.Signals;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.Flutter.Slider;

[GrainType(UIVocabulary.SliderType)]
internal sealed class SliderNeuron([PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<SliderState> store)
    : Neuron<SliderState>(store), ISlider
{
    public Task Configure(double min, double max, double step)
    {
        if (max <= min) { throw new ArgumentOutOfRangeException(nameof(max)); }
        if (step <= 0) { throw new ArgumentOutOfRangeException(nameof(step)); }
        var next = Snapshot;
        next.Name = this.GetPrimaryKeyString();
        next.Version++;
        next.Min = min;
        next.Max = max;
        next.Step = step;
        next.Value = Math.Clamp(next.Value, min, max);
        return Save(next, new SliderChanged(this.GetPrimaryKeyString(), next.Value));
    }

    public Task SetValue(double value)
    {
        var next = Snapshot;
        next.Name = this.GetPrimaryKeyString();
        next.Version++;
        next.Value = Math.Clamp(value, next.Min, next.Max);
        return Save(next, new SliderChanged(this.GetPrimaryKeyString(), next.Value));
    }

    [ReadOnly] public Task<SliderState> Read() { Snapshot.Name = this.GetPrimaryKeyString(); return Task.FromResult(Snapshot); }
}