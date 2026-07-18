namespace DigitalBrain.Abstractions.Bundles;

[GenerateSerializer]
public readonly record struct BundleVersion
{
    public BundleVersion(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Bundle version must be a non-empty value.", nameof(value));
        }

        Value = value.Trim();
    }

    [Id(0)]
    public string Value { get; }

    public override string ToString() => Value;
}
