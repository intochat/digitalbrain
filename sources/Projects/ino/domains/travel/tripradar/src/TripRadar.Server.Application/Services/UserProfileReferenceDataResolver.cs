using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.Services;

public sealed class UserProfileReferenceDataResolver(
    ILanguageRepository languageRepository,
    ICountryRepository countryRepository,
    ITimezoneRepository timezoneRepository)
    : IUserProfileReferenceDataResolver
{
    public async Task<Result<UserProfileReferenceDataResolution>> ResolveAsync(
        string? languageCode,
        string? countryCode,
        int? timezoneId,
        CancellationToken cancellationToken)
    {
        int? languageId = null;
        if (!string.IsNullOrWhiteSpace(languageCode))
        {
            languageId = await languageRepository.GetLanguageIdByCodeAsync(languageCode, cancellationToken);
            if (languageId is null)
            {
                return Result.Failure<UserProfileReferenceDataResolution>(Errors.LanguageCodeNotFound);
            }
        }

        int? countryId = null;
        if (!string.IsNullOrWhiteSpace(countryCode))
        {
            countryId = await countryRepository.GetCountryIdByCodeAsync(countryCode, cancellationToken);
            if (countryId is null)
            {
                return Result.Failure<UserProfileReferenceDataResolution>(Errors.CountryCodeNotFound);
            }
        }

        var timezone = default(Domain.ReferenceData.Timezone);
        if (timezoneId.HasValue)
        {
            timezone = await timezoneRepository.GetByIdAsync(timezoneId.Value, cancellationToken);
            if (timezone is null)
            {
                return Result.Failure<UserProfileReferenceDataResolution>(Errors.TimezoneNotFound);
            }
        }

        return Result.Success(new UserProfileReferenceDataResolution(languageId, countryId, timezone));
    }
}
