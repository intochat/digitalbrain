using DigitalBrain.Abstractions;
using DigitalBrain.Chat;
using DigitalBrain.Client;
using DigitalBrain.Modules.Sdk.Mcp;
using DigitalBrain.UI;

namespace DigitalBrain.Kernel;

internal sealed class OwnerSessionJournal(IDigitalBrain brain)
{
    public IAsyncEnumerable<JournalRead> WatchSurfaceOutgoingAsync(
        string surfaceName,
        long afterSequence,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(surfaceName);
        ArgumentOutOfRangeException.ThrowIfNegative(afterSequence);

        return brain.WatchJournalAsync(
            NeuronId.For<ISurface>(brain.Owner, surfaceName),
            JournalKind.Outgoing,
            afterSequence,
            cancellationToken);
    }

    public IAsyncEnumerable<JournalRead> WatchChatOutgoingAsync(
        string chatName,
        long afterSequence,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chatName);
        ArgumentOutOfRangeException.ThrowIfNegative(afterSequence);

        return brain.WatchJournalAsync(
            NeuronId.For<IChat>(brain.Owner, chatName),
            JournalKind.Outgoing,
            afterSequence,
            cancellationToken);
    }

    public IAsyncEnumerable<JournalRead> WatchGraphOutgoingAsync(
        long afterSequence,
        CancellationToken cancellationToken)
        => WatchGraphOutgoingAsync(principal: null, afterSequence, cancellationToken);

    // A18: principal partition when authenticated; owner graph only for unattributed/system.
    public IAsyncEnumerable<JournalRead> WatchGraphOutgoingAsync(
        PrincipalId? principal,
        long afterSequence,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(afterSequence);

        var graph = principal is { } id
            ? ISynapseGraph.ForPrincipal(brain.Owner, id)
            : ISynapseGraph.ForOwner(brain.Owner);

        return brain.WatchJournalAsync(
            graph,
            JournalKind.Outgoing,
            afterSequence,
            cancellationToken);
    }

    public IAsyncEnumerable<JournalRead> WatchAuthorizationOutgoingAsync(
        long afterSequence,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(afterSequence);

        return brain.WatchJournalAsync(
            NeuronId.For<IMcpAuthorization>(brain.Owner, IMcpAuthorization.DefaultInstanceName),
            JournalKind.Outgoing,
            afterSequence,
            cancellationToken);
    }
}
