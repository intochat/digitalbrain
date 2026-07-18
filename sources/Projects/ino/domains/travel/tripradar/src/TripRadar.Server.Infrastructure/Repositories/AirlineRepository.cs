using Microsoft.EntityFrameworkCore;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Domain.ReferenceData;
using TripRadar.Server.Infrastructure.Database;

namespace TripRadar.Server.Infrastructure.Repositories;

internal sealed class AirlineRepository(TripRadarDbContext context) : Repository<Airline>(context), IAirlineRepository
{
    public async Task<List<Airline>> GetAllActiveAsync(CancellationToken cancellationToken = default) =>
        await context.Airlines
            .AsNoTracking()
            .Where(airline => airline.IsActive)
            .OrderBy(airline => airline.IsAlliance)
            .ThenBy(airline => airline.AirlineName)
            .ToListAsync(cancellationToken);

    public async Task<Airline?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var normalizedCode = NormalizeCode(code);

        return await context.Airlines
            .AsNoTracking()
            .FirstOrDefaultAsync(airline => airline.AirlineCode == normalizedCode, cancellationToken);
    }

    public async Task<List<Airline>> SearchActiveAsync(string? query, int limit, CancellationToken cancellationToken = default)
    {
        var normalizedLimit = limit <= 0 ? 100 : Math.Min(limit, 500);
        var airlinesQuery = context.Airlines
            .AsNoTracking()
            .Where(airline => airline.IsActive);

        if (!string.IsNullOrWhiteSpace(query))
        {
            var normalizedQuery = query.Trim();
            var upperQuery = normalizedQuery.ToUpperInvariant();
            var containsPattern = $"%{normalizedQuery}%";

            airlinesQuery = airlinesQuery.Where(airline =>
                airline.AirlineCode.StartsWith(upperQuery)
                || EF.Functions.ILike(airline.AirlineName, containsPattern)
                || (airline.SearchAliases != null && EF.Functions.ILike(airline.SearchAliases, containsPattern)));
        }

        return await airlinesQuery
            .OrderBy(airline => airline.IsAlliance)
            .ThenBy(airline => airline.AirlineName)
            .Take(normalizedLimit)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<string>> GetInvalidCodesAsync(IEnumerable<string> codes, CancellationToken cancellationToken = default)
    {
        var normalizedCodes = codes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(NormalizeCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalizedCodes.Count == 0)
        {
            return [];
        }

        var existingCodes = await context.Airlines
            .AsNoTracking()
            .Where(airline => airline.IsActive && normalizedCodes.Contains(airline.AirlineCode))
            .Select(airline => airline.AirlineCode)
            .ToListAsync(cancellationToken);

        return normalizedCodes
            .Except(existingCodes, StringComparer.OrdinalIgnoreCase)
            .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();
}
