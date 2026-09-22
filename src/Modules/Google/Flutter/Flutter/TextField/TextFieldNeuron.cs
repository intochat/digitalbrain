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
    private static readonly HashSet<string> Kinds = new(StringComparer.OrdinalIgnoreCase) { "text", "number", "password", "suggest" };

    public Task Configure(string label, string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        if (!Kinds.Contains(kind)) { throw new ArgumentOutOfRangeException(nameof(kind)); }
        var next = Snapshot;
        next.Name = this.GetPrimaryKeyString();
        next.Version++;
        next.Label = label.Trim();
        next.Kind = kind.Trim().ToLowerInvariant();
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

    [ReadOnly] public Task<TextFieldState> Read() { Snapshot.Name = this.GetPrimaryKeyString(); return Task.FromResult(Snapshot); }
}