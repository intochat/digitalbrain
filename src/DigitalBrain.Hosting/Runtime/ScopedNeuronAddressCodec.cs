using System.Globalization;

namespace DigitalBrain;

internal static class ScopedNeuronAddressCodec
{
    private const string Prefix = "digitalbrain.scope.v1|";

    internal static string Encode(ScopedNeuronAddress address)
        => string.Concat(
            Prefix,
            address.Scope.Value.Length.ToString(CultureInfo.InvariantCulture),
            ":",
            address.Scope.Value,
            NeuronKey.Encode(address.Neuron));

    internal static ScopedNeuronAddress Decode(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!value.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Invalid encoded scoped neuron address.");
        }

        var offset = Prefix.Length;
        var scope = TakeSegment(value, ref offset);
        return new ScopedNeuronAddress(new ScopeKey(scope), NeuronKey.Decode(value[offset..]));
    }

    private static string TakeSegment(string value, ref int offset)
    {
        var remaining = value.AsSpan(offset);
        var separator = remaining.IndexOf(':');
        if (separator < 0
            || !int.TryParse(remaining[..separator], CultureInfo.InvariantCulture, out var length)
            || length <= 0)
        {
            throw new InvalidOperationException("Invalid encoded scoped neuron address.");
        }

        var start = offset + separator + 1;
        if (value.Length - start < length)
        {
            throw new InvalidOperationException("Invalid encoded scoped neuron address.");
        }

        offset = start + length;
        return value.Substring(start, length);
    }
}
