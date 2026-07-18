using TripRadar.Server.Application.DTO.Models;

namespace TripRadar.Server.Application.Contracts.Requests;

public interface ILocalizationRequest
{
    Localization? Localization { get; }
}
