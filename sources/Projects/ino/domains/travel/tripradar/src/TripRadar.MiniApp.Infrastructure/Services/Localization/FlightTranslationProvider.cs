using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace TripRadar.MiniApp.Client.Infrastructure.Services.Localization;

public sealed class FlightTranslationProvider
{
    private readonly Dictionary<string, Dictionary<string, string>> _cities;
    private readonly Dictionary<string, Dictionary<string, string>> _airports;

    public FlightTranslationProvider()
    {
        var assembly = typeof(FlightTranslationProvider).Assembly;
        var resourceNames = assembly.GetManifestResourceNames();
        var resourceName = resourceNames
            .FirstOrDefault(n => n.EndsWith("flight-translations.json", StringComparison.OrdinalIgnoreCase));

        if (resourceName is null)
        {
            var availableResources = resourceNames.Length == 0
                ? "no embedded resources"
                : string.Join(", ", resourceNames);

            throw new InvalidOperationException(
                $"Embedded resource 'flight-translations.json' was not found in '{assembly.GetName().Name}'. Available resources: {availableResources}.");
        }

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        var root = JsonSerializer.Deserialize<JsonElement>(stream);

        _cities = ParseSection(root, "cities");
        _airports = ParseSection(root, "airports");
    }

    public string GetCityName(string? englishName)
    {
        if (string.IsNullOrWhiteSpace(englishName))
            return string.Empty;

        var lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        if (lang == "en")
            return englishName;

        if (_cities.TryGetValue(englishName, out var translations)
            && translations.TryGetValue(lang, out var localized))
            return localized;

        return englishName;
    }

    public string GetAirportName(string? iataCode, string? fallbackName = null)
    {
        if (string.IsNullOrWhiteSpace(iataCode))
            return fallbackName ?? string.Empty;

        var code = iataCode.Trim().ToUpperInvariant();
        var lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

        if (_airports.TryGetValue(code, out var translations)
            && translations.TryGetValue(lang, out var localized))
            return localized;

        return fallbackName ?? iataCode;
    }

    private static Dictionary<string, Dictionary<string, string>> ParseSection(JsonElement root, string section)
    {
        var entries = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        if (!root.TryGetProperty(section, out var sectionElement))
            return entries;

        foreach (var entry in sectionElement.EnumerateObject())
        {
            var langMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var lang in entry.Value.EnumerateObject())
                langMap[lang.Name] = lang.Value.GetString()!;
            entries[entry.Name] = langMap;
        }

        return entries;
    }
}
