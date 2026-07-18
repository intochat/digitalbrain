using MediatR;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Languages.Queries.GetLanguages;

public record GetLanguagesQuery : IRequest<Result<IEnumerable<LanguageResponseDTO>>>;
