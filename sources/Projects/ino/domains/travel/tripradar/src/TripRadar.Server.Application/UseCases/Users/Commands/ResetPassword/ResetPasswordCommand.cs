using MediatR;
using TripRadar.Server.Application.Constants;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.Metrics;
using TripRadar.Server.Comms.Core.Attributes;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Users.Commands.ResetPassword;

public record ResetPasswordCommand(
    string Username,
    [property: Obfuscated] string Token,
    [property: Obfuscated] string NewPassword) : IRequest<Result>, IMonitoringService
{
    public void IncrementCount(CountMetric countMetric)
    {
        countMetric.UpdateMetric(MetricConstants.ResetPassword, 1, CountMetric.SetResult(true));
    }

    public void DecrementCount(CountMetric countMetric)
    {
        countMetric.UpdateMetric(MetricConstants.ResetPassword, 1, CountMetric.SetResult(false));
    }
}
