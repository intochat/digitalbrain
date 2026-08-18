using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Messaging;
using DigitalBrain.Client;

namespace DigitalBrain.Testing;

// Thrown when a journal compacts past the wait cursor DURING an already-established wait —
// the deliveries are gone (ResetSnapshot semantics), so waiting further can never succeed.
// A compaction observed on the wait's first read is not this: it is the wait's baseline
// ("start watching from now"), since nothing the wait promised to see has been lost.
// Distinct from TimeoutException: this is "unknowable", not "not yet".
public sealed class JournalCompactedException : InvalidOperationException
{
    public JournalCompactedException()
    {
    }

    public JournalCompactedException(string message)
        : base(message)
    {
    }

    public JournalCompactedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public static class JournalWait
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    public static async Task<SynapseDelivery> ForAsync(
        IDigitalBrain brain,
        NeuronId subject,
        JournalKind kind,
        Func<SynapseDelivery, bool> match,
        TimeSpan? timeout = null,
        long afterSequence = 0)
    {
        ArgumentNullException.ThrowIfNull(brain);
        ArgumentNullException.ThrowIfNull(match);

        var budget = timeout ?? DefaultTimeout;
        var deadline = DateTimeOffset.UtcNow + budget;
        var seenTypes = new List<string>();
        var isBaselineRead = true;

        while (true)
        {
            var page = await brain.ReadJournalAsync(subject, kind, afterSequence).ConfigureAwait(false);

            if (page.ResetSnapshot is not null)
            {
                if (isBaselineRead)
                {
                    // The journal already exceeded retention before this wait started — that's
                    // the wait's baseline ("start watching from now"), not a loss. Nothing the
                    // wait promised to see has gone missing, so adopt the tip and keep polling.
                    afterSequence = page.ResumeSequence;
                }
                else
                {
                    var seenBeforeCompaction = seenTypes.Count > 0 ? string.Join(", ", seenTypes) : "(none)";
                    throw new JournalCompactedException(
                        $"The {kind} journal of {subject} compacted past the wait cursor (resume {page.ResumeSequence}) "
                        + $"mid-wait; deliveries were dropped before they could be observed. Saw before compaction: [{seenBeforeCompaction}]");
                }
            }
            else
            {
                foreach (var delivery in page.Delta)
                {
                    var typeName = delivery.Synapse.GetType().Name;
                    if (!seenTypes.Contains(typeName, StringComparer.Ordinal))
                    {
                        seenTypes.Add(typeName);
                    }

                    if (match(delivery))
                    {
                        return delivery;
                    }
                }

                afterSequence = page.ResumeSequence;
            }

            isBaselineRead = false;

            if (DateTimeOffset.UtcNow >= deadline)
            {
                var seen = seenTypes.Count > 0 ? string.Join(", ", seenTypes) : "(none)";
                throw new TimeoutException(
                    $"No matching {kind} delivery on {subject} within {budget}. Saw: [{seen}]");
            }

            await Task.Delay(PollInterval).ConfigureAwait(false);
        }
    }
}
