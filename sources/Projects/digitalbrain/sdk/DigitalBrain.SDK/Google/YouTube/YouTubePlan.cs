using System.Text.Json;
using DigitalBrain.Runtime.Ui;
using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.Google.YouTube;

// Pure decision/shape logic for the video card. No grain context, so the card
// contract is unit-testable without booting Aspire. The native Flutter card
// (DigitalBrainWidgets 'VideoPlayerCard') reads these DataJson keys directly; unlike
// OnboardingPlan there is no embedded RFW source — this is a native card.
public static class YouTubePlan
{
    public const string CardLibrary = "digitalbrain";
    public const string CardRootWidget = "VideoPlayerCard";

    public static string CardDataJson(YouTubeVideo video) =>
        JsonSerializer.Serialize(new
        {
            kind = "youtube",
            videoId = video.VideoId,
            title = video.Title,
            channel = video.Channel,
            thumbnailUrl = video.ThumbnailUrl,
            autoplay = true,
        });

    public static VideoFound ToVideoFound(FindVideoRequest req, YouTubeVideo? video) =>
        new(UserAccountId:      req.UserAccountId,
        Video:              video ?? YouTubeVideo.None) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: req.CorrelationId,
            causationId: req.SynapseId,
            callerNeuronId: default,
            callerNeuronType: null,
            receiverNeuronId: req.CallerNeuronId,
            receiverNeuronType: req.CallerNeuronType ?? "External",
            timestamp: default
        ) };

    public static RfwCard ToVideoCard(FindVideoRequest req, YouTubeVideo video) =>
        new(LibraryName:        CardLibrary,
        RootWidget:         CardRootWidget,
        DataJson:           CardDataJson(video)) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: req.CorrelationId,
            causationId: req.SynapseId,
            callerNeuronId: default,
            callerNeuronType: null,
            receiverNeuronId: Guid.Empty,
            receiverNeuronType: "HomeFeed",
            timestamp: default
        ) };
}
