namespace DigitalBrain.Google;

[GenerateSerializer, Alias("db.gmail.state")]
internal sealed record GmailState(
    [property: Id(0)] string? Subject = null,
    [property: Id(1)] string? Email = null,
    [property: Id(2)] string? AccessToken = null,
    [property: Id(3)] string? RefreshToken = null,
    [property: Id(4)] string GrantedScopes = "",
    [property: Id(5)] DateTimeOffset? ExpiresAt = null,
    [property: Id(6)] bool CanCompose = false,
    [property: Id(7)] GmailDraftPreview? PendingDraft = null,
    [property: Id(8)] GmailDraftPreview? SubmittingDraft = null);
