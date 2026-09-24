using DigitalBrain.Inbox;
using Microsoft.Extensions.Logging;
using Orleans;

namespace DigitalBrain.Compute.Allowances;

// Posts limit alerts to the workspace Inbox as ComputeLimitAlert items. Best effort: a notification
// failure must never fail the charging decision that raised it, so the post is guarded and logged.
internal sealed class InboxComputeAlertSink(IGrainFactory grains, ILogger<InboxComputeAlertSink> logger) : IComputeAlertSink
{
    public async Task RaiseAsync(ComputeLimitAlert alert, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(alert);
        try
        {
            await grains.GetGrain<IInboxFeed>(alert.WorkspaceId).Post(ToInboxItem(alert)).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // A best-effort alert must not break the charge it reports on.
        catch (Exception error)
#pragma warning restore CA1031
        {
            logger.LogWarning(error, "Could not post a Compute limit alert to the Inbox for workspace {Workspace}.", alert.WorkspaceId);
        }
    }

    internal static InboxItemDraft ToInboxItem(ComputeLimitAlert alert) => new()
    {
        Kind = InboxItemKind.ComputeLimitAlert,
        Title = alert.Level switch
        {
            LimitAlert.At100 => "Compute limit reached",
            LimitAlert.At90 => "Compute at 90% of limit",
            _ => "Compute at 75% of limit",
        },
        GroupingKey = $"compute-limit:{alert.Level}",
        Detail = $"{alert.SpentCompute} of {alert.LimitCompute} Compute used.",
    };
}
