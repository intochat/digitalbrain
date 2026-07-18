using DigitalBrain.Core.Synapses;

namespace DigitalBrain.Abstractions.Communication;

[GenerateSerializer]
public readonly record struct ApprovedSummary
{
    public ApprovedSummary(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Approved summary must be non-empty.", nameof(value));
        }

        Value = value.Trim();
    }

    [Id(0)]
    public string Value { get; }

    public override string ToString() => Value;

    public static implicit operator ApprovedSummary(string value) => new(value);
}

[GenerateSerializer]
public record SummaryMessage([property: Id(0)] ApprovedSummary Summary) : Synapse;
