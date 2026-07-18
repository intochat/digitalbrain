namespace Ino.Gateway;

/// <summary>
/// Transport-neutral result of <see cref="IInoGateway.ChatAsync"/>. The gRPC
/// layer maps this directly to the wire <c>ChatResponse</c>; MCP and CLI
/// transports shape it to their own surface. RFW bytes are empty when the
/// reply is plain text.
///
/// <see cref="CorrelationId"/> is always populated — the gateway either
/// echoes the client-supplied conversation handle or generates a fresh
/// one for a brand-new chat. The Flutter client caches it and sends it
/// back on the next turn so clarification round-trips reach the same
/// neuron activation.
/// </summary>
public sealed record ChatResult(
    string Reply,
    string NeuronId,
    string ContentType,
    ReadOnlyMemory<byte> RfwDescription,
    ReadOnlyMemory<byte> RfwData,
    string CorrelationId,
    bool IsSkeleton = false)
{
    public static ChatResult Text(string reply, string neuronId, string correlationId) =>
        new(reply, neuronId, "text", ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty, correlationId);

    public static ChatResult WithRfw(
        string reply,
        string neuronId,
        string contentType,
        ReadOnlyMemory<byte> description,
        ReadOnlyMemory<byte> data,
        string correlationId) =>
        new(reply, neuronId, contentType, description, data, correlationId);

    public static ChatResult Skeleton(
        string reply,
        string neuronId,
        string contentType,
        ReadOnlyMemory<byte> description,
        ReadOnlyMemory<byte> data,
        string correlationId) =>
        new(reply, neuronId, contentType, description, data, correlationId, IsSkeleton: true);
}
