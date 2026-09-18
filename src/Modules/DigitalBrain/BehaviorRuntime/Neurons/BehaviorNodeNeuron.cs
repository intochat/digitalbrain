using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Behavior;
using DigitalBrain.Abstractions.Signals;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;

namespace DigitalBrain.Core.Behavior;

[GrainType("behavior-node")]
#pragma warning disable CS9107
internal sealed class BehaviorNodeNeuron(NeuronRuntime runtime,
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<SnapshotEnvelope<BehaviorNodeRuntime>> state)
    : Neuron<BehaviorNodeRuntime>(runtime, state), IBehaviorNode
#pragma warning restore CS9107
{
    internal const string ValueType = "Value";

    public Task Configure(string behaviorId, long version, BehaviorNode node, string trigger, IReadOnlyList<string> predecessors)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(behaviorId);
        ArgumentNullException.ThrowIfNull(node);
        ArgumentException.ThrowIfNullOrWhiteSpace(trigger);
        state.State = new SnapshotEnvelope<BehaviorNodeRuntime>(
            new(behaviorId, version, node, trigger, null, "Idle", "null", null, "null", [.. predecessors], "{}"), null, []);
        return state.WriteStateAsync();
    }

    public Task<BehaviorNodeRuntime?> Read() => Task.FromResult(State);

    protected override async Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        var current = State ?? throw new InvalidOperationException("Configure this node before firing it.");
        if (!current.Node.Kind.Equals("input", StringComparison.OrdinalIgnoreCase)
            && delivery.Signal.Type != ValueType)
        {
            return;
        }

        var envelope = GraphEnvelope.Read(delivery.Signal.Body);
        var runId = envelope.RunId ?? delivery.CorrelationId.ToString();
        var outputs = new Dictionary<string, JsonElement>(envelope.Outputs, StringComparer.Ordinal);
        var input = envelope.Input.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? GraphEnvelope.Parse(delivery.Signal.Body)
            : envelope.Input;
        var predecessors = current.Predecessors ?? [];
        var incoming = current.RunId == runId ? GraphEnvelope.Map(current.IncomingJson) : new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (envelope.From is { Length: > 0 })
        {
            incoming[envelope.From] = envelope.Value.Clone();
        }

        foreach (var pair in incoming)
        {
            outputs[pair.Key] = pair.Value.Clone();
        }

        current = current with { RunId = runId, IncomingJson = GraphEnvelope.WriteMap(incoming), InputJson = input.GetRawText() };
        if (predecessors.Count > 0 && predecessors.Any(id => !incoming.ContainsKey(id)))
        {
            await SaveAsync(current with { Status = "Running" }, cancellationToken).ConfigureAwait(true);
            return;
        }

        var value = current.Node.Kind.Equals("input", StringComparison.OrdinalIgnoreCase) ? input
            : incoming.Count == 1 ? incoming.Values.First()
            : incoming.Count > 1 ? JsonSerializer.SerializeToElement(incoming)
            : envelope.Value;
        var status = "Completed";
        var output = BehaviorWire.Null;
        string? error = null;
        try
        {
            if (current.Node.Kind.ToLowerInvariant() is "call" or "agent" or "code")
            {
                var executor = ServiceProvider.GetRequiredService<IBehaviorNodeExecutor>();
                output = await executor.ExecuteAsync(new(current.BehaviorId, runId, current.Version,
                    current.Node with { Kind = current.Node.Kind.ToLowerInvariant() }, input, value, outputs),
                    cancellationToken).ConfigureAwait(true);
            }
            else
            {
                var result = BehaviorBuiltins.Execute(current.Node, input, value, outputs);
                output = result.Output;
                if (result.Stop)
                {
                    status = "Filtered";
                }
            }

            output = BehaviorExpressions.Clone(output);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception failure)
        {
            status = "Failed";
            error = failure.Message.Length > 2000 ? failure.Message[..2000] : failure.Message;
        }

        var next = current with
        {
            Status = status,
            OutputJson = output.GetRawText(),
            Error = error,
        };

        if (status == "Completed")
        {
            outputs[current.Node.Id] = output;
            Announce(Signal.Create(ValueType, GraphEnvelope.Write(runId, input, output, outputs, current.Node.Id)));
        }

        await SaveAsync(next, cancellationToken).ConfigureAwait(true);

        if (current.Node.Kind.Equals("output", StringComparison.OrdinalIgnoreCase)
            || status is "Failed" or "Cancelled" or "Filtered")
        {
            await GrainFactory.GetGrain<IBehavior>(current.BehaviorId)
                .RecordRun(Snapshot(next, input)).ConfigureAwait(true);
        }

        if (status is "Completed" or "Filtered" && current.Node.Kind.Equals("output", StringComparison.OrdinalIgnoreCase)
            && ServiceProvider.GetService<IBehaviorRunLifecycle>() is { } lifecycle)
        {
            var definition = await GrainFactory.GetGrain<IBehavior>(current.BehaviorId).Read().ConfigureAwait(true);
            if (definition.Definition is { } graph)
            {
                await lifecycle.FinishedAsync(graph, Snapshot(next, input), cancellationToken).ConfigureAwait(true);
            }
        }
    }

    internal static BehaviorRunSnapshot Snapshot(BehaviorNodeRuntime node, JsonElement input)
        => new(node.RunId ?? "", node.BehaviorId, node.Version, node.Status, input,
            GraphEnvelope.Parse(node.OutputJson), [], node.Error, DateTimeOffset.UtcNow,
            node.Status == "Running" ? null : DateTimeOffset.UtcNow);
}

internal static class GraphEnvelope
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static (string? RunId, string? From, JsonElement Input, JsonElement Value, Dictionary<string, JsonElement> Outputs) Read(string body)
    {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("value", out _))
        {
            return (
                root.TryGetProperty("runId", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null,
                root.TryGetProperty("from", out var from) && from.ValueKind == JsonValueKind.String ? from.GetString() : null,
                root.TryGetProperty("input", out var input) ? input.Clone() : Parse("{}"),
                root.GetProperty("value").Clone(),
                Map(root.TryGetProperty("outputs", out var bag) ? bag.GetRawText() : "{}"));
        }

        var value = root.Clone();
        return (null, null, value, value, []);
    }

    internal static string Write(string runId, JsonElement input, JsonElement value, IReadOnlyDictionary<string, JsonElement> outputs, string from)
        => JsonSerializer.Serialize(new { runId, from, input, value, outputs }, Json);

    internal static Dictionary<string, JsonElement> Map(string json)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (var property in document.RootElement.EnumerateObject())
        {
            result[property.Name] = property.Value.Clone();
        }

        return result;
    }

    internal static string WriteMap(IReadOnlyDictionary<string, JsonElement> values)
        => JsonSerializer.Serialize(values, Json);

    internal static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "null" : json);
        return document.RootElement.Clone();
    }
}
