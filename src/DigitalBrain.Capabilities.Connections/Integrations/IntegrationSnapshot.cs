using System.Text;

namespace DigitalBrain.Capabilities.Connections.Integrations;

public enum IntegrationStatus
{
    Unconfigured = 0,
    Partial = 1,
    Ready = 2,
}

public sealed record IntegrationSnapshot
{
    public string IntegrationId { get; init; } = "";
    public IntegrationStatus Status { get; init; }
    public string[] MissingFields { get; init; } = [];
    public Dictionary<string, string> Settings { get; init; } = [];
    public long Revision { get; init; }
}

// Field name to value. The values go to the vault immediately, and the record never prints them.
public sealed record ConfigureIntegration
{
    public Dictionary<string, string> Values { get; init; } = [];

#pragma warning disable CA1822
    private bool PrintMembers(StringBuilder builder) => builder.Append("Values = <redacted>") is not null;
#pragma warning restore CA1822
}

// Handed only to trusted platform code; never persisted.
public sealed record ReleasedIntegration
{
    public Dictionary<string, string> Values { get; init; } = [];
    public IntegrationSnapshot Snapshot { get; init; } = new();

#pragma warning disable CA1822
    private bool PrintMembers(StringBuilder builder) => builder.Append("Values = <redacted>") is not null;
#pragma warning restore CA1822
}
