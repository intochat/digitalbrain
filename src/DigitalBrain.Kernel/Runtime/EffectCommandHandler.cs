using DigitalBrain.Core.Runtime;
using Orleans;

namespace DigitalBrain.Kernel.Runtime;

/// <summary>Bridges an application command to the durable Orleans effect worker.</summary>
public sealed class EffectCommandHandler(IGrainFactory grainFactory) : ICommandHandler
{
    public bool CanHandle(string commandType) => commandType.StartsWith("effect.", StringComparison.Ordinal);

    public async Task<CommandExecutionResult> ExecuteAsync(CommandEnvelope command, CancellationToken cancellationToken = default)
    {
        if (!command.Payload.TryGetProperty("aggregateId", out var aggregate) || aggregate.ValueKind != System.Text.Json.JsonValueKind.String ||
            !command.Payload.TryGetProperty("effectId", out var effect) || effect.ValueKind != System.Text.Json.JsonValueKind.String)
            return new(WorkflowState.Failed, "effect-reference-invalid");

        var owner = command.Payload.TryGetProperty("leaseOwner", out var lease) && lease.ValueKind == System.Text.Json.JsonValueKind.String
            ? lease.GetString() : "v2-command-dispatcher";
        var worker = grainFactory.GetGrain<IEffectWorkerGrain>(aggregate.GetString()!);
        var transition = await worker.ExecuteAsync(aggregate.GetString()!, effect.GetString()!, owner!, TimeSpan.FromMinutes(2));
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

public sealed class OrleansEffectWorkerPort(IGrainFactory grainFactory) : IEffectWorkerPort
{
    public Task<EffectTransitionRecord> ExecuteAsync(string aggregateId, string effectId, string leaseOwner, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return grainFactory.GetGrain<IEffectWorkerGrain>(aggregateId)
            .ExecuteAsync(aggregateId, effectId, leaseOwner, leaseDuration);
    }
}
