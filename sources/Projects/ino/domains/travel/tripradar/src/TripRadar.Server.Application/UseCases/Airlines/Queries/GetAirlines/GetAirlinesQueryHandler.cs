using MediatR;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Airlines.Queries.GetAirlines;

public sealed class GetAirlinesQueryHandler(IAirlineRepository airlineRepository) : IRequestHandler<GetAirlinesQuery, Result<IEnumerable<AirlineResponseDTO>>>
{
    public async Task<Result<IEnumerable<AirlineResponseDTO>>> Handle(GetAirlinesQuery request, CancellationToken cancellationToken)
    {
        var airlines = await airlineRepository.SearchActiveAsync(request.Query, request.Limit, cancellationToken);
        var response = airlines.Select(airline => new AirlineResponseDTO(
            AirlineCode: airline.AirlineCode,
            AirlineName: airline.AirlineName,
            IsAlliance: airline.IsAlliance,
            LogoUrl: BuildLogoUrl(airline.AirlineCode, airline.IsAlliance)));

        return Result.Success(response.AsEnumerable());
    }

    private static string? BuildLogoUrl(string airlineCode, bool isAlliance)
    {
        if (isAlliance || string.IsNullOrWhiteSpace(airlineCode) || airlineCode.Length != 2)
        {
            return null;
        }

        return $"https://www.gstatic.com/flights/airline_logos/70px/{airlineCode.Trim().ToUpperInvariant()}.png";
    }
}
