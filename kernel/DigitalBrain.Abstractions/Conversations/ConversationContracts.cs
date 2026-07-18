namespace DigitalBrain;

public enum ConversationRole
{
    Fast,
    Balanced,
    Reasoning
}

[GenerateSerializer]
[Alias(nameof(ConversationId))]
public readonly record struct ConversationId
{
    public const int MaximumLength = 256;

    public ConversationId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > MaximumLength ||
            value.Any(char.IsControl) ||
            !string.Equals(value, value.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("A bounded, trimmed, control-character-free conversation id is required.", nameof(value));

        Value = value;
    }

    [Id(0)]
    public string Value { get; }

    public override string ToString() => Value;
}

[GenerateSerializer]
[Alias(nameof(ConversationTurnId))]
public readonly record struct ConversationTurnId
{
    public ConversationTurnId(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("A non-empty turn id is required.", nameof(value));

        Value = value;
    }

    [Id(0)]
    public Guid Value { get; }

    public static ConversationTurnId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("N");
}

[GenerateSerializer]
[Alias(nameof(ConversationTurnRequest))]
public sealed record ConversationTurnRequest
{
    public const int MaximumTextLength = 65536;

    public ConversationTurnRequest(ConversationTurnId turnId, ConversationRole role, string text)
    {
        if (turnId.Value == Guid.Empty)
            throw new ArgumentException("A non-empty turn id is required.", nameof(turnId));
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (text.Length > MaximumTextLength)
            throw new ArgumentException("Turn text exceeds the supported length.", nameof(text));
        if (!Enum.IsDefined(role))
            throw new ArgumentException("A declared conversation role is required.", nameof(role));

        TurnId = turnId;
        Role = role;
        Text = text;
    }

    [Id(0)]
    public ConversationTurnId TurnId { get; }

    [Id(1)]
    public ConversationRole Role { get; }

    [Id(2)]
    public string Text { get; }
}

[GenerateSerializer]
[Alias(nameof(ConversationTurnResult))]
public sealed record ConversationTurnResult(
    [property: Id(0)] ConversationTurnId TurnId,
    [property: Id(1)] ConversationRole Role,
    [property: Id(2)] string Response,
    [property: Id(3)] long Revision);

[GenerateSerializer]
[Alias(nameof(ConversationTurn))]
public sealed record ConversationTurn(
    [property: Id(0)] ConversationTurnId TurnId,
    [property: Id(1)] ConversationRole Role,
    [property: Id(2)] string Text,
    [property: Id(3)] string Response);

[GenerateSerializer]
[Alias(nameof(ConversationSnapshot))]
public sealed record ConversationSnapshot(
    [property: Id(0)] IReadOnlyList<ConversationTurn> Turns,
    [property: Id(1)] long Revision);

[Alias(nameof(IConversationNeuron))]
public interface IConversationNeuron : INeuron
{
    [Alias(nameof(SubmitTurnAsync))]
    Task<ConversationTurnResult> SubmitTurnAsync(ConversationTurnRequest request);

    [Alias(nameof(ReadAsync))]
    Task<ConversationSnapshot> ReadAsync();
}
