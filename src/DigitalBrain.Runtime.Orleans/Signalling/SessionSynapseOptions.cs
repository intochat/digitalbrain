namespace DigitalBrain.Runtime.Orleans.Signalling;

/// <summary>Session and listener lifetimes and caps, bound from <c>IntoChatOS:Signalling</c> (04 D-13, R4-03).</summary>
public sealed class SessionSynapseOptions
{
    /// <summary>A session with no ack for this long removes its synapses and cursors and ends; a listener neuron with no handler for this long does the same. Default 10 minutes.</summary>
    public TimeSpan SessionTtl { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>How often a session or listener checks <see cref="SessionTtl"/>. Default 1 minute.</summary>
    public TimeSpan SessionSweepEvery { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How long a <c>Next</c> long-poll waits for signals before it returns null. Default 20 seconds. The wait never reaches the silo's
    /// response timeout: it is capped at two thirds of it, so an idle poll answers before its caller gives up on it.
    /// </summary>
    public TimeSpan NextTimeout { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>Most synapses one session holds; further <c>Subscribe</c> calls are refused. Default 256.</summary>
    public int MaxSessionSynapses { get; set; } = 256;

    /// <summary>Most signals in one batch. Default 256.</summary>
    public int MaxBatchSignals { get; set; } = 256;

    /// <summary>Most signals a session queues before it refuses more; the cursor stays and the next reference retries. Default 4096.</summary>
    public int MaxPendingSignals { get; set; } = 4096;
}
