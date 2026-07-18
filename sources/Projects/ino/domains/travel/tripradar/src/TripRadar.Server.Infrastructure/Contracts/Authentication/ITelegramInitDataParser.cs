using TripRadar.Server.Application.DTO.Models;

namespace TripRadar.Server.Infrastructure.Contracts.Authentication;

public interface ITelegramInitDataParser
{
    bool TryParse(string initData, out TelegramAuthDataDTO authData);
}
