using Qdrant.Client;

namespace DigitalBrain.Qdrant;

public static class QdrantRegistration
{
    public const string ConnectionNameConfigurationKey = "DigitalBrain:Qdrant:ConnectionName";
    public const string DefaultConnectionName = "qdrant";

    internal static QdrantClient CreateClient(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        if (!TryParseConnectionString(connectionString, out var endpoint, out var apiKey))
        {
            throw new InvalidOperationException("Qdrant connection string must include Endpoint and an optional Key.");
        }

        return string.IsNullOrEmpty(apiKey)
            ? new QdrantClient(endpoint)
            : new QdrantClient(endpoint, apiKey: apiKey);
    }

    internal static bool TryParseConnectionString(string connectionString, out Uri endpoint, out string? apiKey)
    {
        ArgumentNullException.ThrowIfNull(connectionString);
        endpoint = null!;
        apiKey = null;
        if (Uri.TryCreate(connectionString, UriKind.Absolute, out var absolute)
            && absolute.Scheme is "http" or "https")
        {
            endpoint = absolute;
            return true;
        }

        string? endpointValue = null;
        foreach (var segment in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = segment.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            var key = segment[..separator].Trim();
            var value = segment[(separator + 1)..].Trim();
            if (key.Equals("Endpoint", StringComparison.OrdinalIgnoreCase) || key.Equals("Uri", StringComparison.OrdinalIgnoreCase))
            {
                endpointValue = value;
            }
            else if (key.Equals("Key", StringComparison.OrdinalIgnoreCase) || key.Equals("ApiKey", StringComparison.OrdinalIgnoreCase))
            {
                apiKey = value;
            }
        }

        return endpointValue is not null && Uri.TryCreate(endpointValue, UriKind.Absolute, out endpoint!);
    }
}