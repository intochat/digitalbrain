using Ino.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans;

namespace Ino.Core.Hosting;

/// <summary>
/// Orleans serialization surrogate for <see cref="NeuronContext"/>. Context is
/// intended to be ambient — built by the silo-local <see cref="IFirePort"/> on
/// each inbound synapse — and the Phase 2 runtime does not marshal it over the
/// wire. But because <see cref="INeuron{T}"/> and <see cref="IReactsTo{T}"/>
/// declare <see cref="NeuronContext"/> in their signatures, Orleans requires a
/// serializer for the type at silo-startup validation time.
///
/// The surrogate round-trips only the wire-safe identity fields (synapse id,
/// correlation, caller, stream, user/session, causation). <see cref="NeuronContext.FirePort"/>
/// and <see cref="NeuronContext.Logger"/> are NOT carried across the wire — on
/// rehydration the surrogate populates them with <see cref="NoOpFirePort"/> and
/// <see cref="NullLogger.Instance"/>. A receiver that actually needs a functional
/// context must rebuild one from its own DI container; relying on the rehydrated
/// instance's Fire/Logger is a bug.
/// </summary>
[GenerateSerializer]
public struct NeuronContextSurrogate
{
    [Id(0)] public SynapseId SynapseId;
    [Id(1)] public CorrelationId CorrelationId;
    [Id(2)] public Caller Source;
    [Id(3)] public StreamKey SourceStream;
    [Id(4)] public string? UserId;
    [Id(5)] public string? SessionId;
    [Id(6)] public EventId? CurrentEventId;
    [Id(7)] public NeuronId? NeuronId;
}

[RegisterConverter]
public sealed class NeuronContextSurrogateConverter : IConverter<NeuronContext, NeuronContextSurrogate>
{
    public NeuronContext ConvertFromSurrogate(in NeuronContextSurrogate surrogate) =>
        new(
            SynapseId: surrogate.SynapseId,
            CorrelationId: surrogate.CorrelationId,
            Source: surrogate.Source,
            SourceStream: surrogate.SourceStream,
            UserId: surrogate.UserId,
            SessionId: surrogate.SessionId,
            NeuronId: surrogate.NeuronId)
        {
            FirePort = new NoOpFirePort(),
            Logger = NullLogger.Instance,
            CurrentEventId = surrogate.CurrentEventId,
        };

    public NeuronContextSurrogate ConvertToSurrogate(in NeuronContext value) =>
        new()
        {
            SynapseId = value.SynapseId,
            CorrelationId = value.CorrelationId,
            Source = value.Source,
            SourceStream = value.SourceStream,
            UserId = value.UserId,
            SessionId = value.SessionId,
            CurrentEventId = value.CurrentEventId,
            NeuronId = value.NeuronId,
        };
}
