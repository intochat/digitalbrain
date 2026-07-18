using MediatR;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Preferences.Queries.GetServices;

public sealed record GetServicesQuery : IRequest<Result<List<ServiceInfoDTO>>>;
