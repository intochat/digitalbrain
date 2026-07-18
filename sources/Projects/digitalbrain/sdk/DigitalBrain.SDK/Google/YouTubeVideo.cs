namespace DigitalBrain.SDK.Google;

[GenerateSerializer]
public sealed record YouTubeVideo(
    [property: Id(0)] string VideoId,
    [property: Id(1)] string Title,
    [property: Id(2)] string Channel,
    [property: Id(3)] string ThumbnailUrl)
{
    public bool IsEmpty => string.IsNullOrEmpty(VideoId);

    public static YouTubeVideo None { get; } = new("", "", "", "");
}
