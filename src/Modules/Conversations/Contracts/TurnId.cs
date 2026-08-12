namespace DigitalBrain.Conversations;

[GenerateSerializer]
[Alias("db.conversation.turn-id")]
public readonly record struct TurnId
{
    [Id(0)]
    public Guid Value { get; }

    public TurnId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("The turn id cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public static TurnId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("N");
}
