using System.Text.Json.Serialization;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Introspection;

internal static class IntrospectionIdentity
{
    private const char GrainKeySeparator = '/';

    internal static string Validated(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        var trimmed = value.Trim();
        if (trimmed.Contains(GrainKeySeparator, StringComparison.Ordinal) || trimmed.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException(
                $"A neuron identity part cannot contain '{GrainKeySeparator}' or whitespace; "
                + $"'{value}' is not addressable.",
                parameterName);
        }

        return trimmed;
    }
}

