using DigitalBrain.Neurons;

namespace DigitalBrain.Capabilities.Compute;

public enum FundingKind
{
    Local = 0,
    OwnConnection = 1,
    Integration = 2,
    Circle = 3,
}

// The selected connection determines funding; these values never establish caller authority.
public readonly record struct Funding(
    FundingKind Kind,
    string? CircleKey = null)
{
    public static Funding Local => new(FundingKind.Local);
    public static Funding OwnConnection => new(FundingKind.OwnConnection);
    public static Funding Integration => new(FundingKind.Integration);

    public static Funding Circle(string circleKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(circleKey);
        return new(FundingKind.Circle, circleKey);
    }
}

public sealed record CostSubject(
    string? Synapse = null,
    string? App = null,
    string? Neuron = null);
