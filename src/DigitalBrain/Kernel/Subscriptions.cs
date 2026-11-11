using System.Collections.Immutable;
using System.Text.Json;
using DigitalBrain.Invocation;

namespace DigitalBrain.Kernel;

internal sealed record Subscription(NeuronId? Source, ImmutableSortedSet<string> Aliases, Position Cursor, DateTimeOffset At)
{
    public bool IsBrainWide => Source is null;

    public bool Covers(NeuronId subscriber, NeuronId publisher)
        => Source is null || Source == publisher;

    public bool Wants(NeuronId subscriber, NeuronId publisher, string alias, Func<string, Type?> typeOf)
        => Covers(subscriber, publisher)
           && (Aliases.Contains(alias) || (typeOf(alias) is { } type && Aliases.Any(held => typeOf(held)?.IsAssignableFrom(type) == true)));
}

internal sealed record Subscriptions(NeuronId Subscriber, ImmutableList<Subscription> Entries)
{
    public const string RecordName = "subscriptions";

    public static Subscriptions None(NeuronId subscriber) => new(subscriber, []);

    public Subscription? Of(NeuronId? source) => Entries.Find(entry => entry.Source == source);

    public Subscriptions Add(NeuronId source, IEnumerable<string> aliases, Position at, DateTimeOffset now)
    {
        if (source == Subscriber) { throw new ArgumentException("A neuron does not subscribe to itself.", nameof(source)); }
        if (source.Brain != Subscriber.Brain) { throw new UnauthorizedAccessException($"Neuron {Subscriber} may not subscribe to {source} of another brain."); }
        var named = Named(aliases);
        if (Of(null) is { } wide) { named = named.Except(wide.Aliases); }
        if (Of(source) is not { } held)
        {
            return named.IsEmpty ? this : this with { Entries = Entries.Add(new Subscription(source, named, at, now)) };
        }
        return held.Aliases.IsSupersetOf(named) ? this : this with { Entries = Entries.Replace(held, held with { Aliases = held.Aliases.Union(named) }) };
    }

    public Subscriptions AddBrainWide(IEnumerable<string> aliases, Position at, DateTimeOffset now, bool exact = false)
    {
        var named = Named(aliases);
        var held = Of(null);
        var wide = held is null ? new Subscription(null, named, at, now) : held with { Aliases = exact ? named : held.Aliases.Union(named) };
        var overlap = Entries.Exists(entry => !entry.IsBrainWide && entry.Aliases.Overlaps(wide.Aliases));
        if (held is not null && !overlap && held.Aliases.SetEquals(wide.Aliases)) { return this; }
        var sources = Entries.Where(entry => !entry.IsBrainWide)
            .Select(entry => entry with { Aliases = entry.Aliases.Except(wide.Aliases) })
            .Where(entry => !entry.Aliases.IsEmpty);
        return this with { Entries = [.. sources, wide] };
    }

    public Subscriptions Remove(NeuronId? source) => Of(source) is { } held ? this with { Entries = Entries.Remove(held) } : this;

    public Subscriptions Advance(Subscription entry, Position position, DateTimeOffset now)
    {
        if (Of(entry.Source) is not { } held) { return this; }
        if (position.Value < held.Cursor.Value)
        {
            throw new InvalidOperationException($"The cursor of {Subscriber} on {entry.Source?.ToString() ?? "*"} never moves back.");
        }
        return position == held.Cursor ? this : this with { Entries = Entries.Replace(held, held with { Cursor = position, At = now }) };
    }

    public Subscriptions Retreat(Subscription before)
        => Of(before.Source) is { } held ? this with { Entries = Entries.Replace(held, held with { Cursor = before.Cursor, At = before.At }) } : this;

    public byte[] Encode() => JsonSerializer.SerializeToUtf8Bytes(this, RuntimeProtocol.Json);

    public static Subscriptions Decode(NeuronId subscriber, ReadOnlyMemory<byte> bytes)
    {
        if (bytes.IsEmpty) { return None(subscriber); }
        try
        {
            using var doc = JsonDocument.Parse(bytes);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return None(subscriber);
            }

            if (doc.RootElement.TryGetProperty("Sources", out var sourcesProp) || doc.RootElement.TryGetProperty("sources", out sourcesProp))
            {
                var entries = new List<Subscription>();
                if (sourcesProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var s in sourcesProp.EnumerateArray())
                    {
                        var srcStr = s.TryGetProperty("Source", out var sp) || s.TryGetProperty("source", out sp) ? sp.GetString() : null;
                        var isWide = srcStr == "*" || string.IsNullOrEmpty(srcStr);
                        NeuronId? sourceId = isWide ? null : (NeuronId.TryParse(srcStr, out var parsed) ? parsed : null);
                        if (!isWide && sourceId is null) { continue; }
                        if (sourceId == subscriber || (sourceId is { } src && src.Brain != subscriber.Brain)) { continue; }

                        var aliasesList = new List<string>();
                        if (s.TryGetProperty("Aliases", out var ap) || s.TryGetProperty("aliases", out ap))
                        {
                            if (ap.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var a in ap.EnumerateArray())
                                {
                                    var str = a.GetString();
                                    if (!string.IsNullOrWhiteSpace(str)) { aliasesList.Add(str); }
                                }
                            }
                        }
                        var aliases = ImmutableSortedSet.CreateRange(StringComparer.Ordinal, aliasesList);
                        if (aliases.IsEmpty) { continue; }

                        long cursor = 0;
                        if ((s.TryGetProperty("Cursor", out var cp) || s.TryGetProperty("cursor", out cp)) && cp.TryGetInt64(out var cVal))
                        {
                            cursor = cVal;
                        }
                        entries.Add(new Subscription(sourceId, aliases, new Position(cursor), DateTimeOffset.UtcNow));
                    }
                }
                return new Subscriptions(subscriber, [.. entries]);
            }

            var decoded = JsonSerializer.Deserialize<Subscriptions>(bytes.Span, RuntimeProtocol.Json) ?? None(subscriber);
            if (decoded.Entries is null) { return None(subscriber); }
            var valid = decoded.Entries.Where(e => e.Source is null || (e.Source.Value.Brain == subscriber.Brain && e.Source.Value != subscriber));
            return new Subscriptions(subscriber, [.. valid]);
        }
        catch (Exception)
        {
            return None(subscriber);
        }
    }

    private static ImmutableSortedSet<string> Named(IEnumerable<string> aliases)
    {
        var set = ImmutableSortedSet.CreateRange(StringComparer.Ordinal, aliases.Where(a => !string.IsNullOrWhiteSpace(a)));
        if (set.IsEmpty) { throw new ArgumentException("At least one non-empty signal alias is required.", nameof(aliases)); }
        return set;
    }
}
