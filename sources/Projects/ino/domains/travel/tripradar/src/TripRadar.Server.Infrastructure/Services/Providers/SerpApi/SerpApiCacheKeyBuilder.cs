using System.Collections;
using System.Globalization;

namespace TripRadar.Server.Infrastructure.Services.Providers.SerpApi;

internal static class SerpApiCacheKeyBuilder
{
    private static readonly HashSet<string> _sensitiveQueryParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "api_key",
        "apikey",
        "apiKey"
    };

    public static string Build(string providerName, Hashtable queryParams)
    {
        var keyParts = new List<string> { providerName.ToLowerInvariant() };

        var sortedParams = queryParams.Cast<DictionaryEntry>()
            .Select(entry => (Key: Convert.ToString(entry.Key, CultureInfo.InvariantCulture), entry.Value))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Key))
            .Where(entry => !_sensitiveQueryParameters.Contains(entry.Key!))
            .Where(entry => entry.Value is not null)
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry =>
            {
                var encodedKey = Uri.EscapeDataString(entry.Key!);
                var encodedValue = Uri.EscapeDataString(ConvertToCacheValue(entry.Value));
                return $"{encodedKey}={encodedValue}";
            });

        keyParts.AddRange(sortedParams);
        return string.Join("&", keyParts);
    }

    private static string ConvertToCacheValue(object? value) => value switch
    {
        null => string.Empty,
        string stringValue => stringValue,
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };
}
