using System.Text.Json.Serialization;

namespace DigitalBrain.Product.Approvals;

public sealed record ApprovalChange
{
    public ApprovalChange(string field, string? before, string after)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        ArgumentException.ThrowIfNullOrWhiteSpace(after);
        Field = field.Trim();
        Before = before;
        After = after;
    }

    public string Field { get; }

    public string? Before { get; }

    [JsonPropertyName("proposedValue")]
    public string After { get; }

    [JsonIgnore]
    public string ProposedValue => After;
}
