using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.Flutter.Select;

[Alias("select"), Orleans.Metadata.DefaultGrainType(UIVocabulary.SelectType)]
public interface ISelect : INeuron
{
    Task Set(string label, IReadOnlyList<SelectOption> options, string? selected);
    Task Choose(string value);
    [ReadOnly, Alias("read")] Task<SelectState> Read();
}

[GenerateSerializer, Alias("ui.select-option")]
public sealed record SelectOption([property: Id(0)] string Id, [property: Id(1)] string Label, [property: Id(2)] bool Enabled = true);

[GenerateSerializer, Alias("ui.select-state")]
public sealed class SelectState
{
    [Id(0)] public string Name { get; set; } = "";
    [Id(1)] public long Revision { get; set; }
    [Id(2)] public string Label { get; set; } = "";
    [Id(3)] public IReadOnlyList<SelectOption> Options { get; set; } = [];
    [Id(4)] public string? Selected { get; set; }
}

[GenerateSerializer, Alias("ui.select-changed")]
public sealed record SelectChanged([property: Id(0)] string Name, [property: Id(1)] string Value) : Signal;
