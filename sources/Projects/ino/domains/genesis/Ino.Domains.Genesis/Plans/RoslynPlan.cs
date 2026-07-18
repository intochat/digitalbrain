using Ino.Core;
using Ino.Core.Hosting;
using Ino.Domains.Genesis.Compilation;
using Ino.Domains.Genesis.Contracts;
using Microsoft.Extensions.Logging;
using Orleans;

namespace Ino.Domains.Genesis.Plans;

/// <summary>
/// Shared <see cref="INeuronPlan"/> shell for every dynamic neuron
/// the L1 loop registers. Cortex resolves this single grain class for any
/// neuron whose <see cref="INeuronDefinition.PlanType"/> is
/// <see cref="IRoslynPlan"/>; <see cref="ExecuteAsync"/> looks up the
/// registered script body for <see cref="NeuronPlanContext.NeuronId"/>
/// and runs it via <see cref="PlanCompiler.ExecuteAsync"/>.
///
/// Doubles as the canonical handler (<see cref="INeuron{TSynapse}"/>) for
/// <see cref="DynamicNeuronTrigger"/>, so the canonical-handler gate
/// in <c>CortexNeuron.TryRouteToAsync</c> resolves cleanly. The
/// <see cref="HandleAsync"/> path is a no-op fallback — Cortex always
/// dispatches via <see cref="INeuronPlan.ExecuteAsync"/> when the
/// neuron declares a <see cref="INeuronDefinition.PlanType"/>; the
/// canonical entry only exists to satisfy Discovery's invariant that
/// every routed neuron has an installed canonical handler.
/// </summary>
public sealed class RoslynPlan(
    IGrainFactory grainFactory,
    ILogger<RoslynPlan> log)
    : Grain, IRoslynPlan, INeuron<DynamicNeuronTrigger>
{
    public async Task<NeuronResult> ExecuteAsync(NeuronPlanContext input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var registry = grainFactory.GetGrain<INeuronRegistry>(0);
        var body = await registry.GetScriptBodyAsync(input.NeuronId.Value, ct);
        if (body is null)
        {
            log.LogWarning(
                "RoslynPlan: no script body registered for {NeuronId} — falling through",
                input.NeuronId);
            return NeuronResult.Ok($"I don't have a handler for '{input.NeuronId}' yet.");
        }

        var globals = new RoslynPlanGlobals
        {
            Prompt = input.Prompt,
            NeuronId = input.NeuronId.Value,
            UserId = input.Caller.UserId ?? string.Empty,
            CorrelationId = input.Caller.CorrelationId.Value,
        };

        try
        {
            var result = await PlanCompiler.ExecuteAsync(body, globals, ct);
            log.LogDebug(
                "RoslynPlan: executed {NeuronId} for user {UserId} (success={Success})",
                input.NeuronId, globals.UserId, result.Success);
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Script-level exceptions should not crash the routing hop.
            // Surface a friendly message and let the inspector pick up the
            // detailed failure off the log.
            log.LogError(ex,
                "RoslynPlan: script execution failed for {NeuronId}",
                input.NeuronId);
            return NeuronResult.Ok(
                $"I tried to handle '{input.NeuronId}' but the script errored: {ex.Message}");
        }
    }

    public Task<NeuronResult> HandleAsync(DynamicNeuronTrigger synapse, NeuronContext ctx, CancellationToken ct)
    {
        // Direct fire path — currently unreachable. Cortex always uses
        // PlanType dispatch for dynamic neurons. Kept as a safety net
        // so a misrouted Fire<DynamicNeuronTrigger> doesn't blow up.
        log.LogDebug(
            "RoslynPlan.HandleAsync invoked directly for neuron {NeuronId} — unexpected path",
            synapse.NeuronId);
        var input = new NeuronPlanContext(
            synapse.Prompt,
            ctx,
            NeuronId.From(synapse.NeuronId));
        return ExecuteAsync(input, ct);
    }
}
