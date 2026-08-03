using System.Collections.Concurrent;

namespace DigitalBrain.Mcp;

internal static class AuthorizationCodeCustodyProbe
{
    private static readonly ConcurrentDictionary<string, byte[]> DurablePayloads =
        new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, byte[]> ProtectedCodes =
        new(StringComparer.Ordinal);

    internal static void Record(string state, byte[] durablePayload, byte[]? protectedCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(state);
        ArgumentNullException.ThrowIfNull(durablePayload);
        DurablePayloads[state] = durablePayload;
        if (protectedCode is { Length: > 0 })
        {
            ProtectedCodes[state] = protectedCode;
        }
        else
        {
            ProtectedCodes.TryRemove(state, out _);
        }
    }

    internal static bool TryGetDurablePayload(string state, out byte[] payload)
        => DurablePayloads.TryGetValue(state, out payload!);

    internal static bool TryGetProtectedCode(string state, out byte[] protectedCode)
        => ProtectedCodes.TryGetValue(state, out protectedCode!);
}
