using TripRadar.Server.Application.DTO.Models;

namespace TripRadar.Server.Application.Contracts.Services;

public interface ILocalizationValidatorService
{
    Task ValidateAsync(Localization? localizationSettings, CancellationToken cancellationToken);
}
