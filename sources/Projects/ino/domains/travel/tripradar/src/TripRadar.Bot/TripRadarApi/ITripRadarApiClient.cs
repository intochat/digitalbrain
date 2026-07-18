namespace TripRadar.Bot.TripRadarApi;

public interface ITripRadarApiClient
{
    Task<IReadOnlyList<ActiveTracking>> LoadActiveTrackingsAsync(CancellationToken ct = default);
}
