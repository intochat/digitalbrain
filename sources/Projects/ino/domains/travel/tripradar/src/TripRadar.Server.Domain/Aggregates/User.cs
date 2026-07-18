using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Domain.Events;
using TripRadar.Server.Domain.SeedWork;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Domain.Aggregates;

public class User : AggregateRoot<long>
{
    private User()
    {
    }

    public new long Id { get; private set; }

    public bool IsActive { get; private set; }

    public int TierId { get; private set; }

    public Tier Tier { get; private set; } = null!;

    public UserSubscription? UserSubscription { get; private set; }

    public bool HasDataStorageConsent { get; private set; }

    public bool AllowsMarketingEmails { get; private set; }

    public DateTime CreatedOn { get; private set; }

    public DateTime? UpdatedOn { get; private set; }

    public UserProfile Profile { get; private set; } = null!;

    public long? PromoCodeId { get; private set; }

    public PromoCode? PromoCode { get; private set; }

    private ICollection<UserMonthlyTokenCount> MonthlyTokenCounts { get; set; } = new List<UserMonthlyTokenCount>();

    private ICollection<ScheduledFlightQuery> ScheduledFlightQueries { get; set; } = new List<ScheduledFlightQuery>();

    private ICollection<ScheduledHotelQuery> ScheduledHotelQueries { get; set; } = new List<ScheduledHotelQuery>();

    private ICollection<ScheduledEventQuery> ScheduledEventQueries { get; set; } = new List<ScheduledEventQuery>();

    private ICollection<ScheduledLocalPlaceQuery> ScheduledLocalPlacesQueries { get; set; } = new List<ScheduledLocalPlaceQuery>();

    private ICollection<Feedback> Feedbacks { get; set; } = new List<Feedback>();

    private ICollection<TripVault> TripVaults { get; set; } = new List<TripVault>();

    public static User Register(
        string password,
        string email,
        bool hasDataStorageConsent,
        string? firstName = null,
        string? lastName = null,
        string? phoneNumber = null,
        string? ipAddress = null,
        string? googleId = null,
        int timezoneId = 1,
        string? profilePictureUrl = null,
        int tierId = 1)
    {
        if (!hasDataStorageConsent)
        {
            throw new ArgumentException("Data storage consent is required.", nameof(hasDataStorageConsent));
        }

        var user = new User
        {
            HasDataStorageConsent = hasDataStorageConsent,
            AllowsMarketingEmails = false,
            CreatedOn = DateTime.UtcNow,
            IsActive = false,
            TierId = tierId,
            Profile = UserProfile.Create(
                HashPassword(password),
                email,
                firstName,
                lastName,
                phoneNumber,
                ipAddress,
                googleId,
                timezoneId,
                profilePictureUrl)
        };

        user.Profile.AttachToUser(user);
        user.RaiseDomainEvent(new UserRegisteredDomainEvent(user.Profile.Email));

        return user;
    }

    public void UpdateTokenData(string refreshToken, DateTime refreshTokenExpiryTime)
    {
        Profile.UpdateTokenData(refreshToken, refreshTokenExpiryTime);
        UpdatedOn = DateTime.UtcNow;
    }

    internal void AttachSubscription(UserSubscription subscription)
    {
        UserSubscription = subscription;
        UpdatedOn = DateTime.UtcNow;
    }

    public void ClearRefreshToken()
    {
        Profile.ClearRefreshToken();
        UpdatedOn = DateTime.UtcNow;
    }

    public void RotateSecurityStamp()
    {
        Profile.RotateSecurityStamp();
        UpdatedOn = DateTime.UtcNow;
    }

    public void UpdateTier(int newTierId)
    {
        TierId = newTierId;
        UpdatedOn = DateTime.UtcNow;
    }

    public void RecordTokenConsumption(ServiceType serviceType, TokenConsumptionType type, decimal? tokenCost = null)
    {
        var username = string.IsNullOrWhiteSpace(Profile.Username) ? Profile.Email : Profile.Username;
        RaiseDomainEvent(new TokenConsumedDomainEvent(username, serviceType, type, tokenCost));
        UpdatedOn = DateTime.UtcNow;
    }

    public bool ChangePassword(string currentPassword, string newPassword)
    {
        if (string.IsNullOrWhiteSpace(Profile.Password) || !BCrypt.Net.BCrypt.Verify(currentPassword, Profile.Password))
        {
            return false;
        }

        ResetPassword(newPassword);
        return true;
    }

    public void ResetPassword(string newPassword)
    {
        Profile.UpdatePassword(HashPassword(newPassword));
        Profile.ClearRefreshToken();
        Profile.RotateSecurityStamp();
        UpdatedOn = DateTime.UtcNow;
        RaiseDomainEvent(new UserPasswordChangedDomainEvent(Id, Profile.Email));
    }

