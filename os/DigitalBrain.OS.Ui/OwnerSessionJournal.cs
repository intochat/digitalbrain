using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors;
using DigitalBrain.Chat;
using DigitalBrain.Client;
using DigitalBrain.Flutter;
using DigitalBrain.Mcp;

namespace DigitalBrain.Flutter.Http;

internal sealed class OwnerSessionJournal
{
    private readonly IGrainFactory _grains;
    private readonly ISessionNeuron _session;
    private readonly OwnerId _owner;

    private OwnerSessionJournal(IGrainFactory grains, ISessionNeuron session, OwnerId owner)
    {
        _grains = grains;
        _session = session;
        _owner = owner;
    }

    public static OwnerSessionJournal Open(IGrainFactory grains, OwnerId owner)
    {
        ArgumentNullException.ThrowIfNull(grains);

        return new OwnerSessionJournal(
            grains,
            grains.GetGrain<ISessionNeuron>(ISessionNeuron.ForOwner(owner).ToGrainId()),
            owner);
    }

    public Task<JournalRead> ReadShellOutgoingAsync(string shellName, long afterSequence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shellName);
        ArgumentOutOfRangeException.ThrowIfNegative(afterSequence);

        return _session.ReadNeuronJournal(
            NeuronId.For<IShell>(_owner, shellName),
            JournalKind.Outgoing,
            afterSequence);
    }

    public Task<JournalRead> ReadChatOutgoingAsync(string chatName, long afterSequence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chatName);
        ArgumentOutOfRangeException.ThrowIfNegative(afterSequence);

        return _session.ReadNeuronJournal(
            NeuronId.For<IChat>(_owner, chatName),
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

        return WatchOutgoingAsync(NeuronId.For<IShell>(_owner, shellName), afterSequence, cancellationToken);
    }

    public IAsyncEnumerable<JournalRead> WatchChatOutgoingAsync(
        string chatName,
        long afterSequence,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chatName);
        ArgumentOutOfRangeException.ThrowIfNegative(afterSequence);

        return WatchOutgoingAsync(NeuronId.For<IChat>(_owner, chatName), afterSequence, cancellationToken);
    }

    public IAsyncEnumerable<JournalRead> WatchAuthorizationOutgoingAsync(
        long afterSequence,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(afterSequence);

        return WatchOutgoingAsync(
            NeuronId.For<IMcpAuthorization>(_owner, McpAuthorizationNeuron.InstanceName),
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

        return WatchOutgoingAsync(
            NeuronId.For<IBehaviorNeuron>(_owner, behaviorId),
            afterSequence,
            cancellationToken);
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "SSE disconnect cleanup must tear down the grain observer without masking the stream completion.")]
    private async IAsyncEnumerable<JournalRead> WatchOutgoingAsync(
        NeuronId subject,
        long afterSequence,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var observer = new ChannelJournalObserver(JournalKind.Outgoing);
        var reference = _grains.CreateObjectReference<IJournalObserver>(observer);
        try
        {
            await _session.WatchNeuron(subject, JournalKind.Outgoing, afterSequence, reference);
            await foreach (var batch in observer.Reads.ReadAllAsync(cancellationToken))
            {
                yield return batch;
            }
        }
        finally
        {
            try
            {
                await _session.UnwatchNeuron(subject, reference);
            }
            catch (Exception)
            {
            }

            try
            {
                _grains.DeleteObjectReference<IJournalObserver>(reference);
            }
            catch (Exception)
            {
            }

            observer.Complete();
        }
    }
}
