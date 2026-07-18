using System.Net.Http.Headers;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Web;

const string sparql = """
    SELECT DISTINCT ?iata ?airportEn ?airportRu ?cityEn ?cityRu WHERE {
      ?airport wdt:P238 ?iata .
      ?airport wdt:P931 ?city .
      ?airport rdfs:label ?airportEn . FILTER(LANG(?airportEn)="en")
      ?airport rdfs:label ?airportRu . FILTER(LANG(?airportRu)="ru")
      ?city rdfs:label ?cityEn . FILTER(LANG(?cityEn)="en")
      ?city rdfs:label ?cityRu . FILTER(LANG(?cityRu)="ru")
      FILTER(STRLEN(?iata)=3)
    }
    ORDER BY ?iata
    """;

Console.WriteLine("Fetching airport translations from Wikidata SPARQL...");

using var http = new HttpClient();
http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("TripRadar-TranslationGen", "1.0"));
http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
http.Timeout = TimeSpan.FromMinutes(5);

var url = $"https://query.wikidata.org/sparql?format=json&query={HttpUtility.UrlEncode(sparql)}";
var response = await http.GetStringAsync(url);
var doc = JsonDocument.Parse(response);
var bindings = doc.RootElement.GetProperty("results").GetProperty("bindings");

var cities = new SortedDictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
var airports = new SortedDictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

var totalBindings = 0;

foreach (var b in bindings.EnumerateArray())
{
    totalBindings++;
    var iata = b.GetProperty("iata").GetProperty("value").GetString()!.Trim().ToUpperInvariant();
    var airportEn = b.GetProperty("airportEn").GetProperty("value").GetString()!;
    var airportRu = b.GetProperty("airportRu").GetProperty("value").GetString()!;
    var cityEn = b.GetProperty("cityEn").GetProperty("value").GetString()!;
    var cityRu = b.GetProperty("cityRu").GetProperty("value").GetString()!;

    if (iata.Length != 3) continue;

    // Cities: keyed by English name, skip if names are identical
    if (!cities.ContainsKey(cityEn) && cityEn != cityRu)
        cities[cityEn] = new() { ["ru"] = cityRu };

    // Also add short variant without common suffixes (e.g. "New York City" → "New York")
    var shortCity = StripCitySuffix(cityEn);
    if (shortCity != cityEn && !cities.ContainsKey(shortCity))
        cities[shortCity] = new() { ["ru"] = cityRu };

    // Airports: keyed by IATA code
    if (airports.ContainsKey(iata)) continue;

    var shortEn = CleanAirportName(airportEn);
    var shortRu = CleanAirportNameRu(airportRu);

    airports[iata] = new() { ["en"] = shortEn, ["ru"] = shortRu };
}

Console.WriteLine($"Wikidata returned {totalBindings} bindings");
Console.WriteLine($"Extracted {cities.Count} unique cities, {airports.Count} unique airports");

var output = new Dictionary<string, object>
{
    ["cities"] = cities,
    ["airports"] = airports
};

var options = new JsonSerializerOptions
{
    WriteIndented = true,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
};

var outputPath = Path.GetFullPath(
    args.Length > 0
        ? args[0]
        : Path.Combine("..", "TripRadar.Localization", "flight-translations.json"));

var json = JsonSerializer.Serialize(output, options);
File.WriteAllText(outputPath, json);
Console.WriteLine($"Written to {outputPath}");

static string CleanAirportName(string name) =>
    name.Replace(" International Airport", "")
        .Replace(" international airport", "")
        .Replace(" Airport", "")
        .Replace(" airport", "")
        .Trim();

static string CleanAirportNameRu(string name) =>
    name.Replace("Международный аэропорт ", "")
        .Replace("международный аэропорт ", "")
        .Replace("Аэропорт ", "")
        .Replace("аэропорт ", "")
        .Trim();

static string StripCitySuffix(string name)
{
    string[] suffixes = [" City", " Metropolitan Area", " metropolitan area", " Municipality"];
    foreach (var suffix in suffixes)
    {
        if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && name.Length > suffix.Length)
            return name[..^suffix.Length];
    }
    return name;
}
