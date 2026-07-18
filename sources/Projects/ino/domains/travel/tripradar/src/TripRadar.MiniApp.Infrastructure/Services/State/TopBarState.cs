using TripRadar.MiniApp.Client.Infrastructure.Contracts;
using TripRadar.MiniApp.Client.Infrastructure.Models.Common;

namespace TripRadar.MiniApp.Client.Infrastructure.Services.State;

public sealed class TopBarState
{
    private readonly IUserManager _userApi;
    private readonly TrackingState _trackingState;

    public TopBarState(IUserManager userApi, TrackingState trackingState)
    {
        _userApi = userApi;
        _trackingState = trackingState;
        _trackingState.OnChanged += () => OnChanged?.Invoke();
    }

    public UserProfile? Profile { get; private set; }
    public int AlertCount => _trackingState.ActiveCount;
    public bool IsLoaded { get; private set; }

    public event Action? OnChanged;

    public async Task LoadAsync()
    {
        if (IsLoaded) return;

        try
        {
            var profileTask = _userApi.GetProfileAsync();
            var trackingTask = _trackingState.IsLoaded
                ? Task.CompletedTask
                : _trackingState.LoadAsync();

            await Task.WhenAll(profileTask, trackingTask);

            Profile = await profileTask;
        }
        catch { }

        IsLoaded = true;
        OnChanged?.Invoke();
    }

    public void Reset()
    {
        Profile = null;
        IsLoaded = false;
        OnChanged?.Invoke();
    }
}