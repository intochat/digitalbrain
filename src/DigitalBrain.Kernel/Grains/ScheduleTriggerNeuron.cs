using DigitalBrain.Core;
using Orleans;
using Orleans.Runtime;
using Orleans.Streams;

namespace DigitalBrain.Kernel;

/// Schedule trigger: projects RegisterReaction (with Schedule) from journals, registers durable per-reaction Orleans reminders.
/// Fires Signal("trigger.schedule.{id}") on tick so AutomationNeuron matches When="Signal:trigger.schedule.{id}".
/// Handles registration on new reactions, unregister on RemoveReaction. Reminders survive restarts.
[Alias("DigitalBrain.Kernel.IScheduleTriggerNeuron")]
public interface IScheduleTriggerNeuron : INeuron
{
}

[GrainType("schedule-trigger.v1")]
public class ScheduleTriggerNeuron(ILogger<ScheduleTriggerNeuron> logger, NeuronJournals journals)
    : Neuron(logger, journals), IScheduleTriggerNeuron, IRemindable
{
    protected override bool ShouldSubscribeToTimeline => true;

    private List<RegisterReaction> _scheduled = [];
    private Dictionary<string, IGrainReminder> _reminders = new(StringComparer.OrdinalIgnoreCase);

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        EnsureScheduled();
        foreach (var r in _scheduled)
        {
            await RegisterReminderForReaction(r, cancellationToken);
        }
    }

    public override async Task OnNextAsync(Synapse item, StreamSequenceToken? token = null)
    {
        await RecordBroadcastReceivedAsync(item);
        EnsureScheduled();
        await DispatchSynapse(item); // let dispatch handle reg/unreg side effects
    }

    protected void EnsureScheduled()
    {
        var removes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rm in OutgoingJournal.Concat(IncomingJournal).OfType<RemoveReaction>())
        {
            removes.Add(rm.Id);
        }

        _scheduled = OutgoingJournal.Concat(IncomingJournal)
            .OfType<RegisterReaction>()
            .Where(r => !string.IsNullOrWhiteSpace(r.Schedule) && !removes.Contains(r.Id))
            .ToList();
    }

    private async Task RegisterReminderForReaction(RegisterReaction r, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_reminders.ContainsKey(r.Id))
        {
            // Due/period fixed for v1; cron parse would compute next due here (e.g. via NCrontab or custom).
            // Schedule field carries the expression (e.g. "0 * * * *") for future use / UI.
            var rem = await this.RegisterOrUpdateReminder(r.Id, TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(1));
            _reminders[r.Id] = rem;
        }
    }

    private async Task UnregisterReminderIfExists(string id)
    {
        if (_reminders.Remove(id, out var rem))
        {
            try { await this.UnregisterReminder(rem); } catch { /* best effort unreg */ }
        }
    }

    public async Task ReceiveReminder(string reminderName, TickStatus status)
    {
        Logger.LogInformation("Schedule reminder {Name} ticked", reminderName);
        // Match convention: AutomationNeuron IsMatch looks for exact "Signal:trigger.schedule.{id}" in When
        await FireAsync(new Signal("trigger.schedule." + reminderName, new Dictionary<string, object?> { ["reactionId"] = reminderName }));
    }

    protected override async Task DispatchSynapse(Synapse synapse, CancellationToken cancellationToken = default)
    {
        await base.DispatchSynapse(synapse, cancellationToken);

        switch (synapse)
        {
            case RegisterReaction rr when !string.IsNullOrWhiteSpace(rr.Schedule):
                _scheduled.Add(rr);
                await RegisterReminderForReaction(rr, cancellationToken);
                break;
            case RemoveReaction rm:
                _scheduled.RemoveAll(r => r.Id == rm.Id);
                await UnregisterReminderIfExists(rm.Id);
                break;
        }
    }
}
