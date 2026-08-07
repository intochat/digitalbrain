using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors;
using DigitalBrain.Chat;
using DigitalBrain.Client;
using DigitalBrain.Mcp;
using DigitalBrain.Shell;

namespace DigitalBrain.UiEdge;

internal sealed class OwnerSessionJournal(IDigitalBrain brain)
{
    public Task<JournalRead> ReadShellOutgoingAsync(string shellName, long afterSequence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shellName);
        ArgumentOutOfRangeException.ThrowIfNegative(afterSequence);

        return brain.ReadJournalAsync(
            NeuronId.For<IShell>(brain.Owner, shellName),
            JournalKind.Outgoing,
            afterSequence);
    }

    public Task<JournalRead> ReadChatOutgoingAsync(string chatName, long afterSequence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chatName);
        ArgumentOutOfRangeException.ThrowIfNegative(afterSequence);

        return brain.ReadJournalAsync(
            NeuronId.For<IChat>(brain.Owner, chatName),
            JournalKind.Outgoing,
            afterSequence);
    }

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

    public IAsyncEnumerable<JournalRead> WatchBehaviorOutgoingAsync(
        string behaviorId,
        long afterSequence,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(behaviorId);
        ArgumentOutOfRangeException.ThrowIfNegative(afterSequence);

        return brain.WatchJournalAsync(
            NeuronId.For<IBehaviorNeuron>(brain.Owner, behaviorId),
            JournalKind.Outgoing,
            afterSequence,
            cancellationToken);
    }
}
