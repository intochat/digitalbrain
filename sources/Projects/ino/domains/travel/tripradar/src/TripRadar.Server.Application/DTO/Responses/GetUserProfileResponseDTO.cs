namespace TripRadar.Server.Application.DTO.Responses;

public record GetUserProfileResponseDTO(
    string Username,
    string Email,
    bool IsEmailConfirmed,
    string? FirstName,
    string? LastName,
    string? PhoneNumber,
    string? GoogleId,
    long? TelegramUserId,
    int TimezoneId,
    string? ProfilePictureUrl,
    string? LanguageCode,
    string? LanguageName,
    string? CountryCode,
    string? CountryName,
    bool AllowsMarketingEmails,
    bool IsActive,
    string TierName,
    DateTime CreatedOn,
    DateTime? UpdatedOn);
