using System.Collections;

namespace TripRadar.Server.Infrastructure.Providers.SerpApi.Extensions;

public static class HashtableExtensions
{
    public static Hashtable ConvertToString(this Hashtable parameters)
    {
        var stringParameters = new Hashtable();

        foreach (DictionaryEntry entry in parameters)
        {
            var key = entry.Key.ToString() ?? string.Empty;
            var value = entry.Value?.ToString() ?? string.Empty;

            if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value))
                stringParameters[key] = value;
        }

        return stringParameters;
    }
}
