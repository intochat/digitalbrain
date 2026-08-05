namespace DigitalBrain.Testing;

// Journal evidence over the public read shape only: a reading either holds the asserted
// line or the failure message quotes what it held instead — the journal is the proof, so
// the proof failure must read like the journal.
public static class JournalAssertions
{
    private const string Heard = "heard";
    private const string Said = "said";

    public static JournalFact HeardSingle<TFact>(this NeuronReading reading)
        where TFact : Synapse
        => Single<TFact>(reading, Heard);

    public static JournalFact SaidSingle<TFact>(this NeuronReading reading)
        where TFact : Synapse
        => Single<TFact>(reading, Said);

    public static IReadOnlyList<JournalFact> AllHeard<TFact>(this NeuronReading reading)
        where TFact : Synapse
        => All<TFact>(reading, Heard);

    public static IReadOnlyList<JournalFact> AllSaid<TFact>(this NeuronReading reading)
        where TFact : Synapse
        => All<TFact>(reading, Said);

    public static Delivery DeliveryTo(this JournalFact said, NeuronId receiver)
        => DeliveryToOrNull(said, receiver)
            ?? throw new InvalidOperationException(
                $"The said '{said.Kind}' at position {said.Position} does not deliver to {receiver}; "
                + $"its receivers are [{string.Join(", ", said.To?.Select(Render) ?? [])}].");

    public static Delivery? DeliveryToOrNull(this JournalFact said, NeuronId receiver)
    {
        ArgumentNullException.ThrowIfNull(said);
        return said.To?.FirstOrDefault(delivery => delivery.Receiver == receiver);
    }

    private static IReadOnlyList<JournalFact> All<TFact>(NeuronReading reading, string entry)
        where TFact : Synapse
    {
        ArgumentNullException.ThrowIfNull(reading);
        return [.. reading.Journal.Where(fact => fact.Entry == entry && fact.Body is TFact)];
    }

    private static JournalFact Single<TFact>(NeuronReading reading, string entry)
    {
        ArgumentNullException.ThrowIfNull(reading);

        var matches = reading.Journal
            .Where(fact => fact.Entry == entry && fact.Body is TFact)
            .ToArray();

        return matches.Length == 1
            ? matches[0]
            : throw new InvalidOperationException(
                $"Expected exactly one {entry} {typeof(TFact).Name}, found {matches.Length}; "
                + $"the journal holds [{string.Join(", ", reading.Journal.Select(fact => $"{fact.Entry} {fact.Kind}"))}].");
    }

    private static string Render(Delivery delivery) => $"{delivery.Receiver} via {delivery.Via}";
}
