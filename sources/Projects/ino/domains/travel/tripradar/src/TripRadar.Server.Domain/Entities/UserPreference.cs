using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Domain.SeedWork;

namespace TripRadar.Server.Domain.Entities;

public class UserPreference : Entity<long>
{
    private UserPreference() { }

    public UserPreference(long userId, int preferenceTypeId, string preferencesJson)
    {
        UserId = userId;
        PreferenceTypeId = preferenceTypeId;
        PreferencesJson = preferencesJson ?? throw new ArgumentNullException(nameof(preferencesJson));
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public long UserId { get; private set; }
    public User User { get; private set; } = null!;

    public int PreferenceTypeId { get; private set; }
    public PreferenceType PreferenceType { get; private set; } = null!;
    public string PreferencesJson { get; private set; } = null!;
    public bool IsActive { get; private set; } = true;

    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public void UpdatePreferences(string preferencesJson)
    {
        PreferencesJson = preferencesJson ?? throw new ArgumentNullException(nameof(preferencesJson));
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetActive(bool isActive)
    {
        IsActive = isActive;
        UpdatedAt = DateTime.UtcNow;
    }
}
