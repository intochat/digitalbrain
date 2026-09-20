using System.Text.RegularExpressions;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Sheet.Signals;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.Flutter.Sheet;
[GrainType(UIVocabulary.SheetType)]
internal sealed class SheetNeuron([PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<SheetState> store)
    : Neuron<SheetState>(store), ISheet
{
    public Task Set(string title, IReadOnlyList<SheetCell> cells)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(cells);
        var next = Snapshot;
        next.Name = this.GetPrimaryKeyString();
        next.Version++;
        next.Title = title.Trim();
        next.Cells = [.. cells];
        return Save(next, new SheetChanged(this.GetPrimaryKeyString(), next.Version));
    }

    [ReadOnly] public Task<SheetState> Read() { Snapshot.Name = this.GetPrimaryKeyString(); return Task.FromResult(Snapshot); }
}

