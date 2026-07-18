using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Comms.Core.Exceptions;

namespace TripRadar.Server.Infrastructure.Services;

public class LocalizationValidatorService(
    ILanguageRepository languageRepository,
    ICountryRepository countryRepository,
    ICurrencyRepository currencyRepository,
    IDomainRepository domainRepository) : ILocalizationValidatorService
{
    public async Task ValidateAsync(Localization? localizationSettings, CancellationToken cancellationToken)
    {
        if (localizationSettings is not null)
        {
            if (localizationSettings.Hl is not null)
            {
                if (await languageRepository.GetByCodeAsync(localizationSettings.Hl, cancellationToken) is null)
                {
                    throw new ObjectNotFoundException($"{localizationSettings.Hl} - {Errors.LanguageCodeNotFound.Reason}");
                }
            }

            if (localizationSettings.Gl is not null)
            {
                if (await countryRepository.GetByCodeAsync(localizationSettings.Gl, cancellationToken) is null)
                {
                    throw new ObjectNotFoundException($"{localizationSettings.Gl} - {Errors.CountryCodeNotFound.Reason}");
                }
            }

            if (localizationSettings.Currency is not null)
            {
                if (await currencyRepository.GetByCodeAsync(localizationSettings.Currency, cancellationToken) is null)
                {
                    throw new ObjectNotFoundException($"{localizationSettings.Currency} - {Errors.CurrencyCodeNotFound.Reason}");
                }
            }

            if (localizationSettings.Domain is not null)
            {
                if (await domainRepository.GetByDomainNameAsync(localizationSettings.Domain, cancellationToken) is null)
                {
                    throw new ObjectNotFoundException($"{localizationSettings.Domain} - Domain not found or not supported");
                }
            }
        }
    }
}
