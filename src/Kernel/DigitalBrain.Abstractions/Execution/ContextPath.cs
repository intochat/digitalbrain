using System.Text.Json.Serialization;

namespace DigitalBrain.Abstractions.Execution;

[GenerateSerializer]
[Alias("db.context-path")]
public readonly record struct ContextPath
{
    [JsonConstructor]
    public ContextPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A context path is required.", nameof(value));
        }

        Value = value.Trim().Trim('/');
    }

    [Id(0)]
    public string Value { get; }

    public override string ToString() => Value;
}
