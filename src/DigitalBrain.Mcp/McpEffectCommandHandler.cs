using DigitalBrain.Core.Runtime;
using DigitalBrain.Kernel.Runtime;
using DigitalBrain.Kernel.Abstractions;

namespace DigitalBrain.Mcp;

/// <summary>HTTP MCP adapter for the durable effect worker; no arbitrary grain targets are accepted.</summary>
public sealed class McpEffectCommandHandler(IEffectWorkerPort workerPort) : ICommandHandler
{
    public bool CanHandle(string commandType) => commandType.StartsWith("effect.", StringComparison.Ordinal);

    public async Task<CommandExecutionResult> ExecuteAsync(CommandEnvelope command, CancellationToken cancellationToken = default)
    {
        if (!command.Payload.TryGetProperty("aggregateId", out var aggregate) || aggregate.ValueKind != System.Text.Json.JsonValueKind.String ||
            !command.Payload.TryGetProperty("effectId", out var effect) || effect.ValueKind != System.Text.Json.JsonValueKind.String)
            return new(WorkflowState.Failed, "effect-reference-invalid");

        var aggregateId = aggregate.GetString();
        if (!GrainIds.IsInScope(aggregateId, command.Context.TenantId, command.Context.WorkspaceId))
            return new(WorkflowState.Failed, "effect-scope-invalid");

        cancellationToken.ThrowIfCancellationRequested();
        var transition = await workerPort.ExecuteAsync(
            aggregateId, effect.GetString()!, "v2-mcp-dispatcher", TimeSpan.FromMinutes(2), cancellationToken);
        return transition.State switch
        {
            "Succeeded" => CommandExecutionResult.Success(),
            "OutcomeUnknown" => CommandExecutionResult.Unknown("effect-outcome-unknown"),
            "RetryScheduled" => new(WorkflowState.RetryScheduled, "effect-retry-scheduled"),
            "Cancelled" => new(WorkflowState.Cancelled, "effect-cancelled"),
            _ => new(WorkflowState.Failed, "effect-failed")
        };
    }
}
