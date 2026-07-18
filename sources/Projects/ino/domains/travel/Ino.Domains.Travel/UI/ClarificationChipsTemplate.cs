using System.Text;
using System.Text.Json;

namespace Ino.Domains.Travel.UI;

/// <summary>
/// Builds the RFW payload for an <c>AskClarification</c> chip row.
/// Renders the prompt text plus one tappable chip per suggestion.
///
/// Event contract: a chip tap emits an RFW event named
/// <c>ino:provide-clarification</c> carrying <c>{ field, value }</c>.
/// The Flutter chat-screen handler (Slice 6) translates that into a
/// <c>FireSynapse</c> gRPC call with verb
/// <c>ino.core.provide-clarification</c>; the gateway reconstructs a
/// <see cref="Ino.Core.ProvideClarification"/> synapse keyed by the same
/// correlation_id so it lands on the same TripPlannerNeuron activation.
///
/// Description bytes are LF-only — the Dart RFW parser rejects CRLF.
/// </summary>
public static class ClarificationChipsTemplate
{
    public const string EventName = "ino:provide-clarification";

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Render the chip row. <paramref name="prompt"/> is the question shown
    /// above the chips; <paramref name="field"/> is the slot id sent back
    /// when the user picks; <paramref name="suggestions"/> become the chips.
    /// </summary>
    public static (byte[] Description, byte[] Data) Build(
        string prompt,
        string field,
        IReadOnlyList<string> suggestions)
    {
        var description = BuildDescription(suggestions.Count);
        var payload = new
        {
            prompt,
            field,
            suggestions = suggestions.ToArray(),
        };
        var data = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        return (Encoding.UTF8.GetBytes(description), data);
    }

    static string BuildDescription(int chipCount)
    {
        var sb = new StringBuilder();
        sb.Append("import core.widgets;\n");
        sb.Append("widget root = Column(\n");
        sb.Append("  crossAxisAlignment: \"start\",\n");
        sb.Append("  children: [\n");
        sb.Append("    Text(text: data.prompt),\n");
        sb.Append("    SizedBox(height: 12.0),\n");
        sb.Append("    Wrap(\n");
        sb.Append("      spacing: 8.0,\n");
        sb.Append("      runSpacing: 8.0,\n");
        sb.Append("      children: [\n");
        for (var i = 0; i < chipCount; i++)
        {
            sb.Append("        GestureDetector(\n");
            sb.Append($"          onTap: event \"{EventName}\" {{ field: data.field, value: data.suggestions.{i} }},\n");
            sb.Append("          child: Padding(\n");
            sb.Append("            padding: [12.0, 8.0, 12.0, 8.0],\n");
            sb.Append($"            child: Text(text: data.suggestions.{i})\n");
            sb.Append("          )\n");
            sb.Append("        )");
            if (i < chipCount - 1) sb.Append(',');
            sb.Append('\n');
        }
        sb.Append("      ]\n");
        sb.Append("    )\n");
        sb.Append("  ]\n");
        sb.Append(");\n");
        return sb.ToString();
    }
}
