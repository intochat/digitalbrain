namespace TripRadar.Server.API.HealthChecks;

internal static class EndpointParser
{
    public static bool TryParseHostPort(string? rawValue, int defaultPort, out string host, out int port)
    {
        host = string.Empty;
        port = defaultPort;

        if (string.IsNullOrWhiteSpace(rawValue)) return false;

        var firstEndpoint = rawValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();

        if (string.IsNullOrWhiteSpace(firstEndpoint)) return false;

        var normalizedEndpoint = firstEndpoint;
        var schemeSeparatorIndex = normalizedEndpoint.IndexOf("://", StringComparison.Ordinal);
        if (schemeSeparatorIndex >= 0) normalizedEndpoint = normalizedEndpoint[(schemeSeparatorIndex + 3)..];

        var slashIndex = normalizedEndpoint.IndexOf('/');
        if (slashIndex >= 0) normalizedEndpoint = normalizedEndpoint[..slashIndex];

        var atIndex = normalizedEndpoint.LastIndexOf('@');
        if (atIndex >= 0) normalizedEndpoint = normalizedEndpoint[(atIndex + 1)..];

        if (!Uri.TryCreate($"tcp://{normalizedEndpoint}", UriKind.Absolute, out var parsedUri)) return false;

        if (string.IsNullOrWhiteSpace(parsedUri.Host)) return false;

        host = parsedUri.Host;
        port = parsedUri.Port > 0 ? parsedUri.Port : defaultPort;
        return true;
    }
}
