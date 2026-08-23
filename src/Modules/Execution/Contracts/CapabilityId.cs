using System.Text.Json.Serialization;

namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.capability-id")]
public readonly record struct CapabilityId
{
    [JsonConstructor]
    public CapabilityId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A capability id is required.", nameof(value));
        }

        Value = value.Trim();
    }

    [Id(0)]
    public string Value { get; }

    public static CapabilityId Parse(string value) => new(value);

    public override string ToString() => Value;
}
