using Microsoft.EntityFrameworkCore;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Infrastructure.Database;

namespace TripRadar.Server.Infrastructure.Repositories;

public class AirportRepository(TripRadarDbContext dbContext) : Repository<Airport>(dbContext), IAirportRepository
{
    public async Task<Airport?> GetByCodeAsync(string code, CancellationToken cancellationToken = default, bool asNoTracking = true)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        IQueryable<Airport> query = dbContext.Airports;
        if (asNoTracking) query = query.AsNoTracking();
        return await query.FirstOrDefaultAsync(a => EF.Functions.ILike(a.Code, Normalize(code)), cancellationToken);
    }

    public async Task<IEnumerable<Airport>> GetByCodesAsync(IEnumerable<string> codes, CancellationToken cancellationToken = default)
    {
        var normalizedCodes = codes
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(Normalize)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedCodes.Length == 0) return [];

        return await dbContext.Airports
            .AsNoTracking()
            .Where(a => normalizedCodes.Contains(a.Code))
            .ToListAsync(cancellationToken);
    }

    public async Task<Airport?> FindBestMatchAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return null;

        var trimmed = query.Trim();
        var startsWithPattern = $"{trimmed}%";
        var containsPattern = $"%{trimmed}%";

        var exactMatch = await dbContext.Airports
            .AsNoTracking()
            .Where(a =>
                EF.Functions.ILike(a.Code, trimmed) ||
                EF.Functions.ILike(a.City, trimmed) ||
                EF.Functions.ILike(a.Name, trimmed) ||
                (a.SearchAliases != null && EF.Functions.ILike(a.SearchAliases, containsPattern)))
            .OrderBy(a => EF.Functions.ILike(a.Name, "%international%") ? 0 : 1)
            .ThenBy(a => a.Name.Length)
            .ThenBy(a => a.Code)
            .FirstOrDefaultAsync(cancellationToken);

        if (exactMatch != null)
        {
            return exactMatch;
        }

        var prefixMatch = await dbContext.Airports
            .AsNoTracking()
            .Where(a =>
                EF.Functions.ILike(a.Code, startsWithPattern) ||
                EF.Functions.ILike(a.City, startsWithPattern) ||
                EF.Functions.ILike(a.Name, startsWithPattern) ||
                (a.SearchAliases != null && EF.Functions.ILike(a.SearchAliases, containsPattern)))
            .OrderBy(a => EF.Functions.ILike(a.Name, "%international%") ? 0 : 1)
            .ThenBy(a => a.Name.Length)
            .ThenBy(a => a.Code)
            .FirstOrDefaultAsync(cancellationToken);

        if (prefixMatch is not null)
        {
            return prefixMatch;
        }

        return await dbContext.Airports
            .AsNoTracking()
            .Where(a =>
                EF.Functions.ILike(a.City, containsPattern) ||
                EF.Functions.ILike(a.Name, containsPattern) ||
                (a.SearchAliases != null && EF.Functions.ILike(a.SearchAliases, containsPattern)))
            .OrderBy(a => EF.Functions.ILike(a.Name, "%international%") ? 0 : 1)
            .ThenBy(a => a.Name.Length)
            .ThenBy(a => a.Code)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Airport>> SearchAsync(string query, int limit = 10, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        var trimmed = query.Trim();
        var startsWithPattern = $"{trimmed}%";
        var containsPattern = $"%{trimmed}%";
        var normalizedLimit = Math.Clamp(limit, 1, 20);

        return await dbContext.Airports
            .AsNoTracking()
            .Where(a => a.AirportType == null || a.AirportType == "large_airport" || a.AirportType == "medium_airport")
            .Where(a =>
                EF.Functions.ILike(a.Code, startsWithPattern) ||
                EF.Functions.ILike(a.City, containsPattern) ||
                EF.Functions.ILike(a.Name, containsPattern) ||
                (a.SearchAliases != null && EF.Functions.ILike(a.SearchAliases, containsPattern)))
            .OrderBy(a => EF.Functions.ILike(a.Code, startsWithPattern) ? 0 :
                EF.Functions.ILike(a.City, startsWithPattern) ? 1 :
                EF.Functions.ILike(a.Name, startsWithPattern) ? 2 : 3)
            .ThenBy(a => a.City)
            .ThenBy(a => a.Name)
            .ThenBy(a => a.Code)
            .Take(normalizedLimit)
            .ToListAsync(cancellationToken);
    }

    private static string Normalize(string value) => value.Trim().ToUpperInvariant();
}
