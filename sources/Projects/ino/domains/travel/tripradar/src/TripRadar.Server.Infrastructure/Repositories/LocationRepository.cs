using Microsoft.EntityFrameworkCore;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Domain.ReferenceData;
using TripRadar.Server.Infrastructure.Database;

namespace TripRadar.Server.Infrastructure.Repositories;

internal sealed class LocationRepository(TripRadarDbContext context) : Repository<Location>(context), ILocationRepository
{
    public async Task<Location?> GetByCountryCodeAsync(string countryCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(countryCode))
        {
            return null;
        }

        var normalizedCountryCode = Normalize(countryCode);

        return await context.Locations
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CountryCode == normalizedCountryCode, cancellationToken);
    }

    public async Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var normalizedName = Normalize(name);
        var hasExactMatch = await context.Locations
            .AsNoTracking()
            .AnyAsync(x => x.Name == normalizedName, cancellationToken);

        if (hasExactMatch)
        {
            return true;
        }

        var containsPattern = $"%{normalizedName}%";
        return await context.Locations
            .AsNoTracking()
            .AnyAsync(x => EF.Functions.ILike(x.Name, containsPattern), cancellationToken);
    }

    public async Task<bool> ExistsByCanonicalNameAsync(string canonicalName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(canonicalName))
        {
            return false;
        }

        var normalizedName = Normalize(canonicalName);
        var hasExactMatch = await context.Locations
            .AsNoTracking()
            .AnyAsync(x => x.CanonicalName == normalizedName, cancellationToken);

        if (hasExactMatch)
        {
            return true;
        }

        var containsPattern = $"%{normalizedName}%";
        return await context.Locations
            .AsNoTracking()
            .AnyAsync(x => EF.Functions.ILike(x.CanonicalName, containsPattern), cancellationToken);
    }

    public async Task<IReadOnlyList<Location>> SearchAsync(string query, int limit = 10, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var normalizedQuery = Normalize(query);
        var startsWithPattern = $"{normalizedQuery}%";
        var containsPattern = $"%{normalizedQuery}%";
        var normalizedLimit = Math.Clamp(limit, 1, 20);

        return await context.Locations
            .AsNoTracking()
            .Where(x =>
                EF.Functions.ILike(x.Name, containsPattern) ||
                EF.Functions.ILike(x.CanonicalName, containsPattern) ||
                EF.Functions.ILike(x.CountryCode, startsWithPattern) ||
                EF.Functions.ILike(x.TargetType, startsWithPattern))
            .OrderBy(x =>
                EF.Functions.ILike(x.Name, normalizedQuery) ? 0 :
                EF.Functions.ILike(x.CanonicalName, normalizedQuery) ? 1 :
                EF.Functions.ILike(x.Name, startsWithPattern) ? 2 :
                EF.Functions.ILike(x.CanonicalName, startsWithPattern) ? 3 :
                EF.Functions.ILike(x.CountryCode, startsWithPattern) ? 4 : 5)
            .ThenByDescending(x => x.Reach ?? 0)
            .ThenBy(x => x.Name)
            .ThenBy(x => x.CanonicalName)
            .Take(normalizedLimit)
            .ToListAsync(cancellationToken);
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();
}
