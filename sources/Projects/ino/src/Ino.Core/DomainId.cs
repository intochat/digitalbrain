namespace Ino.Core;

[GenerateSerializer]
public readonly record struct DomainId([property: Id(0)] string Value)
{
    public override string ToString() => Value;

    public static DomainId From(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("DomainId cannot be empty.", nameof(value));
        return new DomainId(value);
    }
}
