using System.Text.Json;
using DigitalBrain.Kernel.Contracts;
namespace DigitalBrain.Integrations.Google;

internal sealed record GmailMessageReceived(
    BrainOwnerId OwnerId,
    string EventId,
    string MessageId,
    string ThreadId,
    string HistoryId,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    string? CausationId,
    string TraceId);
internal interface IGmailWatchEventHandler
{
    Task<FeatureFanOutResult> HandleAsync(GmailMessageReceived message, CancellationToken cancellationToken = default);
}
internal sealed class GmailWatchEventHandler(IFeatureGrainResolver grains) : IGmailWatchEventHandler
{
    public const string EventKind = "gmail.message.received.v1";
    public Task<FeatureFanOutResult> HandleAsync(GmailMessageReceived message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        Validate(message.EventId, nameof(message.EventId));
        Validate(message.MessageId, nameof(message.MessageId));
        Validate(message.ThreadId, nameof(message.ThreadId));
        Validate(message.HistoryId, nameof(message.HistoryId));
        Validate(message.CorrelationId, nameof(message.CorrelationId));
        Validate(message.TraceId, nameof(message.TraceId));
        if (message.CausationId is not null) Validate(message.CausationId, nameof(message.CausationId));
        if (message.OccurredAt.Offset != TimeSpan.Zero)
            throw new ArgumentException("Gmail event timestamps must be UTC.", nameof(message));
        var payload = JsonSerializer.Serialize(new GmailMessageFacts(message.MessageId, message.ThreadId, message.HistoryId));
        var input = new FeatureInput(message.EventId, EventKind, payload, message.OccurredAt, message.CorrelationId, message.TraceId, message.CausationId);
        return grains.Hub(message.OwnerId).PublishAsync(input).WaitAsync(cancellationToken);
    }
    private static void Validate(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > 256 || value.Any(char.IsControl) || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("A bounded canonical Gmail event identifier is required.", parameterName);
    }
    private sealed record GmailMessageFacts(string MessageId, string ThreadId, string HistoryId);
}
