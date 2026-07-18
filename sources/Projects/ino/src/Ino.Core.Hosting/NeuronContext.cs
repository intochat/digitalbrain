using System.Diagnostics;
using Ino.Core;
using Microsoft.Extensions.Logging;

namespace Ino.Core.Hosting;

/// <summary>
/// Per-call context passed into every HandleAsync / ReactAsync invocation. Carries
/// the identity of the current synapse (correlation, causation), the calling user
/// context (when this is part of a user-initiated chain), and a logger. Also exposes
/// ambient Fire&lt;T&gt; / FireBroadcast&lt;T&gt; surfaces backed by a required IFirePort.
///
/// Phase 2 makes this a sealed record (was an interface in Phase 1). with-expressions
/// are used to derive child contexts without rebuilding every property.
/// </summary>
public sealed record NeuronContext(
    SynapseId SynapseId,
    CorrelationId CorrelationId,
    Caller Source,
    StreamKey SourceStream,
    string? UserId = null,
    string? SessionId = null,
    NeuronId? NeuronId = null)
{
    public required IFirePort FirePort { get; init; }
    public required ILogger Logger { get; init; }
    public Activity? CurrentActivity { get; init; }
    public EventId? CurrentEventId { get; init; }

    public Task<NeuronResult> Fire<T>(T synapse, CancellationToken ct = default) where T : ISynapse
        => FirePort.Fire(synapse, this, ct);

    public Task FireBroadcast<T>(T synapse, CancellationToken ct = default) where T : ISynapse
        => FirePort.FireBroadcast(synapse, this, ct);
}
