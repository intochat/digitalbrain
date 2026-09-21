using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.Flutter.Expander;

[Alias("expander"), Orleans.Metadata.DefaultGrainType(UIVocabulary.ExpanderType)]
public interface IExpander : INeuron
{
    Task Set(string header, bool expanded, IReadOnlyList<UiChildRef>? children = null);
    Task Toggle();
    [ReadOnly, Alias("read")] Task<ExpanderState> Read();
}

[GenerateSerializer, Alias("ui.expander-state")]
public sealed class ExpanderState
{
    [Id(0)] public string Name { get; set; } = "";
    [Id(1)] public int Version { get; set; }
    [Id(2)] public string Header { get; set; } = "";
    [Id(3)] public bool Expanded { get; set; }
    [Id(4)] public List<UiChildRef> Children { get; set; } = [];
}