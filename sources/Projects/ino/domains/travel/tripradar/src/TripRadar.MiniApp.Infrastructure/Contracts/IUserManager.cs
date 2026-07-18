using TripRadar.MiniApp.Client.Infrastructure.Models.Common;

namespace TripRadar.MiniApp.Client.Infrastructure.Contracts;

public interface IUserManager : IManager
{
    Task<UserProfile?> GetProfileAsync();
    Task<UserProfile?> UpdateProfileAsync(UpdateProfileRequest request);
    Task<LoginResponse?> CreatePortableSessionAsync();
    Task<IReadOnlyList<UserPreferenceItem>> GetPreferencesAsync();
}