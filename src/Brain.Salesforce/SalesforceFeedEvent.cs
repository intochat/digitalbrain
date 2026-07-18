using Brain.Contracts;

namespace DigitalBrain.Salesforce;

[GenerateSerializer, Alias("digitalbrain.salesforce.feed-event.v1")]
public sealed record SalesforceFeedEvent(
    [property: Id(0)] string Kind,
    [property: Id(1)] Guid EffectId,
    [property: Id(2)] string IdempotencyKey,
    [property: Id(3)] string SurfaceSummary,
    [property: Id(4)] string ObjectType,
    [property: Id(5)] string RecordId,
    [property: Id(6)] IReadOnlyDictionary<string, string> Fields,
    [property: Id(7)] UiFeedCandidate? UiCandidate = null)
{
    public const string UiSurfaceKind = "ui.surface";
    public const string UpdateEffectKind = "effect.update";
    public const string UpdateCompletedKind = "effect.update.completed";
    public const string UpdateFailedKind = "effect.update.failed";

    public static SalesforceFeedEvent UiSurface(string surfaceSummary, UiFeedCandidate? uiCandidate = null) =>
        new(UiSurfaceKind, Guid.Empty, string.Empty, surfaceSummary, string.Empty, string.Empty, new Dictionary<string, string>(), uiCandidate);

    public static SalesforceFeedEvent UpdateEffect(
        Guid effectId,
        string idempotencyKey,
        string objectType,
        string recordId,
        IReadOnlyDictionary<string, string> fields) =>
        new(UpdateEffectKind, effectId, idempotencyKey, "update-pending", objectType, recordId, fields);

    public static SalesforceFeedEvent UpdateCompleted(
        Guid effectId,
        string idempotencyKey,
        string surfaceSummary,
        UiFeedCandidate? uiCandidate = null) =>
        new(UpdateCompletedKind, effectId, idempotencyKey, surfaceSummary, string.Empty, string.Empty, new Dictionary<string, string>(), uiCandidate);

    public static SalesforceFeedEvent UpdateFailed(
        Guid effectId,
        string idempotencyKey,
        string surfaceSummary,
        UiFeedCandidate? uiCandidate = null) =>
        new(UpdateFailedKind, effectId, idempotencyKey, surfaceSummary, string.Empty, string.Empty, new Dictionary<string, string>(), uiCandidate);
}
