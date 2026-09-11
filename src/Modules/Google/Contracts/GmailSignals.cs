namespace DigitalBrain.Google;

public static class GmailSignals
{
    public const string GmailDisconnectionRequested = nameof(GmailDisconnectionRequested);
    public const string GmailRefreshRequested = nameof(GmailRefreshRequested);
    public const string GmailConnectionRequested = nameof(GmailConnectionRequested);
    public const string GmailConnected = nameof(GmailConnected);
    public const string GmailRefreshed = nameof(GmailRefreshed);
    public const string GmailConnectionRejected = nameof(GmailConnectionRejected);
    public const string GmailDisconnected = nameof(GmailDisconnected);
    public const string GmailDraftRequested = nameof(GmailDraftRequested);
    public const string GmailDraftPrepared = nameof(GmailDraftPrepared);
    public const string GmailDraftSubmitting = nameof(GmailDraftSubmitting);
    public const string GmailDraftConfirmed = nameof(GmailDraftConfirmed);
    public const string GmailDraftCreated = nameof(GmailDraftCreated);
    public const string GmailDraftUncertain = nameof(GmailDraftUncertain);
}
