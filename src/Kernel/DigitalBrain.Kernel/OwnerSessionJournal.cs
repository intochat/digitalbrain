using DigitalBrain.Abstractions;
using DigitalBrain.Chat;
using DigitalBrain.Client;
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

}
