namespace DigitalBrain.Abstractions.Bundles;

// Substrate-level identity of an installable bundle (e.g. "digitalbrain/ino" or "{publisher}/{name}").
// Distribution refines this with publisher/name structure in the marketplace context.
[GenerateSerializer]
public readonly record struct BundleId
{
    public BundleId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Bundle id must be a non-empty value.", nameof(value));
        }

        Value = value.Trim();
    }

    [Id(0)]
    public string Value { get; }

    public override string ToString() => Value;
}
