using System.Collections.Concurrent;

namespace DigitalBrain;

internal static class SpeechDepthCarrier
{
    private static readonly ConcurrentDictionary<string, int> Pending = new(StringComparer.Ordinal);

    internal static void Stage(NeuronId source, long sequence, int depth)
        => Pending[Key(source, sequence)] = Math.Max(1, depth);

    internal static int Take(NeuronId source, long sequence)
        => Pending.TryRemove(Key(source, sequence), out var depth) && depth > 0 ? depth : 0;

    private static string Key(NeuronId source, long sequence)
        => string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{source.Kind}/{source.Name}#{sequence}");
}
