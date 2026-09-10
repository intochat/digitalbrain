namespace DigitalBrain.Salesforce;

public static class SalesforceSignals
{
    public const string SalesforceConnected = nameof(SalesforceConnected);
    public const string SalesforceDisconnected = nameof(SalesforceDisconnected);
    public const string SalesforceWriteRequested = nameof(SalesforceWriteRequested);
    public const string SalesforceWritePrepared = nameof(SalesforceWritePrepared);
    public const string SalesforceWriteConfirmed = nameof(SalesforceWriteConfirmed);
    public const string RecordWritten = nameof(RecordWritten);
    public const string SalesforceWriteFailed = nameof(SalesforceWriteFailed);
    public const string SalesforceWriteUncertain = nameof(SalesforceWriteUncertain);
}

[GenerateSerializer, Alias("db.salesforce.connected")]
public sealed record SalesforceConnected([property: Id(0)] SalesforceConnection Connection);

[GenerateSerializer, Alias("db.salesforce.disconnected")]
public sealed record SalesforceDisconnected;

[GenerateSerializer, Alias("db.salesforce.write-requested")]
public sealed record SalesforceWriteRequested([property: Id(0)] SalesforceWritePreview Preview, [property: Id(1)] string InstanceUrl);

[GenerateSerializer, Alias("db.salesforce.write-prepared")]
public sealed record SalesforceWritePrepared([property: Id(0)] SalesforceWritePreview Preview);

[GenerateSerializer, Alias("db.salesforce.write-confirmed")]
public sealed record SalesforceWriteConfirmed([property: Id(0)] SalesforceWritePreview Preview);

[GenerateSerializer, Alias("db.salesforce.record-written")]
public sealed record RecordWritten([property: Id(0)] SalesforceWritePreview Preview);

[GenerateSerializer, Alias("db.salesforce.write-failed")]
public sealed record SalesforceWriteFailed([property: Id(0)] string PreviewId);

[GenerateSerializer, Alias("db.salesforce.write-uncertain")]
public sealed record SalesforceWriteUncertain([property: Id(0)] string PreviewId);
