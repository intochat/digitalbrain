namespace DigitalBrain.Flutter.Http;

internal sealed record SceneOpenedEvent(long Sequence, string SceneKey, string Title, string CommandId, string Shell);
