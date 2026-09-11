namespace DigitalBrain.Google;

[GenerateSerializer, Alias("db.gmail.draft-created")]
public sealed record GmailDraftCreated([property: Id(0)] string PreviewId, [property: Id(1)] string DraftId);
