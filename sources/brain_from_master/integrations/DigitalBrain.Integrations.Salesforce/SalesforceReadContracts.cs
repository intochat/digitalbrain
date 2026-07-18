namespace DigitalBrain.Integrations.Salesforce;

internal enum SalesforceReadFailure
{
    InvalidRequest,
    AccessDenied
}
internal sealed class SalesforceReadException(SalesforceReadFailure failure, string safeMessage, Exception? innerException = null) : Exception(safeMessage, innerException)
{
    public SalesforceReadFailure Failure { get; } = failure;
}
