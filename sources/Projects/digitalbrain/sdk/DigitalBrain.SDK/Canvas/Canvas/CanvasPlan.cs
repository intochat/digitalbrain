using System.Text.Json;
using DigitalBrain.Runtime.Ui;
using DigitalBrain.Runtime.Filters;
using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.Canvas.Canvas;

// Pure decision logic for the Canvas neuron: card shape, scene-name
// normalization, and the upsert/lookup over an immutable view of the
// durable scene list. No Orleans/grain context, so it's unit-testable
// without booting Aspire. The thin CanvasNeuron grain owns durable
// bookkeeping + FireSynapseAsync; this owns the synapse shapes and the
// card JSON.
public static class CanvasPlan
{
    public const string CardLibrary = "digitalbrain";
    public const string CardRootWidget = "CanvasCard";
    public const string DefaultSceneName = "default";

    public static string NormalizeSceneName(string? sceneName) =>
        string.IsNullOrWhiteSpace(sceneName) ? DefaultSceneName : sceneName.Trim();

    public static string LatestContent(
        IEnumerable<CanvasSceneRecord> scenes,
        string userId,
        string sceneName) =>
        scenes
            .Where(s => s.UserId == userId && s.SceneName == sceneName)
            .OrderByDescending(s => s.UpdatedUtc)
            .Select(s => s.Content)
            .FirstOrDefault() ?? string.Empty;

    public static string CardDataJson(string sceneName, string content) =>
        JsonSerializer.Serialize(new
        {
            sceneName,
            content,
        });

    public static CanvasReady ToCanvasReady(OpenCanvasRequest req, string content) =>
        new(UserId:             req.UserId,
        SceneName:          NormalizeSceneName(req.SceneName),
        Content:            content) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: req.CorrelationId,
            causationId: req.SynapseId,
            callerNeuronId: default,
            callerNeuronType: null,
            receiverNeuronId: req.CallerNeuronId,
            receiverNeuronType: req.CallerNeuronType ?? CallerStampingOutgoingFilter.ExternalCallerSentinel,
            timestamp: default
        ) };

    public static RfwCard ToCanvasCard(OpenCanvasRequest req, string content) =>
        new(LibraryName:        CardLibrary,
        RootWidget:         CardRootWidget,
        DataJson:           CardDataJson(NormalizeSceneName(req.SceneName), content)) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: req.CorrelationId,
            causationId: req.SynapseId,
            callerNeuronId: default,
            callerNeuronType: null,
            receiverNeuronId: Guid.Empty,
            receiverNeuronType: "HomeFeed",
            timestamp: default
        ) };

    public static CanvasSaved ToCanvasSaved(SaveCanvas save) =>
        new(UserId:             save.UserId,
        SceneName:          NormalizeSceneName(save.SceneName)) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: save.CorrelationId,
            causationId: save.SynapseId,
            callerNeuronId: default,
            callerNeuronType: null,
            receiverNeuronId: save.CallerNeuronId,
            receiverNeuronType: save.CallerNeuronType ?? CallerStampingOutgoingFilter.ExternalCallerSentinel,
            timestamp: default
        ) };
}
