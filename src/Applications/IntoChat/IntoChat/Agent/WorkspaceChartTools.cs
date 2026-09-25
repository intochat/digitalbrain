using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.AI.Agents;
using Microsoft.Extensions.AI;

namespace IntoChat.Agent;

// Turns grouped, already-read data into a bounded workspace chart artifact.
internal sealed class WorkspaceChartTools : IAgentToolFactory
{
    public IReadOnlyList<AIFunction> Create(Func<AgentToolContext> context)
    {
        object Render(
            [Description("Short chart title.")] string title,
            [Description("Chart type: pie, bar, or line.")] string chartKind,
            [Description("Category labels from the grouped query result, in the same order as values.")] IReadOnlyList<string> labels,
            [Description("Numeric values from the grouped query result, in the same order as labels.")] IReadOnlyList<double> values)
        {
            if (string.IsNullOrWhiteSpace(title) || title.Length > 200)
            { return Failure("Title must contain 1–200 characters."); }
            var kind = chartKind.Trim().ToLowerInvariant();
            if (kind is not ("pie" or "bar" or "line"))
            { return Failure("Chart type must be pie, bar, or line."); }
            if (labels.Count is < 1 or > 256 || labels.Count != values.Count
                || labels.Any(label => string.IsNullOrWhiteSpace(label) || label.Length > 200)
                || values.Any(value => !double.IsFinite(value) || (kind == "pie" && value < 0)))
            { return Failure("Chart needs 1–256 matching labels and finite values; pie values must be nonnegative."); }
            var points = labels.Zip(values, (label, value) => new ChartDatum(label.Trim(), value)).ToArray();
            if (kind == "pie" && points.Length > 12)
            {
                var sorted = points.OrderByDescending(point => point.Value)
                    .ThenBy(point => point.Label, StringComparer.Ordinal).ToArray();
                points = [.. sorted.Take(11), new ChartDatum("Other", sorted.Skip(11).Sum(point => point.Value))];
            }
            var trusted = context();
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(new[] { trusted.ScopeId, trusted.RunId, trusted.CallId })))).ToLowerInvariant();
            return new
            {
                kind = "chart",
                id = "chart-" + hash,
                title = title.Trim(),
                chartKind = kind,
                points = points.Select(point => new { label = point.Label, value = point.Value }).ToArray(),
            };
        }
        return [AIFunctionFactory.Create(Render, "render_chart",
            "Open a bar, line, or pie chart in the workspace from real grouped labels and values. For many pie categories, the largest 11 plus Other are displayed. If isError=true, repair the data and retry.")];
    }

    private static object Failure(string message) => new { isError = true, message };
    private sealed record ChartDatum(string Label, double Value);
}
