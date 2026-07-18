using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace DigitalBrain;

public static class DigitalBrainClientTelemetry
{
    public const string ActivitySourceName = "DigitalBrain.Client";
    public const string MeterName = "DigitalBrain.Client";

    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> SubmittedTurns =
        Meter.CreateCounter<long>("digitalbrain.client.conversation.turns");
    private static readonly Counter<long> FailedTurns =
        Meter.CreateCounter<long>("digitalbrain.client.conversation.failures");
    private static readonly Histogram<double> TurnDuration =
        Meter.CreateHistogram<double>(
            "digitalbrain.client.conversation.duration",
            "ms");

    internal static async Task<ConversationTurnResult> SubmitTurnAsync(
        IConversationNeuron conversation,
        ConversationTurnId turnId,
        ConversationRole role,
        string text)
    {
        var roleName = role.ToString().ToLowerInvariant();
        using var activity = ActivitySource.StartActivity(
            "digitalbrain.conversation.submit",
            ActivityKind.Client);
        activity?.SetTag("digitalbrain.conversation.role", roleName);
        var started = Stopwatch.GetTimestamp();
        try
        {
            var result = await conversation.SubmitTurnAsync(
                new ConversationTurnRequest(turnId, role, text));
            SubmittedTurns.Add(
                1,
                new KeyValuePair<string, object?>(
                    "digitalbrain.conversation.role",
                    roleName));
            TurnDuration.Record(
                Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                new KeyValuePair<string, object?>(
                    "digitalbrain.conversation.role",
                    roleName));
            return result;
        }
        catch
        {
            FailedTurns.Add(
                1,
                new KeyValuePair<string, object?>(
                    "digitalbrain.conversation.role",
                    roleName));
            activity?.SetStatus(ActivityStatusCode.Error);
            throw;
        }
    }
}
