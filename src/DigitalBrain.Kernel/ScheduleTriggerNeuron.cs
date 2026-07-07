using DigitalBrain.Core;
using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Kernel;

/// Minimal schedule trigger using Orleans reminders (durable with our storage).
/// In full impl, project scheduled reactions from journals and register per-reaction reminders with cron.
[GrainType("schedule-trigger.v1")]
public class ScheduleTriggerNeuron(ILogger<ScheduleTriggerNeuron> logger, NeuronJournals journals)
    : Neuron(logger, journals), IRemindable
{
    private List<RegisterReaction> _scheduled = new();

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        EnsureScheduled();
        foreach (var r in _scheduled.Where(r => !string.IsNullOrWhiteSpace(r.Schedule)))
        {
            // Register reminder; real would parse cron for due/period.
            await this.RegisterOrUpdateReminder(r.Id, TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(1));
        }
    }

    protected void EnsureScheduled()
    {
        _scheduled = OutgoingJournal.Concat(IncomingJournal).OfType<RegisterReaction>()
            .Where(r => !string.IsNullOrWhiteSpace(r.Schedule)).ToList();
    }

    public async Task ReceiveReminder(string reminderName, TickStatus status)
    {
        Logger.LogInformation("Schedule reminder {Name} ticked", reminderName);
        await FireAsync(new Signal("trigger.schedule." + reminderName, new Dictionary<string, object?>()));
    }

    protected override async Task DispatchSynapse(Synapse synapse)
    {
        await base.DispatchSynapse(synapse);
        if (synapse is RegisterReaction rr && !string.IsNullOrWhiteSpace(rr.Schedule))
        {
            _scheduled.Add(rr);
            await this.RegisterOrUpdateReminder(rr.Id, TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(1));
        }
    }
}