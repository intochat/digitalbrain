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
    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        // Example daily reminder; real version would read from journals the RegisterReaction with schedule.
        await this.RegisterOrUpdateReminder("example-schedule", TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(1));
    }

    public async Task ReceiveReminder(string reminderName, TickStatus status)
    {
        Logger.LogInformation("Schedule reminder {Name} ticked", reminderName);
        // Fire a trigger signal that reactions can match on (e.g. "Signal:trigger.schedule.example")
        await FireAsync(new Signal("trigger.schedule." + reminderName, new Dictionary<string, object?>()));
    }
}