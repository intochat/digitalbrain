using System.Text.Json;

namespace DigitalBrain.Runtime.Orleans.Signalling;

/// <summary>One batch of a session, numbered from 1. <c>Next(acked: Number)</c> acknowledges it; until then it is sent again.</summary>
/// <param name="Number">The batch number.</param>
/// <param name="Signals">At most <see cref="SessionSynapseOptions.MaxBatchSignals"/> signals, in source order.</param>
public sealed record SessionBatch(long Number, IReadOnlyList<SessionSignal> Signals);

/// <summary>One signal of a batch as JSON: its source, its publisher sequence, its <c>[Signal]</c> alias and payload.</summary>
/// <param name="Source">The publishing neuron, written <c>brain/type/key</c>.</param>
/// <param name="Sequence">The publisher sequence.</param>
/// <param name="Alias">The <c>[Signal]</c> alias; <c>synapse.gap</c> when history was trimmed past the session's cursor.</param>
/// <param name="Payload">The signal as JSON (web defaults).</param>
/// <param name="At">When it was published.</param>
public sealed record SessionSignal(string Source, long Sequence, string Alias, JsonElement Payload, DateTimeOffset At);

/// <summary>A session already holds <see cref="SessionSynapseOptions.MaxSessionSynapses"/> synapses.</summary>
public sealed class SessionLimitException : InvalidOperationException
{
    /// <summary>Creates the error.</summary>
    public SessionLimitException() : base("The session holds the most synapses it may.") { }

    /// <summary>Creates the error with a message.</summary>
    public SessionLimitException(string message) : base(message) { }

    /// <summary>Creates the error with a message and cause.</summary>
    public SessionLimitException(string message, Exception innerException) : base(message, innerException) { }
}
