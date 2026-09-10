using System.Text.Json.Serialization;

namespace DigitalBrain.Abstractions.Identity;

[GenerateSerializer]
[Alias("db.v3.command-id")]
public readonly record struct CommandId
{
    [JsonConstructor]
    public CommandId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A command id cannot be empty.", nameof(value));
        }

        Value = value;
    }

    [Id(0)]
    public Guid Value { get; }

    public static CommandId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("n");

    public static bool TryParse(string? text, out CommandId id)
    {
        if (Guid.TryParse(text, out var value) && value != Guid.Empty)
        {
            id = new CommandId(value);
            return true;
        }

        id = default;
        return false;
    }
}
