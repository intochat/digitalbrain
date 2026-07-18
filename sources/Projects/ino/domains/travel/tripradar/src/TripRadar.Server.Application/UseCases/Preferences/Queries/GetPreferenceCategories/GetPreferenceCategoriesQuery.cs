using MediatR;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Preferences.Queries.GetPreferenceCategories;

public sealed record GetPreferenceCategoriesQuery() : IRequest<Result<PreferenceCategoriesResponseDTO>>;
