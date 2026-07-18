using Brain.Contracts;

namespace DigitalBrain.Google;

[GenerateSerializer, Alias("digitalbrain.google.feed-event.v1")]
public sealed record GmailFeedEvent(
    [property: Id(0)] string Kind,
    [property: Id(1)] Guid EffectId,
    [property: Id(2)] string IdempotencyKey,
    [property: Id(3)] string SurfaceSummary,
    [property: Id(4)] string To,
    [property: Id(5)] string Subject,
    [property: Id(6)] string Body,
    [property: Id(7)] UiFeedCandidate? UiCandidate = null)
{
    public const string UiSurfaceKind = "ui.surface";
    public const string SendEffectKind = "effect.send";
    public const string SendCompletedKind = "effect.send.completed";
    public const string SendFailedKind = "effect.send.failed";

    public static GmailFeedEvent UiSurface(string surfaceSummary, UiFeedCandidate? uiCandidate = null) =>
        new(UiSurfaceKind, Guid.Empty, string.Empty, surfaceSummary, string.Empty, string.Empty, string.Empty, uiCandidate);

    public static GmailFeedEvent SendEffect(
        Guid effectId,
        string idempotencyKey,
        string to,
        string subject,
        string body) =>
        new(SendEffectKind, effectId, idempotencyKey, "send-pending", to, subject, body);

    public static GmailFeedEvent SendCompleted(
        Guid effectId,
        string idempotencyKey,
        string surfaceSummary,
        UiFeedCandidate? uiCandidate = null) =>
        new(SendCompletedKind, effectId, idempotencyKey, surfaceSummary, string.Empty, string.Empty, string.Empty, uiCandidate);

    public static GmailFeedEvent SendFailed(
        Guid effectId,
        string idempotencyKey,
        string surfaceSummary,
        UiFeedCandidate? uiCandidate = null) =>
        new(SendFailedKind, effectId, idempotencyKey, surfaceSummary, string.Empty, string.Empty, string.Empty, uiCandidate);
}
