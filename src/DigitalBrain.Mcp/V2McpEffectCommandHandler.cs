using DigitalBrain.Core.V2;
using DigitalBrain.Kernel.V2;
using DigitalBrain.Kernel.Abstractions;

namespace DigitalBrain.Mcp;

/// <summary>HTTP MCP adapter for the durable V2 effect worker; no arbitrary grain targets are accepted.</summary>
public sealed class V2McpEffectCommandHandler(IV2EffectWorkerPort workerPort) : IV2CommandHandler
{
    public bool CanHandle(string commandType) => commandType.StartsWith("effect.", StringComparison.Ordinal);

    public async Task<V2CommandExecutionResult> ExecuteAsync(V2CommandEnvelope command, CancellationToken cancellationToken = default)
    {
        if (!command.Payload.TryGetProperty("aggregateId", out var aggregate) || aggregate.ValueKind != System.Text.Json.JsonValueKind.String ||
            !command.Payload.TryGetProperty("effectId", out var effect) || effect.ValueKind != System.Text.Json.JsonValueKind.String)
            return new(WorkflowState.Failed, "effect-reference-invalid");

        var aggregateId = aggregate.GetString();
        if (!V2GrainIds.IsInScope(aggregateId, command.Context.TenantId, command.Context.WorkspaceId))
            return new(WorkflowState.Failed, "effect-scope-invalid");

        cancellationToken.ThrowIfCancellationRequested();
        var transition = await workerPort.ExecuteAsync(
            aggregateId, effect.GetString()!, "v2-mcp-dispatcher", TimeSpan.FromMinutes(2), cancellationToken);
        return transition.State switch
        {
            "Succeeded" => V2CommandExecutionResult.Success(),
            "OutcomeUnknown" => V2CommandExecutionResult.Unknown("effect-outcome-unknown"),
            "RetryScheduled" => new(WorkflowState.RetryScheduled, "effect-retry-scheduled"),
            "Cancelled" => new(WorkflowState.Cancelled, "effect-cancelled"),
            _ => new(WorkflowState.Failed, "effect-failed")
        };
    }
}
