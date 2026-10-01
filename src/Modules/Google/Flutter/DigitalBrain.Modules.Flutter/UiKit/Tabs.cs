using DigitalBrain.Flutter.Tabs;

namespace DigitalBrain.Flutter;

internal sealed record TabsSet(IReadOnlyList<TabItem> Tabs, string SelectedId);

internal sealed record TabsSelect(string Id);
