using System.Text.RegularExpressions;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.TextField.Signals;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.Flutter.TextField;

[GrainType(UIVocabulary.TextFieldType)]
internal sealed class TextFieldNeuron([PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<TextFieldState> store)
    : Neuron<TextFieldState>(store), ITextField
{
    private static readonly HashSet<string> Kinds = new(StringComparer.OrdinalIgnoreCase) { "text", "number", "password", "secret", "suggest", "multiline" };

    public Task Configure(string label, string kind, string? submitButton = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        if (submitButton is not null && (string.IsNullOrWhiteSpace(submitButton) || submitButton.Length > 512))
        { throw new ArgumentException("A submit button name must contain between 1 and 512 characters.", nameof(submitButton)); }
        if (!Kinds.Contains(kind)) { throw new ArgumentOutOfRangeException(nameof(kind)); }
        var next = Snapshot;
        next.Name = this.GetPrimaryKeyString();
        next.Version++;
        next.Label = label.Trim();
        next.Kind = kind.Trim().ToLowerInvariant();
        next.SubmitButton = submitButton;
        return Save(next, new TextFieldChanged(this.GetPrimaryKeyString(), next.Value));
    }

    public Task SetValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var next = Snapshot;
        next.Name = this.GetPrimaryKeyString();
        next.Version++;
        next.Value = value;
        return Save(next, new TextFieldChanged(this.GetPrimaryKeyString(), next.Value));
    }

    public async Task Input(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length > 32_000) { throw new ArgumentException("Input has at most 32000 characters.", nameof(value)); }
        await SetValue(value);
        await GrainFactory.GetGrain<IUiBinding>(this.GetPrimaryKeyString()).Dispatch(new TextFieldChanged(this.GetPrimaryKeyString(), value));
    }

    [ReadOnly] public Task<TextFieldState> Read() { Snapshot.Name = this.GetPrimaryKeyString(); return Task.FromResult(Snapshot); }
}