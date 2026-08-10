using System.Text.Json.Serialization;
using DigitalBrain.UI;

namespace DigitalBrain.Kernel;

internal sealed record OwnerCommandRequest(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("chatName")] string? ChatName = null,
    [property: JsonPropertyName("text")] string? Text = null,
    [property: JsonPropertyName("commandId")] string? CommandId = null,
    [property: JsonPropertyName("offerCommandId")] string? OfferCommandId = null,
    [property: JsonPropertyName("buttonId")] string? ButtonId = null,
    [property: JsonPropertyName("action")] string? Action = null,
    [property: JsonPropertyName("surfaceName")] string? SurfaceName = null,
    [property: JsonPropertyName("surfaceKey")] string? SurfaceKey = null,
    [property: JsonPropertyName("title")] string? Title = null);

internal sealed record ChatTurnEvent(
    long Sequence,
    bool FromUser,
    string Text,
    string CommandId,
    string Synapse,
    string NeuronId,
    string Caller,
    string CorrelationId,
    DateTimeOffset Timestamp,
    ChatButtonOffer[]? Buttons = null,
    ChatChartOffer[]? Charts = null,
    ChatTimerOffer[]? Timers = null);

internal sealed record SurfaceOpenedEvent(
    long Sequence,
    string SurfaceKey,
    string Title,
    string CommandId,
    string Surface);

internal sealed record AuthorizationEvent(
    long Sequence,
    string Kind,
    string CommandId,
    string ServerKey,
    string? ServerDisplayName,
    string? SignInUrl,
    string State,
    DateTimeOffset Timestamp);

internal sealed record BrainTopologySnapshot(
    IReadOnlyList<BrainModule> Modules,
    IReadOnlyList<BrainNeuron> Neurons,
    DateTimeOffset ObservedAt,
    IReadOnlyList<BrainConnection> Connections,
    IReadOnlyList<BrainBroadcastRoute> BroadcastRoutes);

internal sealed record BrainBroadcastRoute(string SynapseAlias, string HandlerGrainType);

internal sealed record BrainModule(string Id);

internal sealed record BrainNeuron(string Id, string GrainType, string Identity, string Placement);

internal sealed record BrainConnection(
    Guid ConnectionId,
    string Source,
    string SynapseAlias,
    string Target,
    string? Transform,
    DateTimeOffset? ExpiresAt);

internal sealed record GraphEvent(
    long Sequence,
    string Kind,
    Guid ConnectionId,
    string? Source,
    string? SynapseAlias,
    string? Target,
    DateTimeOffset Timestamp);
