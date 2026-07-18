using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TripRadar.Server.Db.Constants;
using TripRadar.Server.Db.Models;

namespace TripRadar.Server.Db.Seeding;

public static partial class DbSeeder
{
    private static readonly JsonSerializerOptions SeedJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public static async Task SeedAsync(IConfiguration configuration, SetupDbContext context)
    {
        // Seeds Serp API values
        await SeedAirportsAsync(context);

        await SeedDataFromJsonAsync(context, context.Countries, DbConstants.SeedFiles.Countries,
            (List<Countries> seedData) =>
                seedData
                    .Where(record => !string.IsNullOrWhiteSpace(record.CountryCode))
                    .Select(record => new Countries
                    {
                        CountryCode = record.CountryCode.Trim().ToLowerInvariant(),
                        CountryName = record.CountryName.Trim().ToLowerInvariant()
                    })
                    .GroupBy(record => record.CountryCode)
                    .Select(group => group.First()));

        await SeedDataFromJsonAsync(context, context.Languages, DbConstants.SeedFiles.Languages,
            (List<Languages> seedData) =>
                seedData
                    .Where(record => !string.IsNullOrWhiteSpace(record.LanguageCode))
                    .Select(record => new Languages
                    {
                        LanguageCode = record.LanguageCode.Trim().ToLowerInvariant(),
                        LanguageName = record.LanguageName.Trim().ToLowerInvariant()
                    })
                    .GroupBy(record => record.LanguageCode)
                    .Select(group => group.First()));

        await SeedDataFromJsonAsync(context, context.GoogleLrLanguages, DbConstants.SeedFiles.GoogleLrLanguages,
            (List<GoogleLrLanguages> seedData) =>
                seedData
                    .Where(record => !string.IsNullOrWhiteSpace(record.LanguageCode))
                    .Select(record => new GoogleLrLanguages
                    {
                        LanguageCode = record.LanguageCode.Trim().ToLowerInvariant(),
                        LanguageName = record.LanguageName.Trim().ToLowerInvariant()
                    })
                    .GroupBy(record => record.LanguageCode)
                    .Select(group => group.First()));

        await SeedDataFromJsonAsync(context, context.Domains, DbConstants.SeedFiles.Domains,
            (List<Domains> seedData) =>
                seedData
                    .Where(record => !string.IsNullOrWhiteSpace(record.Domain)
                                     && !string.IsNullOrWhiteSpace(record.LanguageCode)
                                     && !string.IsNullOrWhiteSpace(record.CountryCode)
                                     && !string.IsNullOrWhiteSpace(record.CountryName))
                    .Select(record => new Domains
                    {
                        Domain = record.Domain.Trim().ToLowerInvariant(),
                        LanguageCode = record.LanguageCode.Trim().ToLowerInvariant(),
                        CountryCode = record.CountryCode.Trim().ToLowerInvariant(),
                        CountryName = record.CountryName.Trim().ToLowerInvariant()
                    })
                    .GroupBy(record => record.Domain)
                    .Select(group => group.First()));

        await SeedDataFromJsonAsync(context, context.TripAdvisorDomains, DbConstants.SeedFiles.TripAdvisorDomains,
            (Dictionary<string, TripAdvisorDomains> seedData) =>
                seedData
                    .Where(kvp =>
                        !string.IsNullOrWhiteSpace(kvp.Key)
                        && !string.IsNullOrWhiteSpace(kvp.Value.Title)
                        && !string.IsNullOrWhiteSpace(kvp.Value.Locale))
                    .Select(kvp => new TripAdvisorDomains
                    {
                        Domain = kvp.Key.Trim().ToLowerInvariant(),
                        Title = kvp.Value.Title.Trim(),
                        Locale = kvp.Value.Locale.Trim()
                    })
                    .GroupBy(record => record.Domain)
                    .Select(group => group.First()));

        await SeedDataFromJsonAsync(context, context.OpenTableDomains, DbConstants.SeedFiles.OpenTableDomains,
            (Dictionary<string, string> seedData) =>
                seedData
                    .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Key) && !string.IsNullOrWhiteSpace(kvp.Value))
                    .Select(kvp => new OpenTableDomains
                    {
                        Domain = kvp.Key.Trim().ToLowerInvariant(),
                        Country = kvp.Value.Trim()
                    })
                    .GroupBy(record => record.Domain)
                    .Select(group => group.First()));

        await SeedDataFromJsonAsync(context, context.YelpDomains, DbConstants.SeedFiles.YelpDomains,
            (Dictionary<string, string> seedData) =>
                seedData
                    .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Key) && !string.IsNullOrWhiteSpace(kvp.Value))
                    .Select(kvp => new YelpDomains
                    {
                        Domain = kvp.Key.Trim().ToLowerInvariant(),
                        Locale = kvp.Value.Trim()
                    })
                    .GroupBy(record => record.Domain)
                    .Select(group => group.First()));

        await SeedDataFromJsonAsync(context, context.YelpReviewLanguages, DbConstants.SeedFiles.YelpReviewLanguages,
            (Dictionary<string, string> seedData) =>
                seedData
                    .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Key) && !string.IsNullOrWhiteSpace(kvp.Value))
                    .Select(kvp => new YelpReviewLanguages
                    {
                        LanguageCode = kvp.Key.Trim().ToLowerInvariant(),
                        LanguageName = kvp.Value.Trim()
                    })
                    .GroupBy(record => record.LanguageCode)
                    .Select(group => group.First()));

        await SeedDataFromJsonAsync(context, context.Currencies, DbConstants.SeedFiles.Currencies,
            (Dictionary<string, string> seedData) =>
                seedData
                    .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Key) && !string.IsNullOrWhiteSpace(kvp.Value))
                    .Select(kvp => new Currencies
                    {
                        CurrencyCode = kvp.Key.Trim().ToLowerInvariant(),
                        CurrencyName = kvp.Value.Trim().ToLowerInvariant()
                    })
                    .GroupBy(record => record.CurrencyCode)
                    .Select(group => group.First()));

        await SeedAirlinesAsync(context);

        var existingCountryCodes = context.Countries
            .AsNoTracking()
            .Select(c => c.CountryCode.ToLowerInvariant())
            .ToHashSet();

        await SeedDataFromJsonAsync(context, context.Locations, DbConstants.SeedFiles.Locations,
            (List<Locations> seedData) =>
                seedData
                    .Where(record => !string.IsNullOrWhiteSpace(record.CountryCode) && !string.IsNullOrWhiteSpace(record.Name) &&
                                     existingCountryCodes.Contains(record.CountryCode.Trim().ToLowerInvariant()))
                    .Select(record => new Locations
                    {
                        LocationId = record.LocationId,
                        RowId = record.RowId?.Trim(),
                        GoogleId = record.GoogleId,
                        GoogleParentId = record.GoogleParentId,
                        Name = record.Name.Trim().ToLowerInvariant(),
                        CanonicalName = record.CanonicalName.Trim().ToLowerInvariant(),
                        CountryCode = record.CountryCode.Trim().ToLowerInvariant(),
                        TargetType = record.TargetType.Trim().ToLowerInvariant(),
                        Reach = record.Reach,
                        GpsLongitude = record.GpsLongitude,
                        GpsLatitude = record.GpsLatitude
                    }));

        // Seeds Domain values
        SeedTimezones(context);
        SeedTiers(context);
        SeedBillingPeriods(context);
        SeedPrices(configuration, context);
        SeedFeedbackCategories(context);
        SeedPreferenceCategories(context);
        SeedServiceTypes(context);
        SeedUsageEventSources(context);
        SeedServiceTokenCosts(context);
        SeedOveragePricing(context);
        SeedPreferenceTypes(context);
        SeedDiscountTypes(context);
        SeedPromoCodes(context);
    }
}

