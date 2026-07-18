using Ino.Core;
using Ino.Core.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ino.Testing;

/// <summary>
/// Factory for constructing <see cref="NeuronContext"/> instances inside unit and
/// integration tests. Every field has a sensible default so tests supply only the
/// values they care about; the returned record can be forked via with-expressions.
///
/// Production contexts are built by the runtime FirePort implementation (in Phase 2)
/// which wires the real logger, activity, and causation metadata from the inbound
/// synapse envelope. Tests never go through that path — they build a context by
/// hand, point it at a <see cref="NoOpFirePort"/> (or a capturing fake), and invoke
/// the neuron directly.
/// </summary>
public static class NeuronContextForTest
{
    public static NeuronContext Create(
        Caller source,
        IFirePort? firePort = null,
        ILogger? logger = null,
        StreamKey? sourceStream = null,
        string? userId = null,
        string? sessionId = null)
    {
        return new NeuronContext(
            SynapseId: SynapseId.New(),
            CorrelationId: CorrelationId.New(),
            Source: source,
            SourceStream: sourceStream ?? new StreamKey("test"),
            UserId: userId,
            SessionId: sessionId)
        {
            FirePort = firePort ?? new NoOpFirePort(),
            Logger = logger ?? NullLogger.Instance,
        };
    }
}
