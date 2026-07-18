namespace Ino.Gateway;

/// <summary>
/// Transport-neutral result of <see cref="IInoGateway.FireSynapseAsync"/>.
/// Carries everything needed to render the fired synapse's response inline
/// in the chat thread (RFW bytes if the response payload implements
/// <c>IHasRfwPayload</c>; plain text otherwise) plus the conversation
/// correlation_id so the client keeps the thread pinned.
/// </summary>
public sealed record FireResult(
    bool Success,
    string SynapseId,
    string Reply,
    string ContentType,
    ReadOnlyMemory<byte> RfwDescription,
    ReadOnlyMemory<byte> RfwData,
    string CorrelationId);
