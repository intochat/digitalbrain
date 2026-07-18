using System.Reflection;
using System.Text.Json;
using TripRadar.Server.Application.Contracts.Services;

namespace TripRadar.Server.Infrastructure.Services;

public sealed class CityTranslationProvider : ICityTranslationProvider
{
    private static readonly Lazy<Dictionary<string, string>> Translations = new(LoadTranslations);

    public string? GetEnglishCityName(string localizedName)
    {
        return Translations.Value.TryGetValue(localizedName.Trim(), out var englishName)
            ? englishName
            : null;
    }

    private static Dictionary<string, string> LoadTranslations()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith("flight-translations.json", StringComparison.OrdinalIgnoreCase));

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        var root = JsonSerializer.Deserialize<JsonElement>(stream);

        var reversed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (root.TryGetProperty("cities", out var cities))
        {
            foreach (var city in cities.EnumerateObject())
            {
                var englishName = city.Name;
                foreach (var lang in city.Value.EnumerateObject())
                    reversed.TryAdd(lang.Value.GetString()!, englishName);
            }
        }

        return reversed;
    }
}
