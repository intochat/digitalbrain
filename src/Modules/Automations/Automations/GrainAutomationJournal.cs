using DigitalBrain.Inbox;

namespace DigitalBrain.Automations;

// The automation grain stores run history; the Inbox tells the owner about work done while away.
internal sealed class GrainAutomationJournal(IGrainFactory grains) : IAutomationJournal
{
    public async Task RecordRunAsync(AutomationDefinition definition, JobRun run, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(run);
        var succeeded = run.Outcome == JobOutcome.Succeeded;
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
