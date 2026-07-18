using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Comms.Core.Errors;

namespace TripRadar.Server.Application.Services;

public sealed class ReferenceLookupValidator(
    ILocationRepository locationRepository,
    IGoogleLrLanguageRepository googleLrLanguageRepository,
    IAirlineRepository airlineRepository,
    IYelpDomainRepository yelpDomainRepository,
    IYelpReviewLanguageRepository yelpReviewLanguageRepository,
    ITripAdvisorDomainRepository tripAdvisorDomainRepository,
    IOpenTableDomainRepository openTableDomainRepository)
    : IReferenceLookupValidator
{
    public async Task<Error?> ValidateLocationAsync(string? location, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(location))
        {
            return null;
        }

        var normalizedLocation = location.Trim();
        if (await LocationExistsAsync(normalizedLocation, cancellationToken))
        {
            return null;
        }

        var locationParts = normalizedLocation.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (locationParts.Length > 1 && await LocationExistsAsync(locationParts[0], cancellationToken))
        {
            return null;
        }

        return AppendReason(Errors.LocationNotFound, normalizedLocation);
    }

    public async Task<Error?> ValidateGoogleLrAsync(string? lr, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(lr))
        {
            return null;
        }

        foreach (var token in lr.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            var normalized = token.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalized) || !normalized.StartsWith("lang_", StringComparison.Ordinal))
            {
                return AppendReason(Errors.GoogleLrLanguageNotFound, token);
            }

            var lrLanguage = await googleLrLanguageRepository.GetByLanguageCodeAsync(normalized, cancellationToken);
            if (lrLanguage is null)
            {
                return AppendReason(Errors.GoogleLrLanguageNotFound, token);
            }
        }

        return null;
    }

    public async Task<Error?> ValidateAirlineCodesAsync(string? airlineCodes, CancellationToken cancellationToken)
    {
        var normalizedCodes = airlineCodes?
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalizedCodes is not { Count: > 0 })
        {
            return null;
        }

        var invalidCodes = await airlineRepository.GetInvalidCodesAsync(normalizedCodes, cancellationToken);
        return invalidCodes.Count == 0 ? null : AppendReason(Errors.AirlineCodeNotFound, string.Join(", ", invalidCodes));
    }

    public async Task<Error?> ValidateYelpDomainAsync(string? domain, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            return null;
        }

        var reference = await yelpDomainRepository.GetByDomainNameAsync(domain, cancellationToken);
        return reference is not null ? null : AppendReason(Errors.YelpDomainNotFound, domain);
    }

    public async Task<Error?> ValidateYelpReviewLanguageAsync(string? language, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return null;
        }

        var reference = await yelpReviewLanguageRepository.GetByLanguageCodeAsync(language, cancellationToken);
        return reference is not null ? null : AppendReason(Errors.YelpReviewLanguageNotFound, language);
    }

    public async Task<Error?> ValidateTripAdvisorDomainAsync(string? domain, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            return null;
        }

        var reference = await tripAdvisorDomainRepository.GetByDomainNameAsync(domain, cancellationToken);
        return reference is not null ? null : AppendReason(Errors.TripAdvisorDomainNotFound, domain);
    }

    public async Task<Error?> ValidateOpenTableDomainAsync(string? domain, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            return null;
        }

        var reference = await openTableDomainRepository.GetByDomainNameAsync(domain, cancellationToken);
        return reference is not null ? null : AppendReason(Errors.OpenTableDomainNotFound, domain);
    }

    private async Task<bool> LocationExistsAsync(string location, CancellationToken cancellationToken)
    {
        var byCountryCode = await locationRepository.GetByCountryCodeAsync(location, cancellationToken);
        if (byCountryCode is not null)
        {
            return true;
        }

        if (await locationRepository.ExistsByNameAsync(location, cancellationToken))
        {
            return true;
        }

        return await locationRepository.ExistsByCanonicalNameAsync(location, cancellationToken);
    }

    private static Error AppendReason(Error error, string value)
    {
        return error with { Reason = $"{value} - {error.Reason}" };
    }
}
