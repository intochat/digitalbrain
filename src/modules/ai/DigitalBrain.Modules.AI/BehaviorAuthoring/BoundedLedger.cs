namespace DigitalBrain.AI;

// Eviction is oldest-first, and the order is carried explicitly: a Dictionary reuses the slots
// freed by a removal, so its key enumeration stops matching insertion order the moment anything
// has been evicted or settled, and "the first key" would then drop an arbitrary entry.
[GenerateSerializer]
internal sealed record BoundedLedger<TKey, TValue>
    where TKey : notnull
{
    [Id(0)]
    public Dictionary<TKey, TValue> Entries { get; init; } = [];

    [Id(1)]
    public List<TKey> Order { get; init; } = [];

    public bool TryGet(TKey key, out TValue value) => Entries.TryGetValue(key, out value!);

    public BoundedLedger<TKey, TValue> With(TKey key, TValue value, int bound)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(bound, 1);

        var entries = new Dictionary<TKey, TValue>(Entries) { [key] = value };
        var order = Order.Where(retained => !EqualityComparer<TKey>.Default.Equals(retained, key)).ToList();
        order.Add(key);

        while (order.Count > bound)
        {
            entries.Remove(order[0]);
            order.RemoveAt(0);
        }

        return new() { Entries = entries, Order = order };
    }

    public BoundedLedger<TKey, TValue> Without(TKey key)
    {
        if (!Entries.ContainsKey(key))
        {
            return this;
        }

        var entries = new Dictionary<TKey, TValue>(Entries);
        entries.Remove(key);

        return new()
        {
            Entries = entries,
            Order = [.. Order.Where(retained => !EqualityComparer<TKey>.Default.Equals(retained, key))],
        };
    }
}
