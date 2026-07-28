namespace DigitalBrain.UI;

internal sealed record SceneOpenedEvent(
    long Sequence,
    string SceneKey,
    string Title,
    string CommandId,
    string Shell);
