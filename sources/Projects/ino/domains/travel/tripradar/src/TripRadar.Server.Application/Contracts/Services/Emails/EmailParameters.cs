namespace TripRadar.Server.Application.Contracts.Services.Emails;

public record EmailParameters(
    string UsernameOrEmail,
    string? LanguageCode = null,
    string? ActionUrl = null,
    Dictionary<string, object>? Data = null,
    EmailType EmailType = EmailType.EmailConfirmation,
    string? UnsubscribeUrl = null);
