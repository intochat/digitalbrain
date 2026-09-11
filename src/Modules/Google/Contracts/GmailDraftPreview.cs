namespace DigitalBrain.Google;

[GenerateSerializer]
[Alias("db.gmail.draft-preview")]
public sealed record GmailDraftPreview(
    [property: Id(0)] string PreviewId,
    [property: Id(1)] string ToolSchemaHash,
    [property: Id(2)] IReadOnlyList<string> To,
    [property: Id(3)] IReadOnlyList<string> Cc,
    [property: Id(4)] IReadOnlyList<string> Bcc,
    [property: Id(5)] string Subject,
    [property: Id(6)] string Body,
    [property: Id(7)] DateTimeOffset ExpiresAt,
    [property: Id(8)] string? DraftId = null);
