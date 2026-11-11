namespace DigitalBrain.Kernel;

internal sealed class SubscriptionIndex
{
    private readonly Dictionary<NeuronId, Subscriptions> _all = [];
    private readonly Dictionary<NeuronId, string> _owners = [];
    private readonly Lock _gate = new();

    public IReadOnlyCollection<Subscriptions> All { get { lock (_gate) { return [.. _all.Values]; } } }

    public void Fold(NeuronId subscriber, ReadOnlyMemory<byte> bytes, StateChange change, string? owner = null)
    {
        lock (_gate)
        {
            if (change == StateChange.Clear)
            {
                _all.Remove(subscriber);
                _owners.Remove(subscriber);
                return;
            }
            _all[subscriber] = Subscriptions.Decode(subscriber, bytes);
            if (!string.IsNullOrEmpty(owner))
            {
                _owners[subscriber] = owner;
            }
        }
    }

    public void Fold(Subscriptions subscriptions, string? owner = null)
    {
        lock (_gate)
        {
            _all[subscriptions.Subscriber] = subscriptions;
            if (!string.IsNullOrEmpty(owner))
            {
                _owners[subscriptions.Subscriber] = owner;
            }
        }
    }

    public string? OwnerOf(NeuronId subscriber)
    {
        lock (_gate)
        {
            return _owners.GetValueOrDefault(subscriber);
        }
    }

    public Subscriptions? Of(NeuronId subscriber)
    {
        lock (_gate)
        {
            return _all.GetValueOrDefault(subscriber);
        }
    }

    public IEnumerable<NeuronId> Hearing(NeuronId publisher, IReadOnlyCollection<string> aliases, Func<string, Type?> typeOf)
    {
        lock (_gate)
        {
            return [.. _all.Values
                .Where(subs => subs.Entries.Exists(e => e.Covers(subs.Subscriber, publisher) && aliases.Any(a => e.Wants(subs.Subscriber, publisher, a, typeOf))))
                .Select(subs => subs.Subscriber)];
        }
    }

    public IReadOnlyList<NeuronId> Due(IJournalView journal, Func<string, Type?> typeOf, long slack)
    {
        lock (_gate)
        {
            return [.. _all.Values.Where(held => held.Entries.Exists(entry =>
                    journal.FloorOf(entry.Source).Value > entry.Cursor.Value
                    || ((entry.Source is { } source ? journal.LastOf(source) : journal.Last).Value > entry.Cursor.Value
                        && journal.Last.Value - entry.Cursor.Value >= slack)
                    || journal.Marks(entry.Source).Any(mark => mark.Last.Value > entry.Cursor.Value && entry.Wants(held.Subscriber, mark.Publisher, mark.Alias, typeOf))))
                .Select(held => held.Subscriber)];
        }
    }

    public Position? LowestLive(NeuronId publisher, DateTimeOffset threshold)
    {
        lock (_gate)
        {
            var positions = _all.Values.SelectMany(s => s.Entries)
                .Where(e => (e.Source is null || e.Source == publisher) && e.At >= threshold)
                .Select(e => e.Cursor)
                .ToList();
            return positions.Count == 0 ? null : positions.Min();
        }
    }
}
