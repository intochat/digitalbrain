using Microsoft.Extensions.AI;

namespace DigitalBrain.Conversations;

// Stage-2 domain owner (Seam 5). UI → Conversations ← AI. Tip IChat is not forever.
[ClientEntryPoint]
[Alias("conversation")]
public partial interface IConversation :
    INeuron,
    IHandle<ReadConversationTranscript>,
    IHandle<ConversationNote>
{
    const string GrainTypeName = "conversation";
    const string DefaultLocalName = "main";

    static NeuronId ForOwner(OwnerId owner, string localName = DefaultLocalName)
        => new(GrainTypeName, owner, ConversationIdentity.Validated(localName, nameof(localName)));

    // A18: use NeuronId.ForPrincipal<IConversation>(owner, principal, localName)
    // — ForPrincipal lives in Abstractions 5.0, not module contracts.

    [Alias(nameof(Send))]
    [ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    Task<TurnAccepted> Send(SendConversationMessage message);

    [Alias(nameof(SendStreaming))]
    IAsyncEnumerable<ChatResponseUpdate> SendStreaming(
        SendConversationMessage message,
        CancellationToken cancellationToken = default);

    [Alias(nameof(Cancel))]
    Task Cancel(CancelConversationTurn command);

    [Alias(nameof(Read))]
    Task<ConversationTranscript> Read();

    // D3 — resumable projection by sequence.
    [Alias(nameof(ReadAfter))]
    Task<ConversationTranscript> ReadAfter(long afterSequence, int limit = 64);

    [Alias(nameof(ReadTurns))]
    Task<IReadOnlyList<ConversationTurnSnapshot>> ReadTurns();
}
