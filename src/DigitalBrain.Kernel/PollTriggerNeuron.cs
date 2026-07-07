using DigitalBrain.Core;
using Orleans;

namespace DigitalBrain.Kernel;

/// Minimal poll trigger (reminder-driven). Real version would use approved capability (e.g. HTTP fetch via broker),
/// diff with persisted cursor, emit per new item.
[GrainType("poll-trigger.v1")]
public class PollTriggerNeuron(ILogger<PollTriggerNeuron> logger, NeuronJournals journals)
    : Neuron(logger, journals), IRemindable
{
    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await this.RegisterOrUpdateReminder("poll-example", TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(5));
    }

    public async Task ReceiveReminder(string reminderName, TickStatus status)
    {
        Logger.LogInformation("Poll reminder {Name} ticked", reminderName);
        // In real: fetch via broker, emit new items as signals.
        await FireAsync(new Signal("trigger.poll." + reminderName, new Dictionary<string, object?> { ["item"] = "example" }));
    }
}