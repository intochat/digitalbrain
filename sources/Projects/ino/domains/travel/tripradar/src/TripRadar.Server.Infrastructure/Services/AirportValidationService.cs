using System.Text.RegularExpressions;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Infrastructure.Services;

/// <summary>
/// Service responsible for validating airport codes.
/// </summary>
public class AirportValidationService(IUnitOfWork unitOfWork) : IAirportValidationService
{
    public async Task<string?> ResolveAirportCodeAsync(string? input, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var trimmed = input.Trim();
        if (IsIataCode(trimmed))
        {
            var byCode = await unitOfWork.AirportRepository.GetByCodeAsync(trimmed, cancellationToken);
            return byCode?.Code.ToUpperInvariant();
        }

        var normalized = NormalizeLocationQuery(trimmed);
        var match = await unitOfWork.AirportRepository.FindBestMatchAsync(normalized, cancellationToken);
        return match?.Code.ToUpperInvariant();
    }

    /// <inheritdoc />
    public async Task<Result> ValidateAirportCodesAsync(string departureCode, string arrivalCode, CancellationToken cancellationToken = default)
    {
        var departureToValidate = await NormalizeCodeForValidationAsync(departureCode, cancellationToken) ?? departureCode;
        var arrivalToValidate = await NormalizeCodeForValidationAsync(arrivalCode, cancellationToken) ?? arrivalCode;

        var airportCodes = new List<string> { departureToValidate, arrivalToValidate };
        var airports = await unitOfWork.AirportRepository.GetByCodesAsync(airportCodes, cancellationToken);
        var airportsDictionary = airports.ToDictionary(a => a.Code, a => a, StringComparer.OrdinalIgnoreCase);

        if (!airportsDictionary.ContainsKey(departureToValidate))
        {
            return Result.Failure(Errors.AirportCodeNotFound with
            {
                Reason = $"Departure airport code '{departureCode}' not found"
            });
        }

        if (!airportsDictionary.ContainsKey(arrivalToValidate))
        {
            return Result.Failure(Errors.AirportCodeNotFound with
            {
                Reason = $"Arrival airport code '{arrivalCode}' not found"
            });
        }

        return Result.Success();
    }

    private async Task<string?> NormalizeCodeForValidationAsync(string code, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return code;
        }

        var trimmedCode = code.Trim();
        if (IsIataCode(trimmedCode))
        {
            return trimmedCode.ToUpperInvariant();
        }

        return await ResolveAirportCodeAsync(trimmedCode, cancellationToken);
    }

    private static bool IsIataCode(string value) => value.Length == 3 && value.All(char.IsLetter);

    private static string NormalizeLocationQuery(string value)
    {
        var normalized = Regex.Replace(value, @"\s*\(.*?\)\s*", " ").Trim();
        normalized = Regex.Replace(normalized, @"^(from|to|in|at)\s+", "", RegexOptions.IgnoreCase);
        var commaIndex = normalized.IndexOf(',');
        if (commaIndex >= 0)
        {
            normalized = normalized[..commaIndex];
        }

        return normalized.Trim().ToLowerInvariant();
    }
}