    public void UpdateEmail(string email)
    {
        Profile.UpdateEmail(email);
        UpdatedOn = DateTime.UtcNow;
    }

    public void SetEmailConfirmationToken(string token, DateTime expiry)
    {
        Profile.SetEmailConfirmationToken(token, expiry);
        UpdatedOn = DateTime.UtcNow;
    }

    public void ConfirmEmail()
    {
        Profile.ConfirmEmail();
        UpdatedOn = DateTime.UtcNow;
    }

    public void SetPasswordResetToken(string token, DateTime expiry)
    {
        Profile.SetPasswordResetToken(token, expiry);
        UpdatedOn = DateTime.UtcNow;
    }

    public void ToggleStatus()
    {
        IsActive = !IsActive;
        UpdatedOn = DateTime.UtcNow;
    }

    public void Activate()
    {
        IsActive = true;
        UpdatedOn = DateTime.UtcNow;
    }

    public void UpdateUsername(string username)
    {
        Profile.UpdateUsername(username);
        UpdatedOn = DateTime.UtcNow;
    }

    public bool IsEmailConfirmationTokenValid(string token)
    {
        return Profile.IsEmailConfirmationTokenValid(token);
    }

    public bool IsPasswordResetTokenValid(string token)
    {
        return Profile.IsPasswordResetTokenValid(token);
    }

    public void UpdateGoogleData(string googleId, string? profilePictureUrl = null)
    {
        Profile.UpdateGoogleData(googleId);
        Profile.UpdateProfilePicture(profilePictureUrl);
        UpdatedOn = DateTime.UtcNow;
    }

    public void UpdateTelegramUserId(long telegramUserId)
    {
        Profile.UpdateTelegramUserId(telegramUserId);
        UpdatedOn = DateTime.UtcNow;
    }

    public void UpdatePersonalInfo(string? firstName, string? lastName, string? phoneNumber)
    {
        Profile.UpdatePersonalInfo(firstName, lastName, phoneNumber);
        UpdatedOn = DateTime.UtcNow;
    }

    public void UpdateProfile(
        string? firstName,
        string? lastName,
        string? phoneNumber,
        int? timezoneId,
        string? profilePictureUrl,
        int? languageId,
        int? countryId = null)
    {
        Profile.UpdateProfile(firstName, lastName, phoneNumber, timezoneId, profilePictureUrl, languageId, countryId);
        UpdatedOn = DateTime.UtcNow;
    }

    public void UpdateMarketingEmailPermission(bool allowsMarketingEmails)
    {
        AllowsMarketingEmails = allowsMarketingEmails;
        UpdatedOn = DateTime.UtcNow;
    }

    public static User CreateFromGoogleAuth(
        string email,
        string? firstName,
        string? lastName,
        string googleId,
        string? profilePictureUrl,
        string ipAddress,
        int timezoneId = 1,
        int tierId = 1)
    {
        var user = new User
        {
            HasDataStorageConsent = true,
            AllowsMarketingEmails = false,
            CreatedOn = DateTime.UtcNow,
            IsActive = true,
            TierId = tierId,
            Profile = UserProfile.Create(
                string.Empty,
                email,
                firstName,
                lastName,
                null,
                ipAddress,
                googleId,
                timezoneId,
                profilePictureUrl)
        };

        user.Profile.AttachToUser(user);
        user.Profile.ConfirmEmail();
        user.RaiseDomainEvent(new UserRegisteredDomainEvent(user.Profile.Email));

        return user;
    }

    public static User CreateFromTelegramAuth(
        long telegramUserId,
        string? telegramUsername,
        string? firstName,
        string? lastName,
        string? profilePictureUrl,
        int tierId = 1)
    {
        var email = $"{telegramUserId}@tg.local";
        var username = !string.IsNullOrWhiteSpace(telegramUsername) ? telegramUsername : $"tg_{telegramUserId}";

        var user = new User
        {
            HasDataStorageConsent = true,
            AllowsMarketingEmails = false,
            CreatedOn = DateTime.UtcNow,
            IsActive = true,
            TierId = tierId,
            Profile = UserProfile.Create(
                string.Empty,
                email,
                firstName,
                lastName,
                null,
                null,
                null,
                1,
                profilePictureUrl)
        };

        user.Profile.AttachToUser(user);
        user.Profile.ConfirmEmail();
        user.Profile.UpdateTelegramUserId(telegramUserId);
        user.Profile.UpdateUsername(username);
        user.RaiseDomainEvent(new UserRegisteredDomainEvent(email));

        return user;
    }

    private static string HashPassword(string password) =>
        string.IsNullOrWhiteSpace(password) ? throw new ArgumentException("password cannot be empty.", nameof(password)) : BCrypt.Net.BCrypt.HashPassword(password, BCrypt.Net.BCrypt.GenerateSalt());
}
