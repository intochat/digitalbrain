using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.DTO.Responses;
using TripRadar.Server.Domain.Aggregates;

namespace TripRadar.Server.Application.Services;

public sealed class UserProfileAssembler : IUserProfileAssembler
{
    public GetUserProfileResponseDTO Assemble(User user) =>
        new(
            Username: user.Profile.Username ?? string.Empty,
            Email: user.Profile.Email,
            IsEmailConfirmed: user.Profile.IsEmailConfirmed,
            FirstName: user.Profile.FirstName,
            LastName: user.Profile.LastName,
            PhoneNumber: user.Profile.PhoneNumber,
            GoogleId: user.Profile.GoogleId,
            TelegramUserId: user.Profile.TelegramUserId,
            TimezoneId: user.Profile.TimezoneId,
            ProfilePictureUrl: user.Profile.ProfilePictureUrl,
            LanguageCode: user.Profile.Language?.LanguageCode,
            LanguageName: user.Profile.Language?.LanguageName,
            CountryCode: user.Profile.Country?.CountryCode,
            CountryName: user.Profile.Country?.CountryName,
            AllowsMarketingEmails: user.AllowsMarketingEmails,
            IsActive: user.IsActive,
            TierName: user.Tier.Name,
            CreatedOn: user.CreatedOn,
            UpdatedOn: user.UpdatedOn);
}
