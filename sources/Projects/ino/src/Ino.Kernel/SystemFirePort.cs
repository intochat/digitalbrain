using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Ino.Core;
using Ino.Core.Hosting;
using Ino.Core.Hosting.Llm;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;

namespace Ino.Kernel;

/// <summary>
/// IFirePort implementation that runs inside the system silo (where the gateway lives).
/// Resolves targets via IDiscoveryClient, then dispatches canonical fires to
/// <see cref="INeuron{T}"/> and reactive broadcasts to <see cref="IReactsTo{T}"/> grains.
///
/// Unlike <c>Ino.Domains.FirePort</c>, this port runs no capability enforcement: the
/// system silo is the kernel, and gateway-initiated fires carry <see cref="Caller.Ambient"/>
/// which the domains-side enforcer already kernel-bypasses. Keeping the enforcer off
/// the system path documents that the trust boundary lives at the silo edge, not here.
/// </summary>
public sealed class SystemFirePort(
    IGrainFactory grains,
    IDiscoveryClient discovery,
    ActivitySource activitySource,
    IInoEventBus events,
    ISynapseJournal journal,
    IReasoningProbe reasoningProbe) : IFirePort
{
    private long _sequence;

    public async Task<NeuronResult> Fire<T>(T synapse, NeuronContext caller, CancellationToken ct = default)
        where T : ISynapse
    {
        var target = await discovery.LookupCanonicalAsync(typeof(T), ct);
        if (target is null)
            return NeuronResult.Fail(new SynapseError(
                SynapseErrorCode.NoCanonicalHandler,
                $"No installed domain implements INeuron<{typeof(T).Name}>."));

        PublishEvent(caller, "SynapseFired", typeof(T).Name, target.GrainType.FullName);

        using var span = activitySource.StartActivity(
            Telemetry.Spans.Fire(typeof(T)), ActivityKind.Producer);
        span?.SetTag(Telemetry.Tags.SynapseType, typeof(T).FullName);
        span?.SetTag(Telemetry.Tags.SourceDomain,
            caller.Source is Caller.FromDomain d ? d.Domain.Value : null);
        span?.SetTag(Telemetry.Tags.TargetDomain, target.Domain.Value);
        span?.SetTag(Telemetry.Tags.CorrelationId, caller.CorrelationId.Value);

        // Interface-only resolution: with one canonical handler per synapse type
        // the grain type resolves unambiguously (see CLAUDE.md trap — Orleans matches
        // grainClassNamePrefix against GrainType.Name, not Type.FullName, so passing
        // the full name silently fails on prefix comparison).
        var grain = grains.GetGrain<INeuron<T>>(primaryKey: caller.CorrelationId.Value);

        var childContext = DeriveChildContext(caller, target);
        var result = await grain.HandleAsync(synapse, childContext, ct);

        span?.SetTag(Telemetry.Tags.ResultSuccess, result.Success);
        if (!result.Success && result.Error is { } err)
            span?.SetTag(Telemetry.Tags.ErrorCode, err.Code.ToString());

        return result;
    }

    public async Task FireBroadcast<T>(T synapse, NeuronContext caller, CancellationToken ct = default)
        where T : ISynapse
    {
        var targets = await discovery.LookupReactiveAsync(typeof(T), ct);
        if (targets.Count == 0) return;

        PublishEvent(caller, "SynapseBroadcast", typeof(T).Name, $"{targets.Count} targets");

        using var span = activitySource.StartActivity(
            Telemetry.Spans.FireBroadcast(typeof(T)), ActivityKind.Producer);
        span?.SetTag(Telemetry.Tags.SynapseType, typeof(T).FullName);
        span?.SetTag(Telemetry.Tags.SourceDomain,
            caller.Source is Caller.FromDomain d ? d.Domain.Value : null);
        span?.SetTag(Telemetry.Tags.CorrelationId, caller.CorrelationId.Value);
        span?.SetTag(Telemetry.Tags.BroadcastTargetCount, targets.Count);

        var transportFailures = new ConcurrentBag<Exception>();
        var failedCount = 0;

        await Parallel.ForEachAsync(targets, ct, async (target, inner) =>
        {
            try
            {
                // Interface-only resolution. Passing target.GrainType.FullName here hit a
                // cold-boot race: Orleans' grain-class directory gossips lazily after a
                // silo join, so prefix lookup threw "Could not find an implementation
                // matching prefix" for ~30s even on a Healthy cluster. And the prefix is
                // matched against Orleans' lowercased GrainType.Name anyway, not
                // Type.FullName (see CLAUDE.md known-traps).
                //
                // v0.1 ships with at most one IReactsTo<T> per synapse type, so ambiguity
                // doesn't surface. When a second reactor lands post-v0.1, plumb an
                // explicit [GrainType("bundle.neuron")] alias through ReactiveTarget and
                // pass it here as the prefix.
                var grain = grains.GetGrain<IReactsTo<T>>(primaryKey: caller.CorrelationId.Value);
                await grain.ReactAsync(synapse, DeriveChildContext(caller, target), inner);
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
        span?.SetTag(Telemetry.Tags.BroadcastTransportFailures, transportFailures.Count);

        if (!transportFailures.IsEmpty)
            throw new AggregateException(
                $"{transportFailures.Count} of {targets.Count} reactive listeners failed with Orleans transport errors " +
                $"on broadcast of {typeof(T).FullName}",
                transportFailures);
    }

    void PublishEvent(NeuronContext caller, string eventType, string synapseVerb, string? target)
    {
        var source = caller.Source is Caller.FromDomain d ? d.Domain.Value : "gateway";
        var targetId = target ?? "?";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        journal.Record(new SynapseJournalEntry(
            TimestampUnixMs: timestamp,
            Kind: eventType,
            SynapseVerb: synapseVerb,
            CorrelationId: caller.CorrelationId.Value,
            SourceNeuron: source,
            TargetNeuron: targetId));

        if (string.IsNullOrWhiteSpace(caller.UserId)) return;

        // The Flutter Trace view decodes the payload as JSON and reads the shape
        // below (state/timeline_bloc.dart::_fromInoEvent). Adding or renaming keys
        // here is a wire change — update both ends together.
        var sequence = Interlocked.Increment(ref _sequence);
        var envelope = new Dictionary<string, object?>
        {
            ["SequenceNumber"] = sequence,
            ["SynapseVerb"] = synapseVerb,
            ["TargetId"] = targetId,
            ["CorrelationId"] = caller.CorrelationId.Value,
            ["Decay"] = 100,
        };

        // Slice 15 — if Cortex (or any neuron) has annotated this target via
        // BddMockChatClient, surface the matched scenario on the envelope so
        // the Flutter inspector's Reasoning panel can render "mocked via BDD
        // · {Feature} — {Scenario}" without a separate gateway round-trip.
        if (reasoningProbe.TryGet(targetId, out var reasoning))
        {
            envelope["ReasoningSource"] = reasoning.Source;
            envelope["Scenario"] = reasoning.ScenarioName;
            envelope["Feature"] = reasoning.FeatureTitle;
        }

        if (caller.NeuronId is { } neuronId)
        {
            envelope["Neuron"] = neuronId.Value;
        }

        var payload = JsonSerializer.SerializeToUtf8Bytes(envelope);
        var evt = new InoEvent(
            Type: eventType,
            SourceNeuron: source,
            CorrelationId: caller.CorrelationId.Value,
            Payload: payload,
            TimestampUnixMs: timestamp);
        events.Publish(caller.UserId, evt);
    }

    private static NeuronContext DeriveChildContext(NeuronContext caller, CanonicalTarget target) =>
        caller with
        {
            SynapseId = SynapseId.New(),
            Source = new Caller.FromDomain(target.Domain),
            CurrentEventId = caller.CurrentEventId,
        };

    private static NeuronContext DeriveChildContext(NeuronContext caller, ReactiveTarget target) =>
        caller with
        {
            SynapseId = SynapseId.New(),
            Source = new Caller.FromDomain(target.Domain),
        };
}
