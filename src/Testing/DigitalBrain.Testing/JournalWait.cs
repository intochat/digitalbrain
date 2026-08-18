using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Messaging;
using DigitalBrain.Client;

namespace DigitalBrain.Testing;

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
