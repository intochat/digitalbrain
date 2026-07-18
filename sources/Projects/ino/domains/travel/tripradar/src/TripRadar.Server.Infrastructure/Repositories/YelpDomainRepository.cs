using Microsoft.EntityFrameworkCore;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Domain.ReferenceData;
using TripRadar.Server.Infrastructure.Database;

namespace TripRadar.Server.Infrastructure.Repositories;

public class YelpDomainRepository(TripRadarDbContext dbContext)
    : Repository<YelpDomain>(dbContext), IYelpDomainRepository
{
    public async Task<YelpDomain?> GetByDomainNameAsync(string domainName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(domainName))
        {
            return null;
        }

        var normalized = NormalizeDomain(domainName);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        var alternate = normalized.StartsWith("www.", StringComparison.Ordinal)
            ? normalized.Length > 4 ? normalized[4..] : normalized
            : $"www.{normalized}";

        return await dbContext.YelpDomains
            .FirstOrDefaultAsync(
                d => d.DomainName == normalized || d.DomainName == alternate,
                cancellationToken);
    }

    private static string NormalizeDomain(string domainName)
    {
        var trimmed = domainName.Trim().ToLowerInvariant();

        if (trimmed.StartsWith("http://", StringComparison.Ordinal) ||
            trimmed.StartsWith("https://", StringComparison.Ordinal))
        {
            if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
            {
                return uri.Host.ToLowerInvariant();
            }
        }

        var slashIndex = trimmed.IndexOf('/', StringComparison.Ordinal);
        return slashIndex >= 0 ? trimmed[..slashIndex] : trimmed;
    }
}
