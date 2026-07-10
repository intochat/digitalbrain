using DigitalBrain.Core.V2;
using Orleans;

namespace DigitalBrain.Kernel.V2;

/// <summary>Bridges an application command to the durable Orleans effect worker.</summary>
public sealed class V2EffectCommandHandler(IGrainFactory grainFactory) : IV2CommandHandler
{
    public bool CanHandle(string commandType) => commandType.StartsWith("effect.", StringComparison.Ordinal);

    public async Task<V2CommandExecutionResult> ExecuteAsync(V2CommandEnvelope command, CancellationToken cancellationToken = default)
    {
        if (!command.Payload.TryGetProperty("aggregateId", out var aggregate) || aggregate.ValueKind != System.Text.Json.JsonValueKind.String ||
            !command.Payload.TryGetProperty("effectId", out var effect) || effect.ValueKind != System.Text.Json.JsonValueKind.String)
            return new(WorkflowState.Failed, "effect-reference-invalid");

        var owner = command.Payload.TryGetProperty("leaseOwner", out var lease) && lease.ValueKind == System.Text.Json.JsonValueKind.String
            ? lease.GetString() : "v2-command-dispatcher";
        var worker = grainFactory.GetGrain<IV2EffectWorkerGrain>(aggregate.GetString()!);
        var transition = await worker.ExecuteAsync(aggregate.GetString()!, effect.GetString()!, owner!, TimeSpan.FromMinutes(2));
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

public sealed class OrleansV2EffectWorkerPort(IGrainFactory grainFactory) : IV2EffectWorkerPort
{
    public Task<EffectTransitionRecord> ExecuteAsync(string aggregateId, string effectId, string leaseOwner, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return grainFactory.GetGrain<IV2EffectWorkerGrain>(aggregateId)
            .ExecuteAsync(aggregateId, effectId, leaseOwner, leaseDuration);
    }
}
