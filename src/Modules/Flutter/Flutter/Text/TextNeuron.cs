using System.Text.RegularExpressions;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Text.Signals;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.Flutter.Text;

[GrainType(UIVocabulary.TextType)]
internal sealed class TextNeuron([PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<TextState> store)
    : Neuron<TextState>(store), IText
{
    public Task Set(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        var next = Snapshot;
        next.Name = this.GetPrimaryKeyString();
        next.Version++;
        next.Markdown = markdown;
        return Save(next, new TextChanged(this.GetPrimaryKeyString()));
    }

    [ReadOnly] public Task<TextState> Read() { Snapshot.Name = this.GetPrimaryKeyString(); return Task.FromResult(Snapshot); }
}