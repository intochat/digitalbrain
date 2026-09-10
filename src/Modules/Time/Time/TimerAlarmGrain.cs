using System.Globalization;
using System.Text.Json;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using Orleans.Runtime;

namespace DigitalBrain.Time;

// Neuron owns IRemindable and forbids subclass timers or reminders, so alarms need a separate grain.
[GrainType("timer-alarm")]
internal sealed class TimerAlarmGrain(TimeOptions options, TimeProvider timeProvider) : Grain, ITimerAlarm, IRemindable
{
    private const string AlarmReminderName = "due";

    public Task Arm(TimeSpan dueIn)
        => this.RegisterOrUpdateReminder(AlarmReminderName, dueIn < TimeSpan.Zero ? TimeSpan.Zero : dueIn, options.AlarmPeriod);

    public async Task Retire()
    {
        var reminder = await this.GetReminder(AlarmReminderName).ConfigureAwait(true);
        if (reminder is not null)
        {
            await this.UnregisterReminder(reminder).ConfigureAwait(true);
        }
    }

    public Task ReceiveReminder(string reminderName, TickStatus status)
    {
        if (reminderName != AlarmReminderName)
        {
            return Task.CompletedTask;
        }

        var key = this.GetPrimaryKeyString();
        var separator = key.LastIndexOf('/');
        if (separator <= 0 || !long.TryParse(key.AsSpan(separator + 1), NumberStyles.None,
                CultureInfo.InvariantCulture, out var generation) || generation <= 0)
        {
            throw new InvalidOperationException($"Malformed timer alarm key '{key}'.");
        }

        NeuronId timer;
        try
        {
            timer = new NeuronId("timer", key[..separator]);
        }
        catch (ArgumentException error)
        {
            throw new InvalidOperationException($"Malformed timer alarm key '{key}'.", error);
        }

        var signal = Signal.Create(TimeSignals.Due,
            JsonSerializer.Serialize(new TimerGeneration(generation), TimeJson.Default.TimerGeneration));
        var delivery = SignalDelivery.Create(signal, NeuronId.FromGrainId(this.GetGrainId()), 1, timeProvider);
        return GrainFactory.GetGrain<INeuron>(timer.ToGrainId()).Deliver(delivery);
    }
}
