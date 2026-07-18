namespace Ino.Core;

[GenerateSerializer]
public readonly record struct NeuronId([property: Id(0)] string Value)
{
    public override string ToString() => Value;

    public static NeuronId From(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("NeuronId cannot be empty.", nameof(value));
        return new NeuronId(value);
    }
}
