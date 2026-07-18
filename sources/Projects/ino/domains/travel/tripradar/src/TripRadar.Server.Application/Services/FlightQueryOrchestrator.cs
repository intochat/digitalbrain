using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.Contracts.Services.Providers;
using TripRadar.Server.Application.DTO.Enums;
using TripRadar.Server.Application.DTO.Requests;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Enums;
using TripRadar.Server.Domain.Policies;

namespace TripRadar.Server.Application.Services;

public sealed class FlightQueryOrchestrator(
    ISerpApiProviderService serpApiProviderService,
    IAirportValidationService airportValidationService,
    ICurrentUserContext currentUserContext,
    IPreferenceService preferenceService)
    : IFlightQueryOrchestrator
{
    public async Task<Result<GetFlightResponseDTO>> ExecuteAsync(GetFlightRequestDTO request, CancellationToken cancellationToken)
    {
        var currentUser = currentUserContext.GetRequiredUser();

        if (request.AdvancedOptions.Type != FlightType.MultiCity && request.Booking?.BookingToken is null && request.FlightSearch is not null)
        {
            request.FlightSearch.DepartureId = await ResolveAirportCodeAsync(request.FlightSearch.DepartureId, cancellationToken);
            request.FlightSearch.ArrivalId = await ResolveAirportCodeAsync(request.FlightSearch.ArrivalId, cancellationToken);
        }

        var appliedRequestResult = await preferenceService.AddPreferencesAsync(request, currentUser.Id, ServiceType.Flight, cancellationToken);
        if (appliedRequestResult.IsFailure)
        {
            return Result.Failure<GetFlightResponseDTO>(appliedRequestResult.Error);
        }

        if (appliedRequestResult.Value.AdvancedOptions.DeepSearch == true && !PaidTierEligibilityPolicy.IsEligibleForPaidFeatures(currentUser))
        {
            appliedRequestResult.Value.AdvancedOptions.DeepSearch = null;
        }

        var response = await serpApiProviderService.SearchAsync<GetFlightRequestDTO, GetFlightResponseDTO>(appliedRequestResult.Value, cancellationToken);
        if (response.IsFailure)
        {
            return Result.Failure<GetFlightResponseDTO>(response.Error);
        }

        return response.Value is null
            ? Result.Failure<GetFlightResponseDTO>(Errors.FlightQueryDataNotFound)
            : Result.Success(response.Value);
    }

    private async Task<string?> ResolveAirportCodeAsync(string? airportInput, CancellationToken cancellationToken)
    {
        if (!NeedsAirportResolution(airportInput))
        {
            return airportInput;
        }

        var resolvedAirportCode = await airportValidationService.ResolveAirportCodeAsync(airportInput!, cancellationToken);
        return string.IsNullOrWhiteSpace(resolvedAirportCode) ? airportInput : resolvedAirportCode;
    }

    private static bool NeedsAirportResolution(string? airportInput)
    {
        if (string.IsNullOrWhiteSpace(airportInput))
        {
            return false;
        }

        var normalizedAirportInput = airportInput.Trim();

        if (normalizedAirportInput.Contains(','))
            return false;

        return normalizedAirportInput.Length != 3 || normalizedAirportInput.Any(ch => !char.IsLetter(ch));
    }
}
