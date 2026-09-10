namespace DigitalBrain.Salesforce;

public static class SalesforceSignals
{
    public const string SalesforceConnectionRejected = nameof(SalesforceConnectionRejected);
    public const string SalesforceRefreshed = nameof(SalesforceRefreshed);
    public const string SalesforceRefreshRequested = nameof(SalesforceRefreshRequested);
    public const string SalesforceDisconnectionRequested = nameof(SalesforceDisconnectionRequested);
    public const string SalesforceConnectionRequested = nameof(SalesforceConnectionRequested);
    public const string SalesforceConnected = nameof(SalesforceConnected);
    public const string SalesforceDisconnected = nameof(SalesforceDisconnected);
    public const string SalesforceWriteRequested = nameof(SalesforceWriteRequested);
    public const string SalesforceWritePrepared = nameof(SalesforceWritePrepared);
    public const string SalesforceWriteConfirmed = nameof(SalesforceWriteConfirmed);
    public const string RecordWritten = nameof(RecordWritten);
    public const string SalesforceWriteFailed = nameof(SalesforceWriteFailed);
    public const string SalesforceWriteUncertain = nameof(SalesforceWriteUncertain);
}
