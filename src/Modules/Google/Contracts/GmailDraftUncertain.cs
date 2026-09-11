namespace DigitalBrain.Google;

[GenerateSerializer, Alias("db.gmail.draft-uncertain")]
public sealed record GmailDraftUncertain([property: Id(0)] string PreviewId);
