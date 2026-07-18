using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.SeedWork;
using TripRadar.Server.Domain.ReferenceData;

namespace TripRadar.Server.Domain.Entities;

public class UserProfile : Entity<long>
{
    private UserProfile()
    {
    }

    private UserProfile(
        string password,
        string email,
        string? firstName = null,
        string? lastName = null,
        string? phoneNumber = null,
        string? ipAddress = null,
        string? googleId = null,
        int timezoneId = 1,
        string? profilePictureUrl = null,
        int? languageId = null,
        int? countryId = null)
    {
        Password = password;
        Email = NormalizeRequired(email, nameof(email));
        FirstName = NormalizeOptional(firstName);
        LastName = NormalizeOptional(lastName);
        PhoneNumber = NormalizeOptional(phoneNumber);
        IpAddress = NormalizeOptional(ipAddress);
        GoogleId = NormalizeOptional(googleId);
        TimezoneId = NormalizeTimezoneId(timezoneId);
        ProfilePictureUrl = NormalizeOptional(profilePictureUrl);
        LanguageId = languageId;
        CountryId = countryId;
        IsEmailConfirmed = false;
        SecurityStamp = CreateSecurityStamp();
    }

    public static UserProfile Create(
        string password,
        string email,
        string? firstName = null,
        string? lastName = null,
        string? phoneNumber = null,
        string? ipAddress = null,
        string? googleId = null,
        int timezoneId = 1,
        string? profilePictureUrl = null,
        int? languageId = null,
        int? countryId = null) =>
        new(
            password:  string.IsNullOrWhiteSpace(password) ? string.Empty : password.Trim(),
            email,
            firstName,
            lastName,
            phoneNumber,
            ipAddress,
            googleId,
            timezoneId,
            profilePictureUrl,
            languageId,
            countryId);

    public long UserId { get; private set; }

    public string? Username { get; private set; }

    public string Password { get; private set; } = null!;

    public string Email { get; private set; } = null!;

    public string? UsernameHash { get; private set; }

    public string? EmailHash { get; private set; }

    public bool IsEmailConfirmed { get; private set; }

    public string? EmailConfirmationToken { get; private set; }

    public DateTime? EmailConfirmationTokenExpiry { get; private set; }

    public string? PasswordResetToken { get; private set; }

    public DateTime? PasswordResetTokenExpiry { get; private set; }

    public string? FirstName { get; private set; }

    public string? LastName { get; private set; }

    public string? PhoneNumber { get; private set; }

    public string? IpAddress { get; private set; }

    public string RefreshToken { get; private set; } = string.Empty;

    public DateTime RefreshTokenExpiryTime { get; private set; }

    public string SecurityStamp { get; private set; } = CreateSecurityStamp();

    public string? GoogleId { get; private set; }

    public long? TelegramUserId { get; private set; }

    public int TimezoneId { get; private set; } = 1;

    public string? ProfilePictureUrl { get; private set; }

    public int? LanguageId { get; private set; }

    public int? CountryId { get; private set; }

    public int AccessFailedCount { get; private set; }

    public DateTime? LockoutEnd { get; private set; }

    public bool LockoutEnabled { get; private set; } = true;

    public User User { get; private set; } = null!;

    public Language? Language { get; private set; }

    public Country? Country { get; private set; }

    public Timezone? TimezoneReference { get; private set; }

    public string TimezoneCode => TimezoneReference?.TimezoneCode ?? "UTC";

    internal void AttachToUser(User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        User = user;
        if (user.Id > 0)
        {
            UserId = user.Id;
        }
    }

    public void UpdatePassword(string newPassword)
    {
        Password = NormalizeRequired(newPassword, nameof(newPassword));
        PasswordResetToken = null;
        PasswordResetTokenExpiry = null;
    }

    public void UpdateEmail(string email)
    {
        Email = NormalizeRequired(email, nameof(email));
        IsEmailConfirmed = false;
    }

    public void SetEmailConfirmationToken(string token, DateTime expiry)
    {
        EmailConfirmationToken = NormalizeRequired(token, nameof(token));
        EmailConfirmationTokenExpiry = DateTime.SpecifyKind(expiry, DateTimeKind.Utc);
    }

    public void ConfirmEmail()
    {
        IsEmailConfirmed = true;
        EmailConfirmationToken = null;
        EmailConfirmationTokenExpiry = null;
    }

