using System.Threading.Channels;
using Orleans;
using Orleans.Runtime;
using Orleans.Timers;

namespace DigitalBrain.Tests;

internal sealed record ReminderDelivery(GrainId Id, object Activation);

internal sealed class ReminderControl : IIncomingGrainCallFilter
{
    public IReminderTable Table { get; set; } = null!;
    public bool FailRegister { get; set; }
    public bool FailUnregister { get; set; }
    public bool FailRead { get; set; }
    public bool FailAfterRegister { get; set; }
    public bool FailAfterUnregister { get; set; }
    public Channel<ReminderDelivery> Delivered { get; } = Channel.CreateUnbounded<ReminderDelivery>();

    public async Task Invoke(IIncomingGrainCallContext context)
    {
        await context.Invoke();
        if (context.InterfaceMethod.DeclaringType == typeof(IRemindable))
        { Delivered.Writer.TryWrite(new(context.TargetId, context.TargetContext)); }
    }

    public async Task<ReminderDelivery> NextDeliveryAsync(CancellationToken ct)
        => await Delivered.Reader.ReadAsync(ct).AsTask().WaitAsync(TimeSpan.FromSeconds(15), ct);
}

internal sealed class FaultingReminders(IReminderRegistry inner, ReminderControl faults) : IReminderRegistry
{
    public async Task<IGrainReminder> RegisterOrUpdateReminder(GrainId id, string name, TimeSpan due, TimeSpan period)
    {
        if (faults.FailRegister) { faults.FailRegister = false; throw new IOException("Refused reminder registration."); }
        var registered = await inner.RegisterOrUpdateReminder(id, name, due, period);
        if (faults.FailAfterRegister) { faults.FailAfterRegister = false; throw new IOException("Lost registration acknowledgement."); }
        return registered;
    }
    public async Task UnregisterReminder(GrainId id, IGrainReminder reminder)
    {
        if (faults.FailUnregister) { faults.FailUnregister = false; throw new IOException("Refused reminder removal."); }
        await inner.UnregisterReminder(id, reminder);
        if (faults.FailAfterUnregister) { faults.FailAfterUnregister = false; throw new IOException("Lost removal acknowledgement."); }
    }
    public Task<IGrainReminder?> GetReminder(GrainId id, string name)
    {
        if (faults.FailRead) { faults.FailRead = false; throw new IOException("Reminder lookup failed."); }
        return inner.GetReminder(id, name);
    }
    public Task<List<IGrainReminder>> GetReminders(GrainId id) => inner.GetReminders(id);
}