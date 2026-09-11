namespace DigitalBrain.Google;

[GenerateSerializer, Alias("db.gmail.draft-requested")]
public sealed record GmailDraftRequested([property: Id(0)] GmailDraftPreview Preview, [property: Id(1)] string AccountSubject);
