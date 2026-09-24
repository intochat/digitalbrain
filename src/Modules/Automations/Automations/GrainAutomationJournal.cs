using DigitalBrain.Inbox;
using DigitalBrain.Receipts;

namespace DigitalBrain.Automations;

// One durable receipt per run, keyed by the run's intent id, plus an Inbox item so work that
// happened while the user was away reaches them. Failures surface the first exception verbatim.
internal sealed class GrainAutomationJournal(IGrainFactory grains) : IAutomationJournal
{
    public async Task RecordRunAsync(AutomationDefinition definition, JobRun run, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(run);
        var succeeded = run.Outcome == JobOutcome.Succeeded;
        var receipt = grains.GetGrain<IReceipt>(run.IntentId);
        await receipt.Write(new ReceiptDraft
        {
            WorkspaceId = definition.WorkspaceId,
            ConversationId = definition.Id,
            Outcome = succeeded ? ReceiptOutcome.Succeeded : ReceiptOutcome.Failed,
            Summary = $"{definition.Name} ({definition.Action.AppId}.{definition.Action.Operation})",
            FailureExplanation = run.FirstError,
            FirstTry = run.Outcome != JobOutcome.SkippedBudget,
        }).ConfigureAwait(true);

        var inbox = grains.GetGrain<IInboxFeed>(definition.WorkspaceId);
        await inbox.Post(new InboxItemDraft
        {
            Kind = succeeded ? InboxItemKind.AutomationResult : InboxItemKind.AutomationFailed,
            Title = succeeded ? $"{definition.Name} ran" : $"{definition.Name} failed",
            GroupingKey = definition.Id,
            Detail = run.FirstError,
            IntentId = run.IntentId,
            AppId = definition.Action.AppId,
        }).ConfigureAwait(true);
    }
}
