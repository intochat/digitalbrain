using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Messaging;
using DigitalBrain.Client;

namespace DigitalBrain.Testing;

// Thrown when a journal compacts past the wait cursor before the awaited delivery was
// observed — the deliveries are gone (ResetSnapshot semantics), so waiting further can
// never succeed. Distinct from TimeoutException: this is "unknowable", not "not yet".
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
        TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(brain);
        ArgumentNullException.ThrowIfNull(match);

        var budget = timeout ?? DefaultTimeout;
        var deadline = DateTimeOffset.UtcNow + budget;
        var afterSequence = 0L;
        var seenTypes = new List<string>();

        while (true)
        {
            var page = await brain.ReadJournalAsync(subject, kind, afterSequence).ConfigureAwait(false);

            if (page.ResetSnapshot is not null)
            {
                var seenBeforeCompaction = seenTypes.Count > 0 ? string.Join(", ", seenTypes) : "(none)";
                throw new JournalCompactedException(
                    $"The {kind} journal of {subject} compacted past the wait cursor (resume {page.ResumeSequence}); "
                    + $"deliveries were dropped before they could be observed. Saw before compaction: [{seenBeforeCompaction}]");
            }

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
