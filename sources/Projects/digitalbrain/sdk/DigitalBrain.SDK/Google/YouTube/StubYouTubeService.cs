namespace DigitalBrain.SDK.Google.YouTube;

// Deterministic fast-stage stub registered when DigitalBrain:Google:UseStubServices=true.
// Any non-empty query returns a fixed public video; the "no results" sentinel
// query returns null so the empty-result path is exercisable without network.
public sealed class StubYouTubeService : IYouTubeService
{
    public const string NoResultsQuery = "zzz-no-results";

    static readonly YouTubeVideo Fixture = new(
        VideoId: "tcodrIK2P_I",
        Title: "Stub Video",
        Channel: "DigitalBrain Stub",
        ThumbnailUrl: "https://i.ytimg.com/vi/tcodrIK2P_I/hqdefault.jpg");

    public Task<YouTubeVideo?> SearchTopAsync(
        string userAccountId, string query, CancellationToken ct) =>
        Task.FromResult(
            string.Equals(query, NoResultsQuery, StringComparison.OrdinalIgnoreCase)
                ? null
                : Fixture);
}
