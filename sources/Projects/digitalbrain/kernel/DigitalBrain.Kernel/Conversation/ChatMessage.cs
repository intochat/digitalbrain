namespace DigitalBrain.Kernel.Conversation;

public enum ChatRole { User, Assistant, System }

[GenerateSerializer]
public sealed record ChatMessage(
    [property: Id(0)] Guid           MessageId,
    [property: Id(1)] ChatRole       Role,
    [property: Id(2)] string         Text,
    [property: Id(3)] string?        RfwEnvelopeJson,
    [property: Id(4)] Guid           CorrelationId,
    [property: Id(5)] DateTimeOffset Timestamp);
