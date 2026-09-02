using System.Text.Json;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Execution;

public sealed class ExplainabilityHandler(IGrainFactory grains) : ICapabilityHandler
{
    public CapabilityId Id { get; } = CapabilityId.Parse("explain.why");

    public async Task<ContextDelta> InvokeAsync(
        ExecutionId executionId,
        OwnerId owner,
        string requestJson,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var context = grains.GetGrain<IExecutionContext>(
            EntityId.For<IExecutionContext>(owner, executionId.ToString()).ToGrainId());
        var contextState = await context.Read()
            .WaitAsync(cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        var contextPaths = new List<string>(contextState?.Slots.Count ?? 0);
        if (contextState?.Slots is { } slots)
        {
            for (var i = 0; i < slots.Count; i++)
            {
                contextPaths.Add(slots[i].Path.Value);
            }
        }

        var preferences = grains.GetGrain<IPreferenceStore>(
            EntityId.For<IPreferenceStore>(owner, IPreferenceStore.DefaultInstanceName).ToGrainId());
        var rules = await preferences.ListRules()
            .WaitAsync(cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        var preferenceRules = new List<string>(rules.Count);
        for (var i = 0; i < rules.Count; i++)
        {
            preferenceRules.Add($"[{rules[i].Category}] {rules[i].RuleText}");
        }

        var payload = JsonSerializer.Serialize(new
        {
            executionId = executionId.ToString(),
            workload = ReadWorkloadName(requestJson),
            contextPaths,
            preferenceRules,
            note = "Based on active execution context and preferences.",
        });

        return new ContextDelta(
            new ContextPath("explain.trace"),
            SchemaHash: "explain.trace.v1",
            PayloadJson: payload,
            BlobRef: null);
    }

    private static string ReadWorkloadName(string requestJson)
    {
        if (string.IsNullOrWhiteSpace(requestJson))
        {
            return "unknown";
        }

        try
        {
            using var document = JsonDocument.Parse(requestJson);
            if (document.RootElement.TryGetProperty("workload", out var workload)
                && workload.ValueKind == JsonValueKind.String
                && workload.GetString() is { Length: > 0 } name)
            {
                return name;
            }
        }
        catch (JsonException)
        {
        }

        return "unknown";
    }
}
