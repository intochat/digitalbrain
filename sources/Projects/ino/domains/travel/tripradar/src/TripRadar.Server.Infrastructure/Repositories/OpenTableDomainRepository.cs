using Microsoft.EntityFrameworkCore;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Domain.ReferenceData;
using TripRadar.Server.Infrastructure.Database;

namespace TripRadar.Server.Infrastructure.Repositories;

public class OpenTableDomainRepository(TripRadarDbContext dbContext)
    : Repository<OpenTableDomain>(dbContext), IOpenTableDomainRepository
{
    public async Task<OpenTableDomain?> GetByDomainNameAsync(string domainName,
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

        return await dbContext.OpenTableDomains
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
                trimmed = uri.Host.ToLowerInvariant();
            }
        }

        var slashIndex = trimmed.IndexOf('/', StringComparison.Ordinal);
        if (slashIndex >= 0)
        {
            trimmed = trimmed[..slashIndex];
        }

        return trimmed.StartsWith("www.", StringComparison.Ordinal)
            ? trimmed.Length > 4 ? trimmed[4..] : trimmed
            : trimmed;
    }
}

