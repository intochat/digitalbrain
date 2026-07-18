using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.Contracts.Services.Providers;

public interface IFlightPriceCalendarProvider
{
    Task<Result<FlightPriceCalendarProviderResponse>> GetMonthlyPricesAsync(FlightPriceCalendarProviderRequest request, CancellationToken cancellationToken);
}