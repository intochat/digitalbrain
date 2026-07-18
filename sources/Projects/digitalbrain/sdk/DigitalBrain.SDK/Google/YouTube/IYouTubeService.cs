namespace DigitalBrain.SDK.Google.YouTube;

public interface IYouTubeService
{
    Task<YouTubeVideo?> SearchTopAsync(
        string userAccountId, string query, CancellationToken ct);
}
