using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TripRadar.Server.Db.Constants;
using TripRadar.Server.Db.Models;

namespace TripRadar.Server.Db.Seeding;

public static partial class DbSeeder
{
    private static async Task SeedAirportsAsync(SetupDbContext context)
    {
        var path = ResolveSeedDataPath(DbConstants.SeedFiles.Airports);
        var json = await File.ReadAllTextAsync(path);
        var seedData = JsonSerializer.Deserialize<List<Airports>>(json, SeedJsonOptions);

        if (seedData is null || seedData.Count == 0)
        {
            return;
        }

        var expectedAirports = seedData
            .Where(record => !string.IsNullOrWhiteSpace(record.Code)
                          && !string.IsNullOrWhiteSpace(record.Name)
                          && !string.IsNullOrWhiteSpace(record.City)
                          && !string.IsNullOrWhiteSpace(record.Country))
            .Select(record => new Airports
            {
                Code = record.Code.Trim().ToUpperInvariant(),
                Name = record.Name.Trim().ToLowerInvariant(),
                City = record.City.Trim().ToLowerInvariant(),
                Country = record.Country.Trim().ToLowerInvariant(),
                Latitude = record.Latitude,
                Longitude = record.Longitude,
                AirportType = record.AirportType?.Trim().ToLowerInvariant(),
                SearchAliases = NormalizeAirportAliases(record.SearchAliases)
            })
            .GroupBy(record => record.Code)
            .Select(group => group.First())
            .ToList();

        if (expectedAirports.Count == 0)
        {
            return;
        }

        var existingAirports = await context.Airports
            .ToDictionaryAsync(record => record.Code, StringComparer.OrdinalIgnoreCase);

        var airportsToAdd = new List<Airports>();
        var hasChanges = false;

        foreach (var expectedAirport in expectedAirports)
        {
            if (existingAirports.TryGetValue(expectedAirport.Code, out var existingAirport))
            {
                if (!string.Equals(existingAirport.Code, expectedAirport.Code, StringComparison.Ordinal)
                    || !string.Equals(existingAirport.Name, expectedAirport.Name, StringComparison.Ordinal)
                    || !string.Equals(existingAirport.City, expectedAirport.City, StringComparison.Ordinal)
                    || !string.Equals(existingAirport.Country, expectedAirport.Country, StringComparison.Ordinal)
                    || existingAirport.Latitude != expectedAirport.Latitude
                    || existingAirport.Longitude != expectedAirport.Longitude
                    || !string.Equals(existingAirport.AirportType, expectedAirport.AirportType, StringComparison.Ordinal)
                    || !string.Equals(existingAirport.SearchAliases, expectedAirport.SearchAliases, StringComparison.Ordinal))
                {
                    existingAirport.Code = expectedAirport.Code;
                    existingAirport.Name = expectedAirport.Name;
                    existingAirport.City = expectedAirport.City;
                    existingAirport.Country = expectedAirport.Country;
                    existingAirport.Latitude = expectedAirport.Latitude;
                    existingAirport.Longitude = expectedAirport.Longitude;
                    existingAirport.AirportType = expectedAirport.AirportType;
                    existingAirport.SearchAliases = expectedAirport.SearchAliases;
                    hasChanges = true;
                }

                continue;
            }

            airportsToAdd.Add(expectedAirport);
        }

        if (airportsToAdd.Count != 0)
        {
            await context.Airports.AddRangeAsync(airportsToAdd);
            hasChanges = true;
        }

        if (hasChanges)
        {
            await context.SaveChangesAsync();
        }
    }

    private static string? NormalizeAirportAliases(string? aliases)
    {
        if (string.IsNullOrWhiteSpace(aliases))
        {
            return null;
        }

        var normalizedAliases = aliases
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return normalizedAliases.Count == 0 ? null : string.Join('|', normalizedAliases);
    }
}

