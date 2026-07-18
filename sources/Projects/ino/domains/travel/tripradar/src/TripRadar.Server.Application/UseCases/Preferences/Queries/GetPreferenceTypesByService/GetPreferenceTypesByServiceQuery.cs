using MediatR;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.UseCases.Preferences.Queries.GetPreferenceTypesByService;

public sealed record GetPreferenceTypesByServiceQuery(ServiceType ServiceType) : IRequest<Result<List<PreferenceTypeResponseDTO>>>;
