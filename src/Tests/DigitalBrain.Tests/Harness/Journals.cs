using DigitalBrain.Abstractions;
using DigitalBrain.Client;

namespace DigitalBrain.Tests.Harness;

internal static class Journals
{
    private static readonly TimeSpan DefaultPatience = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    internal static async Task<SynapseDelivery> WaitForAsync(
        IDigitalBrain brain,
        NeuronId subject,
        JournalKind kind,
        Func<SynapseDelivery, bool> match,
        TimeSpan? patience = null)
    {
        var deadline = DateTime.UtcNow + (patience ?? DefaultPatience);
        long cursor = 0;

        while (DateTime.UtcNow < deadline)
        {
            var page = await brain.ReadJournalAsync(subject, kind, cursor);
            foreach (var delivery in page.Delta)
            {
                if (match(delivery))
                {
                    return delivery;
                }
            }

            cursor = page.ResumeSequence;
            await Task.Delay(PollInterval);
        }

        throw new TimeoutException(
            $"No matching delivery appeared in {subject} {kind} journal within {patience ?? DefaultPatience}.");
    }

    internal static async Task<IReadOnlyList<SynapseDelivery>> SnapshotAfterQuietAsync(
        IDigitalBrain brain,
        NeuronId subject,
        JournalKind kind,
        TimeSpan quietFor,
        TimeSpan? patience = null)
    {
        var entries = new List<SynapseDelivery>();
        long cursor = 0;
        var quietSince = DateTime.UtcNow;
        var deadline = DateTime.UtcNow + (patience ?? DefaultPatience);

        while (DateTime.UtcNow - quietSince < quietFor)
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException(
                    $"The {subject} {kind} journal kept growing and never went quiet for {quietFor} within {patience ?? DefaultPatience}.");
            }

            var page = await brain.ReadJournalAsync(subject, kind, cursor);
            if (page.Delta.Count > 0)
            {
                entries.AddRange(page.Delta);
                cursor = page.ResumeSequence;
                quietSince = DateTime.UtcNow;
            }

            await Task.Delay(PollInterval);
        }

        return entries;
    }
}
