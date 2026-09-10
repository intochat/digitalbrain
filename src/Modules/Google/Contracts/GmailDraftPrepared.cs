namespace DigitalBrain.Google;

[GenerateSerializer, Alias("db.gmail.draft-prepared")]
public sealed record GmailDraftPrepared([property: Id(0)] string PreviewId, [property: Id(1)] string ToolSchemaHash);
