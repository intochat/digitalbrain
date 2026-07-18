using MediatR;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Preferences.Queries.GetAllPreferenceTypes;

public sealed record GetAllPreferenceTypesQuery : IRequest<Result<List<PreferenceTypeResponseDTO>>>;
