using DigitalBrain.SDK.Google.Auth;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;

namespace DigitalBrain.SDK.Google.YouTube;

internal sealed class GoogleYouTubeService(GoogleAuthBroker broker) : IYouTubeService
{
    static readonly string[] Scopes = ["https://www.googleapis.com/auth/youtube.readonly"];

    public async Task<YouTubeVideo?> SearchTopAsync(
        string userAccountId, string query, CancellationToken ct)
    {
        var credential = await broker.GetCredentialAsync(userAccountId, Scopes, ct)
            ?? throw new InvalidOperationException(
                $"No credential for '{userAccountId}'. Authorize the youtube.readonly scope first.");

        using var svc = new YouTubeService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "DigitalBrain",
        });

        var search = svc.Search.List("snippet");
        search.Q = query;
        search.Type = "video";
        search.MaxResults = 1;
        var resp = await search.ExecuteAsync(ct);

        var item = resp.Items?.FirstOrDefault();
        if (item?.Id?.VideoId is not { Length: > 0 } videoId) return null;

        var snippet = item.Snippet;
        return new YouTubeVideo(
            VideoId: videoId,
            Title: snippet?.Title ?? "",
            Channel: snippet?.ChannelTitle ?? "",
            ThumbnailUrl: snippet?.Thumbnails?.Medium?.Url ?? "");
    }
}
