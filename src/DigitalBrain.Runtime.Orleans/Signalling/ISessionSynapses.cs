using DigitalBrain.Contracts;

namespace DigitalBrain.Runtime.Orleans.Signalling;

/// <summary>
/// The one subscription path of in-process clients and scripts (04 D-02, R4-03): a per-session neuron
/// <c>brain/runtime.session/{session}</c> that owns synapses and hands their signals out in ack-driven batches. Get it with
/// <see cref="SessionSynapses.For"/>. <c>Next</c> interleaves, so a waiting long-poll never blocks the other calls or the signal
/// deliveries that complete it. Admission runs separately from ordinary subscription commits and shutdown.
/// </summary>
[NeuronAlias("runtime.session")]
public interface ISessionSynapses
{
    /// <summary>Obtains a one-use ticket after the source admits these signals under the session's identity; commits no synapse.</summary>
    /// <exception cref="SessionLimitException">The session already holds the most synapses it may.</exception>
    /// <exception cref="UnauthorizedAccessException">The source refuses the session, or is in another brain.</exception>
    /// <exception cref="ArgumentException">An alias is not a registered <c>[Signal]</c>.</exception>
    [Interleaves]
    Task<string> PrepareSubscription(NeuronId source, string[] aliases);

    /// <summary>Commits this activation's one-use admission ticket. Expired, invalidated and foreign-caller tickets are refused.</summary>
    Task CommitSubscription(string ticket);

    /// <summary>Drops the synapse from <paramref name="source"/>.</summary>
    Task Unsubscribe(NeuronId source);

    /// <summary>
    /// Acknowledges batch <paramref name="acked"/> (0 for none) and returns the next one. When batch <c>acked + 1</c> was sent and not
    /// acknowledged it is sent again. With nothing pending the call waits up to <see cref="SessionSynapseOptions.NextTimeout"/>
    /// and returns null. Each call counts as an ack for <see cref="SessionSynapseOptions.SessionTtl"/>.
    /// </summary>
    [Interleaves]
    Task<SessionBatch?> Next(long acked, CancellationToken cancellationToken);

    /// <summary>Removes every synapse and cursor of the session and ends it.</summary>
    Task End();
}

/// <summary>Addresses session neurons.</summary>
public static class SessionSynapses
{
    /// <summary>The neuron type of sessions.</summary>
    public const string NeuronType = "runtime.session";

    /// <summary>Obtains source admission without holding the session's turn, then commits the admitted subscription.</summary>
    public static async Task Subscribe(this ISessionSynapses session, NeuronId source, string[] aliases)
    {
        ArgumentNullException.ThrowIfNull(session);
        var ticket = await session.PrepareSubscription(source, aliases).ConfigureAwait(true);
        await session.CommitSubscription(ticket).ConfigureAwait(true);
    }

    /// <summary>The id of session <paramref name="session"/> in <paramref name="brain"/>; the session id is issued by the caller's authenticated session.</summary>
    public static NeuronId For(BrainId brain, string session) => new(brain, NeuronType, session);
}
