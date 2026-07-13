namespace DigitalBrain.Integrations.Google.Contracts;

public sealed class GmailMessageReadRequest
{
    public GmailMessageReadRequest(string messageId)
    {
        MessageId = ContractGuard.Required(messageId, nameof(messageId), 512);
    }

    public string MessageId { get; }
}

public sealed class GmailMessage
{
    public GmailMessage(
        string messageId,
        string? threadId,
        DateTimeOffset receivedAt,
        string? senderAddress,
        string? subject,
        string plainTextBody)
    {
        MessageId = ContractGuard.Required(messageId, nameof(messageId), 512);
        ThreadId = ContractGuard.Optional(threadId, nameof(threadId), 512);
        ReceivedAt = receivedAt;
        SenderAddress = ContractGuard.Optional(senderAddress, nameof(senderAddress), 320);
        Subject = ContractGuard.Optional(subject, nameof(subject), 998);
        PlainTextBody = ContractGuard.Bounded(plainTextBody, nameof(plainTextBody), 1_000_000);
    }

    public string MessageId { get; }
    public string? ThreadId { get; }
    public DateTimeOffset ReceivedAt { get; }
    public string? SenderAddress { get; }
    public string? Subject { get; }
    public string PlainTextBody { get; }
}

public interface IGmailMessageReader
{
    Task<GmailMessage> ReadAsync(
        GmailMessageReadRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class GmailMailboxReadRequest
{
    public GmailMailboxReadRequest(int limit = 20, string? continuationToken = null)
    {
        Limit = ContractGuard.Range(limit, nameof(limit), 1, 100);
        ContinuationToken = ContractGuard.Optional(continuationToken, nameof(continuationToken), 4_096);
    }

    public int Limit { get; }
    public string? ContinuationToken { get; }
}

public sealed class GmailMessageSummary
{
    public GmailMessageSummary(
        string messageId,
        string? threadId,
        DateTimeOffset receivedAt,
        string? senderAddress,
        string? subject)
    {
        MessageId = ContractGuard.Required(messageId, nameof(messageId), 512);
        ThreadId = ContractGuard.Optional(threadId, nameof(threadId), 512);
        ReceivedAt = receivedAt;
        SenderAddress = ContractGuard.Optional(senderAddress, nameof(senderAddress), 320);
        Subject = ContractGuard.Optional(subject, nameof(subject), 998);
    }

    public string MessageId { get; }
    public string? ThreadId { get; }
    public DateTimeOffset ReceivedAt { get; }
    public string? SenderAddress { get; }
    public string? Subject { get; }
}

public sealed class GmailMailboxPage
{
    public GmailMailboxPage(
        IReadOnlyList<GmailMessageSummary> messages,
        string? continuationToken = null)
    {
        Messages = ContractGuard.Copy(messages, nameof(messages), 100);
        ContinuationToken = ContractGuard.Optional(continuationToken, nameof(continuationToken), 4_096);
    }

    public IReadOnlyList<GmailMessageSummary> Messages { get; }
    public string? ContinuationToken { get; }
}

public interface IGmailMailboxReader
{
    Task<GmailMailboxPage> ReadAsync(
        GmailMailboxReadRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class GmailSendProposalRequest
{
    public GmailSendProposalRequest(
        string recipient,
        string subject,
        string body,
        string logicalOperationKey)
    {
        Recipient = ContractGuard.Required(recipient, nameof(recipient), 320);
        Subject = ContractGuard.Bounded(subject, nameof(subject), 998);
        Body = ContractGuard.Bounded(body, nameof(body), 100_000);
        LogicalOperationKey = ContractGuard.Required(logicalOperationKey, nameof(logicalOperationKey), 256);
    }

    public string Recipient { get; }
    public string Subject { get; }
    public string Body { get; }
    public string LogicalOperationKey { get; }
}

public sealed class GmailSendProposal
{
    public GmailSendProposal(
        string recipient,
        string subject,
        string body,
        string logicalOperationKey)
    {
        Recipient = ContractGuard.Required(recipient, nameof(recipient), 320);
        Subject = ContractGuard.Bounded(subject, nameof(subject), 998);
        Body = ContractGuard.Bounded(body, nameof(body), 100_000);
        LogicalOperationKey = ContractGuard.Required(logicalOperationKey, nameof(logicalOperationKey), 256);
    }

    public string Recipient { get; }
    public string Subject { get; }
    public string Body { get; }
    public string LogicalOperationKey { get; }
}

public interface IGmailSendProposer
{
    Task<GmailSendProposal> ProposeAsync(
        GmailSendProposalRequest request,
        CancellationToken cancellationToken = default);
}

internal static class ContractGuard
{
    internal static string Required(string value, string parameterName, int maximumLength)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength)
        {
            throw new ArgumentException($"Value must contain 1 to {maximumLength} characters.", parameterName);
        }

        return value;
    }

    internal static string Bounded(string value, string parameterName, int maximumLength)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (value.Length > maximumLength)
        {
            throw new ArgumentException($"Value must contain at most {maximumLength} characters.", parameterName);
        }

        return value;
    }

    internal static string? Optional(string? value, string parameterName, int maximumLength)
    {
        if (value is not null && value.Length > maximumLength)
        {
            throw new ArgumentException($"Value must contain at most {maximumLength} characters.", parameterName);
        }

        return value;
    }

    internal static int Range(int value, string parameterName, int minimum, int maximum)
    {
        if (value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, $"Value must be between {minimum} and {maximum}.");
        }

        return value;
    }

    internal static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> values, string parameterName, int maximumCount)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        if (values.Count > maximumCount)
        {
            throw new ArgumentException($"Collection must contain at most {maximumCount} items.", parameterName);
        }

        var copy = new T[values.Count];
        for (var index = 0; index < values.Count; index++)
        {
            if (values[index] is null)
            {
                throw new ArgumentException("Collection cannot contain null items.", parameterName);
            }

            copy[index] = values[index];
        }

        return Array.AsReadOnly(copy);
    }
}
