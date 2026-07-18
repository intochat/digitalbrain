using MediatR;
using TripRadar.Server.Application.Constants;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.Metrics;
using TripRadar.Server.Comms.Core.Attributes;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Users.Commands.CreateNewUser;

public record CreateNewUserCommand(
    [property: Obfuscated] string Password,
    [property: Obfuscated] string Email,
    [property: Obfuscated] string? FirstName,
    [property: Obfuscated] string? LastName,
    [property: Obfuscated] string? PhoneNumber,
    bool HasDataStorageConsent,
    [property: Obfuscated] string? IpAddress) : IRequest<Result>, IMonitoringService
{
    public void IncrementCount(CountMetric countMetric)
    {
        countMetric.UpdateMetric(MetricConstants.CreateNewUser, 1, CountMetric.SetResult(true));
    }

    public void DecrementCount(CountMetric countMetric)
    {
        countMetric.UpdateMetric(MetricConstants.CreateNewUser, 1, CountMetric.SetResult(false));
    }
}
