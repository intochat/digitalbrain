using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.Flutter.Select;

[GrainType(UIVocabulary.SelectType)]
internal sealed class SelectNeuron([PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<SelectState> store)
    : Neuron<SelectState>(store), ISelect
{
    public Task Set(string label, IReadOnlyList<SelectOption> options, string? selected)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(options);
        if (options.Count > 1000 || options.Any(option => option is null || option.Id is null || option.Id.Length > 200 || string.IsNullOrWhiteSpace(option.Label))
            || options.Select(option => option.Id).Distinct(StringComparer.Ordinal).Count() != options.Count)
        { throw new ArgumentException("Select options must have unique bounded IDs and nonempty labels.", nameof(options)); }
        if (selected is not null && !options.Any(option => option.Id == selected)) { throw new ArgumentException("Selected option is missing.", nameof(selected)); }
        return Save(new() { Name = this.GetPrimaryKeyString(), Revision = Snapshot.Revision + 1, Label = label.Trim(), Options = options.ToArray(), Selected = selected },
            new SelectChanged(this.GetPrimaryKeyString(), selected ?? ""));
    }
    public async Task Choose(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!Snapshot.Options.Any(option => option.Id == value && option.Enabled)) { throw new ArgumentException("Choose an enabled option.", nameof(value)); }
        var signal = new SelectChanged(this.GetPrimaryKeyString(), value);
        var next = Snapshot;
        next.Selected = value;
        next.Revision++;
        await Save(next, signal);
        await GrainFactory.GetGrain<IUiBinding>(this.GetPrimaryKeyString()).Dispatch(signal);
    }
    public Task<SelectState> Read() { Snapshot.Name = this.GetPrimaryKeyString(); return Task.FromResult(Snapshot); }
}
