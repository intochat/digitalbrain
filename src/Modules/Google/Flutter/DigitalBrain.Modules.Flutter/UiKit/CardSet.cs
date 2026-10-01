namespace DigitalBrain.Flutter;

internal sealed record CardSet(string Title, string Body, IReadOnlyList<UiChildRef>? Children);
