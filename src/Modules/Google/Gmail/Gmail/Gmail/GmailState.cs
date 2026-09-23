using DigitalBrain.Contracts.Types;

namespace DigitalBrain.Google.Gmail;

[GenerateSerializer, Alias("db.gmail.state")]
internal sealed record GmailState
{
    [Id(0)] public string? Subject { get; init; }
    [Id(1)] public string? Email { get; init; }
    [Id(2)] public string? AccessToken { get; init; }
    [Id(3)] public string? RefreshToken { get; init; }
    [Id(4)] public string GrantedScopes { get; init; } = "";
    [Id(5)] public DateTimeOffset? ExpiresAt { get; init; }
    [Id(6)] public bool CanCompose { get; init; }
    [Id(7)] public GmailDraftPreview? PendingDraft { get; init; }
    [Id(8)] public GmailDraftPreview? SubmittingDraft { get; init; }
    [Id(9)] public SecretRef? Credential { get; init; }
    [Id(10)] public SecretRef? RefreshCredential { get; init; }
}