    public void SetPasswordResetToken(string token, DateTime expiry)
    {
        PasswordResetToken = NormalizeRequired(token, nameof(token));
        PasswordResetTokenExpiry = DateTime.SpecifyKind(expiry, DateTimeKind.Utc);
    }

    public void UpdateTokenData(string refreshToken, DateTime refreshTokenExpiryTime)
    {
        RefreshToken = NormalizeRequired(refreshToken, nameof(refreshToken));
        RefreshTokenExpiryTime = DateTime.SpecifyKind(refreshTokenExpiryTime, DateTimeKind.Utc);
    }

    public void ClearRefreshToken()
    {
        RefreshToken = string.Empty;
        RefreshTokenExpiryTime = DateTime.MinValue;
    }

    public void RotateSecurityStamp()
    {
        SecurityStamp = CreateSecurityStamp();
    }

    public bool IsEmailConfirmationTokenValid(string token)
    {
        return EmailConfirmationToken == token &&
               EmailConfirmationTokenExpiry.HasValue &&
               EmailConfirmationTokenExpiry.Value > DateTime.UtcNow;
    }

    public bool IsPasswordResetTokenValid(string token)
    {
        return PasswordResetToken == token &&
               PasswordResetTokenExpiry.HasValue &&
               PasswordResetTokenExpiry.Value > DateTime.UtcNow;
    }

    public void UpdateGoogleData(string googleId)
    {
        GoogleId = NormalizeRequired(googleId, nameof(googleId));
    }

    public void UpdateTelegramUserId(long telegramUserId)
    {
        TelegramUserId = telegramUserId;
    }

    public void UpdatePersonalInfo(string? firstName, string? lastName, string? phoneNumber)
    {
        FirstName = NormalizeOptional(firstName);
        LastName = NormalizeOptional(lastName);
        PhoneNumber = NormalizeOptional(phoneNumber);
    }

    public void UpdateProfilePicture(string? profilePictureUrl)
    {
        ProfilePictureUrl = NormalizeOptional(profilePictureUrl);
    }

    public void UpdateProfile(string? firstName, string? lastName, string? phoneNumber, int? timezoneId, string? profilePictureUrl, int? languageId, int? countryId = null)
    {
        if (firstName is not null)
        {
            FirstName = NormalizeOptional(firstName);
        }

        if (lastName is not null)
        {
            LastName = NormalizeOptional(lastName);
        }

        if (phoneNumber is not null)
        {
            PhoneNumber = NormalizeOptional(phoneNumber);
        }

        if (timezoneId.HasValue)
        {
            TimezoneId = NormalizeTimezoneId(timezoneId.Value);
        }

        if (profilePictureUrl is not null)
        {
            ProfilePictureUrl = NormalizeOptional(profilePictureUrl);
        }

        if (languageId.HasValue)
        {
            LanguageId = languageId.Value;
        }

        if (countryId.HasValue)
        {
            CountryId = countryId.Value;
        }
    }

    public void UpdateUsername(string username)
    {
        Username = NormalizeRequired(username, nameof(username));
    }

    public void UpdateUsernameHash(string? usernameHash)
    {
        UsernameHash = NormalizeOptional(usernameHash);
    }

    public void UpdateEmailHash(string? emailHash)
    {
        EmailHash = NormalizeOptional(emailHash);
    }

    public bool IsLockedOut()
    {
        return LockoutEnd.HasValue && LockoutEnd.Value > DateTime.UtcNow;
    }

    public int IncrementAccessFailedCount(int maxFailedAttempts = 5, TimeSpan? lockoutDuration = null)
    {
        AccessFailedCount++;

        if (!LockoutEnabled || AccessFailedCount < maxFailedAttempts)
        {
            return AccessFailedCount;
        }

        var duration = lockoutDuration ?? TimeSpan.FromMinutes(15);
        LockoutEnd = DateTime.UtcNow.Add(duration);
        return AccessFailedCount;
    }

    public void ResetAccessFailedCount()
    {
        AccessFailedCount = 0;
        LockoutEnd = null;
    }

    private static string NormalizeRequired(string value, string paramName) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{paramName} cannot be empty.", paramName) : value.Trim();

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int NormalizeTimezoneId(int timezoneId) => timezoneId > 0 ? timezoneId : 1;

    private static string CreateSecurityStamp() => Guid.NewGuid().ToString("N");
}

