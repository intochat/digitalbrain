using MediatR;
using TripRadar.Server.Application.Constants;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Application.Metrics;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Users.Commands.SyncTelegramUsername;

public record SyncTelegramUsernameCommand(TelegramAuthDataDTO TelegramAuth)
    : IRequest<Result<(string Email, string Username)>>, IMonitoringService
{
    public void IncrementCount(CountMetric countMetric)
    {
        countMetric.UpdateMetric(MetricConstants.UpdateUsername, 1, CountMetric.SetResult(true));
    }

    public void DecrementCount(CountMetric countMetric)
    {
        countMetric.UpdateMetric(MetricConstants.UpdateUsername, 1, CountMetric.SetResult(false));
    }
}
