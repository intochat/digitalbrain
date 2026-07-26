namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.behavior-id")]
public readonly record struct BehaviorId
{
    public BehaviorId(string value) => Value = Validate(value);

    [Id(0)]
    public string Value { get; }

    public static BehaviorId Parse(string value) => new(value);

    public override string ToString() => Value;

    private static string Validate(string value)
    {
        if (value is null || value.Length is < 3 or > 128)
        {
            throw new FormatException("A behavior id must contain 3 to 128 lowercase ASCII characters.");
        }

        var labelStart = 0;

        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            var isAlphanumeric = character is >= 'a' and <= 'z' or >= '0' and <= '9';

            if (character == '.')
            {
                if (index == labelStart || !IsAlphanumeric(value[index - 1]))
                {
                    throw new FormatException("A behavior id must use dot-separated lowercase ASCII labels.");
                }

                labelStart = index + 1;
                continue;
            }

            if (!isAlphanumeric && (character != '-' || index == labelStart))
            {
                throw new FormatException("A behavior id must use dot-separated lowercase ASCII labels.");
            }
        }

        if (labelStart == value.Length || !IsAlphanumeric(value[^1]))
        {
            throw new FormatException("A behavior id must use dot-separated lowercase ASCII labels.");
        }

        return value;
    }

    private static bool IsAlphanumeric(char value)
        => value is >= 'a' and <= 'z' or >= '0' and <= '9';
}
