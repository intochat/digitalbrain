using MediatR;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Timezones.Queries.GetTimezones;

public record GetTimezonesQuery : IRequest<Result<IEnumerable<TimezoneResponseDTO>>>;
