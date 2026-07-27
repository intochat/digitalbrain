namespace DigitalBrain.UI;

internal sealed record ChatTurnEvent(
    long Sequence,
    bool FromUser,
    string Text,
    string CommandId);
