using System.Globalization;

namespace DigitalBrain;

internal static class NeuronKey
{
    internal static string Encode(NeuronId id)
        => string.Concat(id.Kind.Length.ToString(CultureInfo.InvariantCulture), ":", id.Kind, id.Name);

    internal static NeuronId Decode(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var separator = value.AsSpan().IndexOf(':');
        if (separator < 0
            || !int.TryParse(value.AsSpan(0, separator), CultureInfo.InvariantCulture, out var length)
            || length < 0
            || value.Length - separator - 1 < length)
        {
            throw new InvalidOperationException("Invalid encoded neuron key.");
        }

        var start = separator + 1;
        return new NeuronId(value.Substring(start, length), value[(start + length)..]);
    }
}
