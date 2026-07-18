using MediatR;
using TripRadar.Server.Application.Constants;
using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Application.Metrics;
using TripRadar.Server.Comms.Core.Attributes;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Users.Commands.UpdateUserProfile;

public record UpdateUserProfileCommand(
    string Username,
    [property: Obfuscated] string? FirstName,
    [property: Obfuscated] string? LastName,
    [property: Obfuscated] string? PhoneNumber,
    int? TimezoneId,
    string? ProfilePictureUrl,
    string? LanguageCode,
    string? CountryCode,
    bool? AllowsMarketingEmails) : IRequest<Result<GetUserProfileResponseDTO>>, IMonitoringService, IAuthorizedRequest
{
    public void IncrementCount(CountMetric countMetric)
    {
        countMetric.UpdateMetric(MetricConstants.UpdateUserProfile, 1, CountMetric.SetResult(true));
    }

    public void DecrementCount(CountMetric countMetric)
    {
        countMetric.UpdateMetric(MetricConstants.UpdateUserProfile, 1, CountMetric.SetResult(false));
    }
}
