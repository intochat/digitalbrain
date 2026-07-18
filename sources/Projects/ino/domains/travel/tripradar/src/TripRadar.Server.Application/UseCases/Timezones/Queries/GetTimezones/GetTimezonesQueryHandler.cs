using MediatR;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Timezones.Queries.GetTimezones;

public sealed class GetTimezonesQueryHandler(ITimezoneRepository timezoneRepository) : IRequestHandler<GetTimezonesQuery, Result<IEnumerable<TimezoneResponseDTO>>>
{
    public async Task<Result<IEnumerable<TimezoneResponseDTO>>> Handle(GetTimezonesQuery request, CancellationToken cancellationToken)
    {
        var timezones = await timezoneRepository.GetAllTimezonesAsync(cancellationToken);
        var timezoneResponseDtos = timezones.Select(timezone => new TimezoneResponseDTO(
            TimezoneId: timezone.TimezoneId,
            TimezoneCode: timezone.TimezoneCode,
            TimezoneName: timezone.TimezoneName));
        return Result.Success(timezoneResponseDtos);
    }
}
