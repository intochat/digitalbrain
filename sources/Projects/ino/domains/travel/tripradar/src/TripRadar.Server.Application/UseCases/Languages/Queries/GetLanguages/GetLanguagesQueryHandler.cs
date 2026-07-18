using MediatR;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.UseCases.Languages.Queries.GetLanguages;

public sealed class GetLanguagesQueryHandler(ILanguageRepository languageRepository) : IRequestHandler<GetLanguagesQuery, Result<IEnumerable<LanguageResponseDTO>>>
{
    public async Task<Result<IEnumerable<LanguageResponseDTO>>> Handle(GetLanguagesQuery request, CancellationToken cancellationToken)
    {
        var supportedLanguageCodes = SupportedLanguageType
            .GetAllLanguageCodes()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var languages = await languageRepository.GetAllSystemLanguagesAsync(cancellationToken);
        var publicLanguages = languages
            .Where(l => !l.IsInternal && supportedLanguageCodes.Contains(l.LanguageCode))
            .DistinctBy(l => l.LanguageCode, StringComparer.OrdinalIgnoreCase);
        var languageResponseDtos = publicLanguages.Select(l => new LanguageResponseDTO(
            LanguageCode: l.LanguageCode,
            LanguageName: l.LanguageName))
            .OrderBy(l => l.LanguageName)
            .ThenBy(l => l.LanguageCode);

        return Result.Success(languageResponseDtos.AsEnumerable());
    }
}
