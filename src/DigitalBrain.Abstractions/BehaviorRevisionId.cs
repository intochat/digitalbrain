namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.behavior-revision-id")]
public readonly record struct BehaviorRevisionId
{
    public BehaviorRevisionId(string value)
    {
        if (value is null || value.Length != 64 || value.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
        {
            throw new FormatException("A behavior revision id must be a 64-character lowercase hexadecimal SHA-256 digest.");
        }

        Value = value;
    }

    [Id(0)]
    public string Value { get; }

    public static BehaviorRevisionId Parse(string value) => new(value);

    public override string ToString() => Value;
}
