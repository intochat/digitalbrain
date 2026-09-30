using System.Text.Json;
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
        next.Activation = null;
        return Save(next, new ButtonChanged(this.GetPrimaryKeyString(), next.Version));
    }

    public Task SetActivation(string label, string activationJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentException.ThrowIfNullOrWhiteSpace(activationJson);
        if (activationJson.Length > 32_000) { throw new ArgumentException("Activation JSON has at most 32000 characters.", nameof(activationJson)); }
        try
        {
            using var document = JsonDocument.Parse(activationJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object) { throw new ArgumentException("Activation must be a JSON object.", nameof(activationJson)); }
        }
        catch (JsonException error) { throw new ArgumentException("Activation must be a JSON object.", nameof(activationJson), error); }
        var next = Snapshot;
        next.Name = this.GetPrimaryKeyString();
        next.Version++;
        next.Label = label.Trim();
        next.Enabled = true;
        next.Action = "activate";
        next.Activation = activationJson;
        return Save(next, new ButtonChanged(next.Name, next.Version));
    }

    public async Task Click()
    {
        var next = Snapshot;
        if (!next.Enabled || string.IsNullOrWhiteSpace(next.Action))
        {
            throw new InvalidOperationException("Button is not ready to click.");
        }

        next.Name = this.GetPrimaryKeyString();
        next.Version++;
        next.ClickCount++;
        var clicked = new ButtonClicked(this.GetPrimaryKeyString(), next.Action);
        await Save(next, new ButtonChanged(this.GetPrimaryKeyString(), next.Version), clicked);
        await GrainFactory.GetGrain<IUiBinding>(this.GetPrimaryKeyString()).Dispatch(clicked);
    }

    [ReadOnly] public Task<ButtonState> Read() => Task.FromResult(Named(Snapshot));

    private ButtonState Named(ButtonState state) { state.Name = this.GetPrimaryKeyString(); return state; }
}
