namespace DigitalBrain.Google.Gmail;

public sealed record GmailAccountStatus(GmailConnection Connection, Uri? LoginUrl);