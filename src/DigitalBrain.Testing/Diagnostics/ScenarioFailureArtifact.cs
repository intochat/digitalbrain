using System.Text;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Testing;

public sealed class ScenarioFailureArtifact
{
    internal const int MaxStages = 64;
    internal const int MaxFaultDescriptionLength = 256;
    internal const int MaxFaultDescriptions = 16;

    public required OwnerId Owner { get; init; }

    public required IReadOnlyList<string> Stages { get; init; }

    public required int ArmedFaultCount { get; init; }

    public required IReadOnlyList<string> ArmedFaultDescriptions { get; init; }

    public required DateTimeOffset ClockUtc { get; init; }

    public string? Message { get; init; }

    public override string ToString()
    {
        var text = new StringBuilder(256);
        text.Append("Scenario failure artifact");
        text.AppendLine();
        text.Append("  Owner: ").Append(Owner.Value);
        text.AppendLine();
        text.Append("  ClockUtc: ").Append(ClockUtc.ToString("O"));
        text.AppendLine();
        text.Append("  Stages: ").Append(Stages.Count == 0 ? "(none)" : string.Join(" → ", Stages));
        text.AppendLine();
        text.Append("  ArmedFaults (").Append(ArmedFaultCount).Append("): ");

        if (ArmedFaultDescriptions.Count == 0)
        {
            text.Append("(none)");
        }
        else
        {
            text.Append(string.Join("; ", ArmedFaultDescriptions));
        }

        if (!string.IsNullOrWhiteSpace(Message))
        {
            text.AppendLine();
            text.Append("  Message: ").Append(Message);
        }

        return text.ToString();
    }
}
