using TripRadar.MiniApp.Client.Infrastructure.Contracts;
using TripRadar.MiniApp.Client.Infrastructure.Models.Common;

namespace TripRadar.MiniApp.Client.Infrastructure.Managers;

public sealed class UserManager(TripRadarApiClient client) : IUserManager
{
    public Task<UserProfile?> GetProfileAsync() => client.GetAsync<UserProfile>(ApiEndpoints.UserProfile);

    public Task<UserProfile?> UpdateProfileAsync(UpdateProfileRequest request) => client.PutAsync<UserProfile>(ApiEndpoints.UserProfile, request);

    public Task<LoginResponse?> CreatePortableSessionAsync() => client.PostAsync<LoginResponse>(ApiEndpoints.UserPortableSession, new { });

    public async Task<IReadOnlyList<UserPreferenceItem>> GetPreferencesAsync()
    {
        var response = await client.GetAsync<UserPreferencesResponse>(ApiEndpoints.UserPreferences);
        return response?.Preferences ?? [];
    }
}