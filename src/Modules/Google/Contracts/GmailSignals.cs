namespace DigitalBrain.Google;

public static class GmailSignals
{
    public const string GmailConnected = nameof(GmailConnected);
    public const string GmailDisconnected = nameof(GmailDisconnected);
    public const string GmailDraftRequested = nameof(GmailDraftRequested);
    public const string GmailDraftPrepared = nameof(GmailDraftPrepared);
    public const string GmailDraftConfirmed = nameof(GmailDraftConfirmed);
    public const string GmailDraftCreated = nameof(GmailDraftCreated);
    public const string GmailDraftUncertain = nameof(GmailDraftUncertain);
}

[GenerateSerializer, Alias("db.gmail.connected")]
public sealed record GmailConnected([property: Id(0)] GmailConnection Connection);

[GenerateSerializer, Alias("db.gmail.disconnected")]
public sealed record GmailDisconnected;

[GenerateSerializer, Alias("db.gmail.draft-requested")]
public sealed record GmailDraftRequested([property: Id(0)] GmailDraftPreview Preview, [property: Id(1)] string AccountSubject);

[GenerateSerializer, Alias("db.gmail.draft-prepared")]
public sealed record GmailDraftPrepared([property: Id(0)] GmailDraftPreview Preview);

[GenerateSerializer, Alias("db.gmail.draft-confirmed")]
public sealed record GmailDraftConfirmed([property: Id(0)] GmailDraftPreview Preview);

[GenerateSerializer, Alias("db.gmail.draft-created")]
public sealed record GmailDraftCreated([property: Id(0)] GmailDraftPreview Preview);

[GenerateSerializer, Alias("db.gmail.draft-uncertain")]
public sealed record GmailDraftUncertain([property: Id(0)] string PreviewId);
