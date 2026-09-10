using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Signals;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;

namespace DigitalBrain.Core;

// A neuron whose domain state is a snapshot (IPersistentState) rather than the op log.
// Concrete subclasses must redeclare [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)]
// on their own constructor parameter and forward it: Orleans binds facets on the leaf constructor.
public abstract class Neuron<TState> : Neuron where TState : class
{
    private static readonly SnapshotEnvelope<TState> Empty = new(null, null, []);
    private readonly IPersistentState<SnapshotEnvelope<TState>> _state;
    private readonly AnnouncementDrain _announcements;
    private readonly IReactionCrashPoint? _crashPoint;

    protected Neuron(NeuronRuntime runtime, IPersistentState<SnapshotEnvelope<TState>> state) : base(runtime)
    {
        ArgumentNullException.ThrowIfNull(state);
        _state = state;
        _announcements = new AnnouncementDrain(FireAnnouncementAsync);
        _crashPoint = ServiceProvider.GetService<IReactionCrashPoint>();
    }

    protected TState? State => Envelope.State;

    private SnapshotEnvelope<TState> Envelope => _state.RecordExists ? _state.State : Empty;

    internal int StoredAnnouncementCount => Envelope.Announcements.Count;

    private protected override bool HasStoredAnnouncements => StoredAnnouncementCount > 0;

    private protected override bool IsAppliedBy(SignalId delivery) => Envelope.AppliedBy == delivery;

    private protected override bool HasBufferedAnnouncements => _announcements.HasBuffered;

    private protected override void DiscardBufferedAnnouncements() => _announcements.Clear();

    private protected override async Task<bool> DrainAnnouncementsAsync(CancellationToken cancellationToken)
    {
        var envelope = Envelope;
        var remaining = await _announcements.FireStoredAsync(envelope.Announcements, cancellationToken).ConfigureAwait(true);
        if (remaining.Count != envelope.Announcements.Count)
        {
            await WriteEnvelopeAsync(envelope with { Announcements = remaining }, cancellationToken).ConfigureAwait(true);
        }

        return remaining.Count > 0;
    }

    protected Task SaveAsync(TState value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (ExecutingCommand is { } id)
        {
            throw new InvalidOperationException(
                $"Neuron '{Id}' cannot save a snapshot while executing command '{id}': save state from a reaction, not a command.");
        }

        var envelope = new SnapshotEnvelope<TState>(value, (ReactionContext as DeliveryReaction)?.Delivery.SignalId,
            _announcements.WithBuffered(Envelope.Announcements));
        return SaveSnapshotAsync(envelope, cancellationToken);
    }

    private async Task SaveSnapshotAsync(SnapshotEnvelope<TState> envelope, CancellationToken cancellationToken)
    {
        await WriteEnvelopeAsync(envelope, cancellationToken).ConfigureAwait(true);
        NoteSnapshotSaved();
        _announcements.Clear();
        _crashPoint?.AfterSnapshotSave(Id);
    }

    protected void Announce(Signal signal, NeuronId? to = null, CorrelationId? correlation = null)
    {
        ArgumentNullException.ThrowIfNull(signal);
        if (ExecutingCommand is { } id)
        {
            throw new InvalidOperationException(
                $"Neuron '{Id}' cannot announce while executing command '{id}': announce from a reaction, not a command.");
        }

        if (ReactionContext is not DeliveryReaction reaction)
        {
            throw new InvalidOperationException(
                $"Neuron '{Id}' can only announce from a reaction: announcements are saved with the snapshot.");
        }

        signal = Signal.Create(signal.Type, signal.Body);
        if (to == Id)
        {
            throw new SignalRejectedException($"Neuron '{Id}' cannot announce to itself.");
        }

        _announcements.Buffer(new Announcement(SignalId.New(), signal, to,
            correlation ?? reaction.Delivery.CorrelationId, reaction.Delivery.SignalId));
    }

    private async Task WriteEnvelopeAsync(SnapshotEnvelope<TState> envelope, CancellationToken cancellationToken)
    {
        _state.State = envelope;
        try
        {
            await _state.WriteStateAsync(cancellationToken).ConfigureAwait(true);
        }
        catch
        {
            // A failed write must not leave AppliedBy set in memory, or the retry would skip a reaction that never committed.
            await _state.ReadStateAsync(CancellationToken.None).ConfigureAwait(true);
            throw;
        }
    }
}

[GenerateSerializer]
[Alias("db.v3.snapshot`1")]
public sealed record SnapshotEnvelope<TState>(
    [property: Id(0)] TState? State,
    [property: Id(1)] SignalId? AppliedBy,
    [property: Id(2)] IReadOnlyList<Announcement> Announcements) where TState : class;
