using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.Contracts.Services;

/// <summary>
/// Service responsible for validating airport codes.
/// </summary>
public interface IAirportValidationService
{
    /// <summary>
    /// Resolves a city name, airport name, or IATA code into a valid airport code.
    /// </summary>
    /// <param name="input">City, airport name, or IATA code.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Resolved IATA code or null if no match found.</returns>
    Task<string?> ResolveAirportCodeAsync(string? input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates that the provided airport codes exist in the system.
    /// </summary>
    /// <param name="departureCode">The departure airport code.</param>
    /// <param name="arrivalCode">The arrival airport code.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result indicating success or failure with appropriate error.</returns>
    Task<Result> ValidateAirportCodesAsync(string departureCode, string arrivalCode, CancellationToken cancellationToken = default);
}
