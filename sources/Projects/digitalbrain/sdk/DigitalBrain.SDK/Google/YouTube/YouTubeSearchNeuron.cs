using DigitalBrain.Runtime;
using DigitalBrain.Runtime.Neurons;
using DigitalBrain.SDK.Google.Auth;
using DigitalBrain.SDK.Google;
using Orleans.Journaling;

namespace DigitalBrain.SDK.Google.YouTube;

[ImplicitStreamSubscription(YouTubeSearchNeuronType)]
internal sealed class YouTubeSearchNeuron(
    [FromKeyedServices("incoming")] IDurableList<Synapse> incoming,
    [FromKeyedServices("outgoing")] IDurableList<Synapse> outgoing,
    IGrainFactory grains,
    IGoogleAuthBroker broker,
    IYouTubeService youTubeService,
    ILogger<YouTubeSearchNeuron> logger)
    : Neuron(incoming, outgoing, grains, logger),
      IYouTube,
      INeuronMetadata,
      IExternalNeuron,
      IHandle<FindVideoRequest>
{
    public const string YouTubeSearchNeuronType = nameof(YouTubeSearchNeuron);
    static readonly string[] Scopes = ["https://www.googleapis.com/auth/youtube.readonly"];
    const string DefaultConsentUrl = "https://accounts.google.com/o/oauth2/auth";

    public static NeuronId Id => new("google/youtube");
    public static string Icon => "youtube";
    public static NeuronCapability Capabilities => NeuronCapability.External;

    protected override async Task HandleSynapseAsync(Synapse synapse)
    {
        if (synapse is FindVideoRequest request)
            await HandleFindAsync(request);
    }

    async Task HandleFindAsync(FindVideoRequest request)
    {
        var hasToken = await broker.HasStoredTokenAsync(
            request.UserAccountId, Scopes, CancellationToken.None);
        if (!hasToken)
        {
            await FireSynapseAsync(new OAuthConsentRequired(UserAccountId: request.UserAccountId,
        ConsentUrl: DefaultConsentUrl,
        Scopes: Scopes) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: request.CorrelationId,
            causationId: request.SynapseId,
            callerNeuronId: default,
            callerNeuronType: null,
            receiverNeuronId: request.CallerNeuronId,
            receiverNeuronType: request.CallerNeuronType ?? "External",
            timestamp: default
        ) });
            return;
        }

        var video = await youTubeService.SearchTopAsync(
            request.UserAccountId, request.Query, CancellationToken.None);

        await FireSynapseAsync(YouTubePlan.ToVideoFound(request, video));
        if (video is not null && !video.IsEmpty)
            await FireSynapseAsync(YouTubePlan.ToVideoCard(request, video));
    }
}
