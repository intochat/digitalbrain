using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.Contracts.Services;

public interface IUserProfileReferenceDataResolver
{
    Task<Result<UserProfileReferenceDataResolution>> ResolveAsync(string? languageCode, string? countryCode, int? timezoneId, CancellationToken cancellationToken);
}
