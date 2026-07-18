using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TripRadar.Server.Db.Constants;
using TripRadar.Server.Db.Models;

namespace TripRadar.Server.Db.Seeding;

public static partial class DbSeeder
{
    private static async Task SeedAirlinesAsync(SetupDbContext context)
    {
        var path = ResolveSeedDataPath(DbConstants.SeedFiles.Airlines);
        var json = await File.ReadAllTextAsync(path);
        var seedData = JsonSerializer.Deserialize<List<AirlineSeedRecord>>(json, SeedJsonOptions);

        if (seedData is null || seedData.Count == 0)
        {
            return;
        }

        var expectedAirlines = seedData
            .Where(record => !string.IsNullOrWhiteSpace(record.AirlineCode) && !string.IsNullOrWhiteSpace(record.AirlineName))
            .Select(record => new Airlines
            {
                AirlineCode = record.AirlineCode.Trim().ToUpperInvariant(),
                AirlineName = record.AirlineName.Trim(),
                SearchAliases = NormalizeAliases(record.SearchAliases),
                IsAlliance = record.IsAlliance,
                IsActive = record.IsActive
            })
            .GroupBy(record => record.AirlineCode, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        if (expectedAirlines.Count == 0)
        {
            return;
        }

        var existingAirlines = await context.Airlines
            .ToDictionaryAsync(record => record.AirlineCode, StringComparer.OrdinalIgnoreCase);

        var airlinesToAdd = new List<Airlines>();
        var hasChanges = false;

        foreach (var expectedAirline in expectedAirlines)
        {
            if (existingAirlines.TryGetValue(expectedAirline.AirlineCode, out var existingAirline))
            {
                if (!string.Equals(existingAirline.AirlineName, expectedAirline.AirlineName, StringComparison.Ordinal)
                    || !string.Equals(existingAirline.SearchAliases, expectedAirline.SearchAliases, StringComparison.Ordinal)
                    || existingAirline.IsAlliance != expectedAirline.IsAlliance
                    || existingAirline.IsActive != expectedAirline.IsActive)
                {
                    existingAirline.AirlineName = expectedAirline.AirlineName;
                    existingAirline.SearchAliases = expectedAirline.SearchAliases;
                    existingAirline.IsAlliance = expectedAirline.IsAlliance;
                    existingAirline.IsActive = expectedAirline.IsActive;
                    hasChanges = true;
                }

                continue;
            }

            airlinesToAdd.Add(expectedAirline);
        }

        if (airlinesToAdd.Count != 0)
        {
            await context.Airlines.AddRangeAsync(airlinesToAdd);
            hasChanges = true;
        }

        if (hasChanges)
        {
            await context.SaveChangesAsync();
        }
    }

    private static string? NormalizeAliases(IReadOnlyList<string>? aliases)
    {
        if (aliases is null || aliases.Count == 0)
        {
            return null;
        }

        var normalizedAliases = aliases
            .Where(alias => !string.IsNullOrWhiteSpace(alias))
            .Select(alias => alias.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return normalizedAliases.Count == 0 ? null : string.Join('|', normalizedAliases);
    }

    private sealed class AirlineSeedRecord
    {
        public string AirlineCode { get; set; } = string.Empty;

        public string AirlineName { get; set; } = string.Empty;

        public List<string>? SearchAliases { get; set; }

        public bool IsAlliance { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
