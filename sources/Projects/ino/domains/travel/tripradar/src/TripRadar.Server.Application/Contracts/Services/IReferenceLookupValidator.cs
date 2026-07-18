using TripRadar.Server.Comms.Core.Errors;

namespace TripRadar.Server.Application.Contracts.Services;

public interface IReferenceLookupValidator
{
    Task<Error?> ValidateLocationAsync(string? location, CancellationToken cancellationToken);

    Task<Error?> ValidateGoogleLrAsync(string? lr, CancellationToken cancellationToken);

    Task<Error?> ValidateAirlineCodesAsync(string? airlineCodes, CancellationToken cancellationToken);

    Task<Error?> ValidateYelpDomainAsync(string? domain, CancellationToken cancellationToken);

    Task<Error?> ValidateYelpReviewLanguageAsync(string? language, CancellationToken cancellationToken);

    Task<Error?> ValidateTripAdvisorDomainAsync(string? domain, CancellationToken cancellationToken);

    Task<Error?> ValidateOpenTableDomainAsync(string? domain, CancellationToken cancellationToken);
}
