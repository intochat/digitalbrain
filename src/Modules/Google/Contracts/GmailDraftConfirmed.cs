namespace DigitalBrain.Google;

[GenerateSerializer, Alias("db.gmail.draft-confirmed")]
public sealed record GmailDraftConfirmed([property: Id(0)] string PreviewId, [property: Id(1)] string ToolSchemaHash);
