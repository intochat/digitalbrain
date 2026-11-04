using DigitalBrain.Rules;

namespace DigitalBrain.Spi;

/// <summary>What a host calls on a state neuron to run its synapse functions.</summary>
public interface ISynapseReceiver
{
    /// <summary>Runs the reaction synapses for <paramref name="signal"/> in registration order; after a save the views run.</summary>
    /// <param name="signal">The arriving signal.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>A task that completes when every reaction has run.</returns>
    Task Receive(Signal signal, CancellationToken ct);

    /// <summary>Runs the gate synapses for <paramref name="signal"/> in order; returns the first refusal (journaled) or the last allowance. Saves nothing on allow; holds the allowed next state as provisional until <see cref="Committed"/> or <see cref="Aborted"/>, and later gate calls evaluate against it.</summary>
    /// <param name="signal">The signal about to be published.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The verdict.</returns>
    Task<Check> Gate(Signal signal, CancellationToken ct);

    /// <summary>
    /// Called after the append of the publisher is durable: saves the next state of <paramref name="allowed"/> with <paramref name="signal"/>.
    /// The provisional entry is matched by <see cref="Check.Allowed.Ticket"/>; only an allowance without a ticket (<see cref="Guid.Empty"/>,
    /// legacy) falls back to the oldest uncommitted entry of an equal signal.
    /// </summary>
    /// <param name="signal">The published signal.</param>
    /// <param name="allowed">The verdict <see cref="Gate"/> returned, carrying its ticket.</param>
    /// <returns>A task that completes when the state is saved.</returns>
    Task Committed(Signal signal, Check.Allowed allowed);

    /// <summary>
    /// Called when the publish <paramref name="signal"/> was gated for did not happen (a later gate refused, timed out or failed, or the
    /// append threw): drops the provisional state of <paramref name="signal"/> without saving; later provisional states are re-gated on the new base.
    /// </summary>
    /// <param name="signal">The signal whose publish was abandoned.</param>
    /// <param name="ticket">
    /// The <see cref="Check.Allowed.Ticket"/> of the entry to drop. <see cref="Guid.Empty"/> means the newest uncommitted entry for
    /// <paramref name="signal"/>: a host sends it for a gate that timed out or failed, whose verdict (and ticket) never arrived.
    /// </param>
    /// <returns>A task that completes when the provisional state is dropped.</returns>
    Task Aborted(Signal signal, Guid ticket);
}
