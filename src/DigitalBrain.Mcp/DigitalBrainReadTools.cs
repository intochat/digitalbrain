using System.ComponentModel;
using System.Text.Json;
using DigitalBrain.Core;
using ModelContextProtocol.Server;

namespace DigitalBrain.Mcp;

using DigitalBrain.Ui.Contracts;
using DigitalBrain.Ui.Runtime;

// Read-only DigitalBrain MCP tools: observe cluster state without side effects. Safe to expose over the
// kernel's direct-run HTTP transport. Mutation tools live in DigitalBrainMutationTools.
[McpServerToolType]
public sealed class DigitalBrainReadTools(IGrainFactory grains) : DigitalBrainToolsBase(grains)
{
    [McpServerTool(Name = "ping_digitalbrain", ReadOnly = true), Description("Simple ping tool to verify MCP connection to DigitalBrain server works. Always returns success.")]
    public static string PingDigitalBrain() => "DigitalBrain MCP connected successfully. Cluster interaction tools ready when kernel is running.";

    [McpServerTool(Name = "get_timeline", ReadOnly = true), Description("Get recent timeline (synapses) for a neuron. Useful to see history, responses, and automation activity.")]
    public async Task<string> GetTimeline(
        [Description("Neuron ID to query, e.g. 'ino-main', 'llm-main', 'status-main'")] string neuronId,
        [Description("Max number of recent entries")] int maxEntries = 10)
    {
        var neuron = ResolveNeuron(neuronId);
        var timeline = await neuron.GetTimelineAsync();
        var lines = timeline.TakeLast(maxEntries).Select(s => $"{s.Timestamp:HH:mm:ss} | {s.Type}: {s}");
        return string.Join("\n", lines);
    }

    [McpServerTool(Name = "get_causal_lineage", ReadOnly = true), Description("Read-only causal lineage lookup for a neuron by CorrelationId or SynapseId. Returns sanitized structured JSON from journals.")]
    public async Task<string> GetCausalLineage(
        [Description("Neuron ID to query, e.g. 'ino-main', 'context-main', 'automation-main'")] string neuronId,
        [Description("CorrelationId or SynapseId to inspect")] string correlationId,
        [Description("Max number of recent lineage entries to include")] int maxEntries = 25,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            return JsonSerializer.Serialize(new
            {
                neuronId,
                error = "correlationId is required"
            }, SurfaceJsonOptions);
        }

        var neuron = ResolveNeuron(neuronId);
        var lineage = await neuron.GetCausalLineageAsync(correlationId, cancellationToken);
        var entries = lineage
            .OrderBy(s => s.Timestamp)
            .TakeLast(Math.Clamp(maxEntries, 1, 100))
            .Select(s => new
            {
                s.Type,
                s.SynapseId,
                s.CorrelationId,
                s.CausationId,
                Timestamp = s.Timestamp,
                Sender = s.Sender?.Value,
                Receiver = s.Receiver?.Value,
                Summary = SanitizeToolText(s.ToString() ?? string.Empty)
            })
            .ToArray();

        return JsonSerializer.Serialize(new
        {
            neuronId,
            correlationId,
            count = entries.Length,
            entries
        }, SurfaceJsonOptions);
    }

    [McpServerTool(Name = "get_workbench_surfaces", ReadOnly = true), Description("Return dynamic UiSurface JSON for the Flutter workbench, derived from task, graph, chart, and timeline journals. Pass comma-separated taskIds when the caller knows active kernel tasks.")]
    public async Task<string> GetWorkbenchSurfaces(
        [Description("Comma-separated kernel task ids to include, if known.")] string taskIds = "",
        [Description("Max graph/timeline events to include")] int maxEvents = 20)
    {
        var taskTimelines = new List<(string TaskId, IReadOnlyList<Synapse> Timeline)>();
        foreach (var taskId in SplitIds(taskIds))
        {
            var task = Grains.GetGrain<INeuron>(taskId);
            taskTimelines.Add((taskId, await task.GetTimelineAsync()));
        }

        var graphTimeline = await ResolveNeuron("cluster-vis").GetTimelineAsync();
        var chartTimeline = await Grains.GetGrain<IDataVisualizationNeuron>("chart-main").GetTimelineAsync();

        var timeline = taskTimelines
            .SelectMany(t => t.Timeline)
            .Concat(graphTimeline)
            .Concat(chartTimeline)
            .OrderBy(s => s.Timestamp)
            .TakeLast(maxEvents)
            .ToArray();

        var surfaces = UiSurfaceLiveData.BuildWorkbenchSurfaces(
            taskTimelines, graphTimeline, timeline, maxEvents, chartTimeline);

        return JsonSerializer.Serialize(surfaces, SurfaceJsonOptions);
    }

    private static string SanitizeToolText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = SensitiveText.Redact(value)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);

        return text.Length <= 500 ? text : text[..497] + "...";
    }
}
