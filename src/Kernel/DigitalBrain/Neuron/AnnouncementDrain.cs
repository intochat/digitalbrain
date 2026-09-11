using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Core;

internal sealed class AnnouncementDrain(Func<Announcement, CancellationToken, Task<FireOutcome>> fire)
{
    private readonly List<Announcement> _buffered = [];

    internal void Buffer(Announcement announcement) => _buffered.Add(announcement);

    internal bool HasBuffered => _buffered.Count > 0;

    internal IReadOnlyList<Announcement> WithBuffered(IReadOnlyList<Announcement> stored) => [.. stored, .. _buffered];

    internal void Clear() => _buffered.Clear();

    internal async Task<IReadOnlyList<Announcement>> FireStoredAsync(IReadOnlyList<Announcement> stored, CancellationToken cancellationToken)
    {
        List<Announcement> remaining = [];
        foreach (var announcement in stored)
        {
            // Re-fire with the minted id so a lost activation yields Duplicate, not a second message.
            var outcome = await fire(announcement, cancellationToken).ConfigureAwait(true);
            if (outcome.Busy > 0)
            {
                remaining.Add(announcement);
            }
        }

        return remaining;
    }
}
