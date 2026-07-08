using System.Text;
using System.Text.RegularExpressions;
using DigitalBrain.Core;

namespace DigitalBrain.Ino;

public static partial class InoExplanationFormatter
{
    public static bool IsExplanationQuestion(string prompt)
    {
        var p = prompt.ToLowerInvariant();
        return p.Contains("why did you do that") ||
               p.Contains("explain last action") ||
               p.Contains("explain correlation") ||
               p.Contains("why did that happen");
    }

    public static string? TryExtractCorrelationId(string prompt)
    {
        var match = CorrelationRegex().Match(prompt);
        return match.Success ? match.Groups["id"].Value : null;
    }

    public static string? ResolveLastCorrelationId(IEnumerable<Synapse> outgoing)
    {
        var lastResponse = outgoing
            .OfType<InoResponse>()
            .Where(response => !IsExplanationQuestion(response.Prompt))
            .LastOrDefault();

        if (lastResponse is not null)
        {
            return lastResponse.CorrelationId ?? lastResponse.SynapseId;
        }

        var lastAction = outgoing
            .Where(synapse => synapse is not ContextPacketSelected)
            .LastOrDefault();

        return lastAction?.CorrelationId ?? lastAction?.SynapseId;
    }

    public static string Format(string correlationId, IReadOnlyList<Synapse> lineage)
    {
        if (lineage.Count == 0)
        {
            return $"I do not have enough lineage for correlation '{correlationId}'. No journaled synapses matched that id.";
        }

        var ordered = lineage
            .OrderBy(synapse => synapse.Timestamp)
            .DistinctBy(synapse => synapse.SynapseId)
            .ToList();

        var request = ordered.OfType<InoRequest>().FirstOrDefault();
        var response = ordered.OfType<InoResponse>().LastOrDefault();
        var packet = ordered.OfType<ContextPacketSelected>().LastOrDefault();

        var builder = new StringBuilder();
        builder.AppendLine($"Correlation: {correlationId}");
        if (request is not null)
        {
            builder.AppendLine($"User request: {Trim(request.Prompt, 180)}");
        }
        else
        {
            builder.AppendLine("User request: missing from this lineage.");
        }

        var synapseTypes = ordered
            .Select(synapse => synapse.Type)
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        builder.AppendLine("Journaled path: " + string.Join(" -> ", synapseTypes));

        if (packet is not null)
        {
            var evidence = packet.Evidence.Take(6).Select(e => $"{e.EvidenceId}:{e.SourceKind}/{e.TrustLevel}");
            builder.AppendLine("Selected context evidence: " + string.Join(", ", evidence));
        }
        else
        {
            builder.AppendLine("Selected context evidence: none recorded for this action.");
        }

        if (response is not null)
        {
            builder.AppendLine($"Result: {Trim(response.Response, 220)}");
        }
        else
        {
            builder.AppendLine("Result: missing response in this lineage.");
        }

        builder.AppendLine($"Lineage entries: {ordered.Count}");
        return builder.ToString().Trim();
    }

    private static string Trim(string value, int max)
    {
        var text = SecretText.Redact(Regex.Replace(value.Trim(), @"\s+", " "));
        return text.Length <= max ? text : text[..(max - 3)] + "...";
    }

    [GeneratedRegex(@"correlation\s+(?<id>[a-zA-Z0-9:_\-\.]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CorrelationRegex();
}
