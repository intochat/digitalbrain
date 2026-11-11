using System.Collections.Immutable;
using System.Text.Json;
using DigitalBrain.Invocation;
using DigitalBrain.Kernel;

namespace DigitalBrain.Runtime.Orleans.Storage;

internal static class SubscriptionUpgrade
{
    internal sealed class LegacySynapseState
    {
        public List<LegacySynapseSource> Sources { get; set; } = [];
    }

    internal sealed class LegacySynapseSource
    {
        public string Source { get; set; } = string.Empty;
        public List<string> Aliases { get; set; } = [];
        public long Cursor { get; set; }
    }

    public static Subscriptions? TryUpgrade(NeuronId subscriber, ReadOnlyMemory<byte> legacyBytes, DateTimeOffset now)
    {
        if (legacyBytes.IsEmpty) { return null; }
        try
        {
            var legacy = JsonSerializer.Deserialize<LegacySynapseState>(legacyBytes.Span, RuntimeProtocol.Json);
            if (legacy is null || legacy.Sources.Count == 0) { return Subscriptions.None(subscriber); }

            var entries = new List<Subscription>();
            foreach (var s in legacy.Sources)
            {
                var isWide = s.Source == "*" || string.IsNullOrEmpty(s.Source);
                NeuronId? sourceId = isWide ? null : (NeuronId.TryParse(s.Source, out var parsed) ? parsed : null);
                if (!isWide && sourceId is null) { continue; }
                if (sourceId == subscriber || (sourceId is { } src && src.Brain != subscriber.Brain)) { continue; }

                var aliases = ImmutableSortedSet.CreateRange(StringComparer.Ordinal, s.Aliases.Where(a => !string.IsNullOrWhiteSpace(a)));
                if (aliases.IsEmpty) { continue; }

                entries.Add(new Subscription(sourceId, aliases, new Position(s.Cursor), now));
            }

            return new Subscriptions(subscriber, [.. entries]);
        }
        catch
        {
            return null;
        }
    }
}
