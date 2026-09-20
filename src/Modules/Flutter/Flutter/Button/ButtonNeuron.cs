using System.Text.RegularExpressions;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Button.Signals;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.Flutter.Button;
[GrainType(UIVocabulary.ButtonType)]
internal sealed class ButtonNeuron([PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<ButtonState> store)
    : Neuron<ButtonState>(store), IButton
{
    public Task Set(string label, string action, bool enabled = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        var next = Snapshot;
        next.Name = this.GetPrimaryKeyString();
        next.Version++;
        next.Label = label.Trim();
        next.Action = action.Trim();
        next.Enabled = enabled;
        return Save(next, new ButtonChanged(this.GetPrimaryKeyString(), next.Version));
    }

    public Task Click()
    {
        var next = Snapshot;
        if (!next.Enabled || string.IsNullOrWhiteSpace(next.Action))
        {
            throw new InvalidOperationException("Button is not ready to click.");
        }

        next.Name = this.GetPrimaryKeyString();
        next.Version++;
        next.ClickCount++;
        return Save(next, new ButtonChanged(this.GetPrimaryKeyString(), next.Version), new ButtonClicked(this.GetPrimaryKeyString(), next.Action));
    }

    [ReadOnly] public Task<ButtonState> Read() => Task.FromResult(Named(Snapshot));

    private ButtonState Named(ButtonState state) { state.Name = this.GetPrimaryKeyString(); return state; }
}

