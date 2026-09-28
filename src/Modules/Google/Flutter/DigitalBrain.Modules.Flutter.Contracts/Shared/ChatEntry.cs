namespace DigitalBrain.Flutter.Chat;

// Stable value types retained for persisted conversation messages.
[GenerateSerializer]
public enum ChatRole { User, Assistant }

[GenerateSerializer, Alias("ui.chat-entry")]
public sealed record ChatEntry([property: Id(0)] string Id, [property: Id(1)] ChatRole Role, [property: Id(2)] string Text);
