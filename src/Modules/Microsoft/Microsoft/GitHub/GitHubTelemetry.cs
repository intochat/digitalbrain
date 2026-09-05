using System.Diagnostics;

namespace DigitalBrain.Microsoft.GitHub;

internal static class GitHubTelemetry
{
    internal static readonly ActivitySource Source = new("DigitalBrain.GitHub");

    internal static GitHubWebhookReceipt CaptureContext(GitHubWebhookReceipt receipt)
    {
        var current = Activity.Current;
        return current?.IdFormat == ActivityIdFormat.W3C
            ? SanitizeContext(receipt with { TraceParent = current.Id, TraceState = current.TraceStateString })
            : receipt with { TraceParent = null, TraceState = null };
    }

    internal static GitHubWebhookReceipt SanitizeContext(GitHubWebhookReceipt receipt)
    {
        if (receipt.TraceParent is not { Length: 55 } parent
            || !ActivityContext.TryParse(parent, null, out var context)
            || context.TraceId == default || context.SpanId == default)
        {
            return receipt with { TraceParent = null, TraceState = null };
        }
        // Carry the bounded W3C vendor state, never Activity baggage or arbitrary headers.
        var state = receipt.TraceState;
        if (state is { Length: > 512 } || state?.Any(static character => character < 0x20 || character > 0x7e) == true)
        {
            state = null;
        }
        return receipt with { TraceState = state };
    }

    internal static Activity? StartReceipt(string operation, ActivityKind kind,
        GitHubRepositoryBinding binding, GitHubWebhookReceipt receipt)
    {
        var safe = SanitizeContext(receipt);
        var parent = ActivityContext.TryParse(safe.TraceParent, safe.TraceState, out var parsed) ? parsed : default;
        var activity = Source.StartActivity(operation, kind, parent);
        TagReceipt(activity, binding, receipt.DeliveryId, receipt.PullRequestNumber);
        return activity;
    }

    internal static void TagReceipt(Activity? activity, GitHubRepositoryBinding binding, string deliveryId, int? number)
    {
        activity?.SetTag("github.binding.id", binding.Id)
            .SetTag("github.repository.id", binding.RepositoryId)
            .SetTag("github.delivery.id", deliveryId);
        if (number is { } value)
        {
            activity?.SetTag("github.pull_request.number", value);
        }
    }

    internal static void Failed(Activity? activity, Exception error)
        => activity?.SetTag("error.type", error.GetType().Name).SetStatus(ActivityStatusCode.Error, error.GetType().Name);
}
