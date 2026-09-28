using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.Flutter.Tabs;

[Alias("tabs"), Orleans.Metadata.DefaultGrainType(UIVocabulary.TabsType)]
public interface ITabs : INeuron
{
    Task Set(IReadOnlyList<TabItem> tabs, string selectedId);
    Task Select(string id);
    [ReadOnly, Alias("read")] Task<TabsState> Read();
}

[GenerateSerializer, Alias("ui.tab-item")]
public sealed record TabItem(
    [property: Id(0)] string Id,
    [property: Id(1)] string Title,
    [property: Id(2)] UiChildRef Child);

[GenerateSerializer, Alias("ui.tabs-state")]
public sealed class TabsState
{
    [Id(0)] public string Name { get; set; } = "";
    [Id(1)] public int Version { get; set; }
    [Id(2)] public List<TabItem> Tabs { get; set; } = [];
    [Id(3)] public string SelectedId { get; set; } = "";
}