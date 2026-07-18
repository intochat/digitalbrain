using System.Collections.Concurrent;
using System.Diagnostics;
using Ino.Core;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;

namespace Ino.Core.Hosting;

public sealed class FirePort(
    IGrainFactory grains,
    IDiscoveryClient discovery,
    ICapabilityEnforcer capabilityEnforcer,
    ActivitySource activitySource) : IFirePort
{
    public async Task<NeuronResult> Fire<T>(T synapse, NeuronContext caller, CancellationToken ct = default) where T : ISynapse
    {
        var target = await discovery.LookupCanonicalAsync(typeof(T), ct);
        if (target is null)
            return NeuronResult.Fail(new SynapseError(
                SynapseErrorCode.NoCanonicalHandler,
                $"No installed domain implements INeuron<{typeof(T).Name}>."));

        try
        {
            capabilityEnforcer.AssertCanFire(caller.Source, target);
        }
        catch (CapabilityDeniedException ex)
        {
            return NeuronResult.Fail(new SynapseError(
                SynapseErrorCode.CapabilityDenied, ex.Message, ex.Details));
        }

        using var span = activitySource.StartActivity(
            Telemetry.Spans.Fire(typeof(T)), ActivityKind.Producer);
        span?.SetTag(Telemetry.Tags.SynapseType, typeof(T).FullName);
        span?.SetTag(Telemetry.Tags.SourceDomain,
            caller.Source is Caller.FromDomain d ? d.Domain.Value : null);
        span?.SetTag(Telemetry.Tags.TargetDomain, target.Domain.Value);
        span?.SetTag(Telemetry.Tags.CorrelationId, caller.CorrelationId.Value);

        // Interface-only resolution: Discovery enforces a single canonical handler per
        // synapse type, so INeuron<T> resolves unambiguously without a class prefix.
        // Passing target.GrainType.FullName here hit a cold-boot race (Orleans' grain
        // class directory gossips lazily after silo join) and silently mismatches Orleans'
        // lowercased GrainType.Name anyway (see CLAUDE.md known-traps).
        var grain = grains.GetGrain<INeuron<T>>(primaryKey: caller.CorrelationId.Value);

        var childContext = DeriveChildContext(caller, target);
        var result = await grain.HandleAsync(synapse, childContext, ct);

        span?.SetTag(Telemetry.Tags.ResultSuccess, result.Success);
        if (!result.Success && result.Error is { } err)
            span?.SetTag(Telemetry.Tags.ErrorCode, err.Code.ToString());

        return result;
    }

    public async Task FireBroadcast<T>(T synapse, NeuronContext caller, CancellationToken ct = default) where T : ISynapse
    {
        var targets = await discovery.LookupReactiveAsync(typeof(T), ct);
        if (targets.Count == 0) return;

        using var span = activitySource.StartActivity(
            Telemetry.Spans.FireBroadcast(typeof(T)), ActivityKind.Producer);
        span?.SetTag(Telemetry.Tags.SynapseType, typeof(T).FullName);
        span?.SetTag(Telemetry.Tags.SourceDomain,
            caller.Source is Caller.FromDomain d ? d.Domain.Value : null);
        span?.SetTag(Telemetry.Tags.CorrelationId, caller.CorrelationId.Value);
        span?.SetTag(Telemetry.Tags.BroadcastTargetCount, targets.Count);

        var transportFailures = new ConcurrentBag<Exception>();
        var capabilityDenied = 0;
        var failedCount = 0;

        await Parallel.ForEachAsync(targets, ct, async (target, inner) =>
        {
            try
            {
                capabilityEnforcer.AssertCanFireBroadcast(caller.Source, target);
                // Interface-only resolution — see SystemFirePort.FireBroadcast for the
                // cold-boot-race + prefix-mismatch rationale. v0.1 has at most one
                // reactor per synapse type; plumb [GrainType] aliases post-v0.1.
                var grain = grains.GetGrain<IReactsTo<T>>(primaryKey: caller.CorrelationId.Value);
                await grain.ReactAsync(synapse, DeriveChildContext(caller, target), inner);
            }
            catch (CapabilityDeniedException ex)
            {
                Interlocked.Increment(ref capabilityDenied);
                Interlocked.Increment(ref failedCount);
                caller.Logger.LogWarning(ex,
                    "Capability denied for reactive listener {Target} on broadcast of {Synapse}",
                    target.GrainType.FullName, typeof(T).FullName);
            }
            catch (OrleansException ex)
            {
                Interlocked.Increment(ref failedCount);
                transportFailures.Add(ex);
                caller.Logger.LogWarning(ex,
                    "Orleans transport error on reactive listener {Target} for {Synapse}",
                    target.GrainType.FullName, typeof(T).FullName);
            }
        });

        span?.SetTag(Telemetry.Tags.BroadcastFailedCount, failedCount);
        span?.SetTag(Telemetry.Tags.BroadcastCapabilityDenied, capabilityDenied);
        span?.SetTag(Telemetry.Tags.BroadcastTransportFailures, transportFailures.Count);

        if (!transportFailures.IsEmpty)
            throw new AggregateException(
                $"{transportFailures.Count} of {targets.Count} reactive listeners failed with Orleans transport errors " +
                $"on broadcast of {typeof(T).FullName}",
                transportFailures);
    }

    private static NeuronContext DeriveChildContext(NeuronContext caller, CanonicalTarget target)
    {
        return caller with
        {
            SynapseId = SynapseId.New(),
            Source = new Caller.FromDomain(target.Domain),
            CurrentEventId = caller.CurrentEventId,
        };
    }

    private static NeuronContext DeriveChildContext(NeuronContext caller, ReactiveTarget target)
    {
        return caller with
        {
            SynapseId = SynapseId.New(),
            Source = new Caller.FromDomain(target.Domain),
        };
    }
}
