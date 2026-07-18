namespace Ino.Core.Hosting;

/// <summary>
/// Transport-neutral event published by neurons (mostly via <see cref="IFirePort"/>) and
/// consumed by the gateway's <c>StreamEvents</c> surface. The Flutter Trace view + the
/// inspector drawer both read this stream to surface live cross-silo synapse activity.
///
/// <c>Payload</c> is opaque — callers who want typed data should subscribe to the
/// in-process synapse via a reactive neuron instead. This event stream is the
/// observability channel, not the data channel.
/// </summary>
public sealed record InoEvent(
    string Type,
    string SourceNeuron,
    string CorrelationId,
    ReadOnlyMemory<byte> Payload,
    long TimestampUnixMs);
