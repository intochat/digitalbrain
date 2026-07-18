using MediatR;
using TripRadar.Server.Application.Constants;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Application.Metrics;
using TripRadar.Server.Comms.Core.Attributes;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Users.Commands.ActivateUser;

public record ActivateUserCommand([property: Obfuscated] string Email, string Username, TelegramAuthDataDTO TelegramAuth) : IRequest<Result>, IMonitoringService
{
    public void IncrementCount(CountMetric countMetric)
    {
        countMetric.UpdateMetric(MetricConstants.ActivateUser, 1, CountMetric.SetResult(true));
    }

    public void DecrementCount(CountMetric countMetric)
    {
        countMetric.UpdateMetric(MetricConstants.ActivateUser, 1, CountMetric.SetResult(false));
    }
}
