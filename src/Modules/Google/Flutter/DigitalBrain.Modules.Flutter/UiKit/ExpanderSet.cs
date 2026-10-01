namespace DigitalBrain.Flutter;

internal sealed record ExpanderSet(string Header, bool Expanded, IReadOnlyList<UiChildRef>? Children);
