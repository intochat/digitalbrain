namespace DigitalBrain.Salesforce;

public enum SalesforceReadFailure
{
    InvalidRequest,
    AccessDenied,
    LimitReached,
    ContinuationExpired,
    Unsupported
}

public sealed class SalesforceReadException(
    SalesforceReadFailure failure,
    string safeMessage,
    Exception? innerException = null) : Exception(safeMessage, innerException)
{
    public SalesforceReadFailure Failure { get; } = failure;

    internal static SalesforceReadException Unsupported() =>
        new(SalesforceReadFailure.Unsupported, "This Salesforce read capability is unavailable.");
}

public sealed record SalesforceProviderScope(
    string OrganizationId,
    string SalesforceUserId);

public sealed class SalesforceContinuation
{
    internal SalesforceContinuation(
        string nextRecordsUrl,
        SalesforceProviderScope scope,
        string entityLabel,
        string recordIdField,
        IReadOnlyDictionary<string, string> fieldLabels)
    {
        NextRecordsUrl = nextRecordsUrl;
        Scope = scope;
        EntityLabel = entityLabel;
        RecordIdField = recordIdField;
        FieldLabels = fieldLabels;
    }

    internal string NextRecordsUrl { get; }
    internal string EntityLabel { get; }
    internal string RecordIdField { get; }
    internal IReadOnlyDictionary<string, string> FieldLabels { get; }
    public SalesforceProviderScope Scope { get; }
}

public sealed record SalesforceReadPage(
    string Content,
    int ReturnedCount,
    int? TotalSize,
    SalesforceProviderScope Scope,
    SalesforceContinuation? Continuation = null);
