using DigitalBrain.Abstractions;
using DigitalBrain.Chat;
using DigitalBrain.Client;
using DigitalBrain.Mcp;
using DigitalBrain.Shell;

namespace DigitalBrain.Kernel;

internal sealed class OwnerSessionJournal(IDigitalBrain brain)
{
    public IAsyncEnumerable<JournalRead> WatchShellOutgoingAsync(
        string shellName,
        long afterSequence,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shellName);
        ArgumentOutOfRangeException.ThrowIfNegative(afterSequence);

        return brain.WatchJournalAsync(
            NeuronId.For<IShell>(brain.Owner, shellName),
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

    public IAsyncEnumerable<JournalRead> WatchAuthorizationOutgoingAsync(
        long afterSequence,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(afterSequence);

        return brain.WatchJournalAsync(
            NeuronId.For<IMcpAuthorization>(brain.Owner, McpAuthorizationNeuron.InstanceName),
            JournalKind.Outgoing,
            afterSequence,
            cancellationToken);
    }
}
