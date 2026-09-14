using System.Reflection;
using DigitalBrain.Abstractions.Slots;
using Orleans.Concurrency;

namespace DigitalBrain.Core;

internal sealed class NeuronActivationGuardFilter(IActiveSlotLease lease) : IIncomingGrainCallFilter
{
    public async Task Invoke(IIncomingGrainCallContext context)
    {
        if (context.Grain is Neuron neuron)
        {
            // Drain and ReceiveReminder apply the slot fence themselves (Neuron.IsStandby); every other
            // call to a neuron passes through the slot fence below, then the separate persistence guard.
            var declaringInterface = context.InterfaceMethod.DeclaringType;
            if (declaringInterface != typeof(INeuronInbox)
                && declaringInterface != typeof(IRemindable))
            {
                // The slot fence: a standby silo shares this neuron's storage and reminders with the live
                // one, so it may answer reads (the promotion smoke-checks it through them) and change
                // nothing. Throwing here, before the method body, keeps the refusal out of the command
                // journal.
                if (!lease.HoldsLease && !context.InterfaceMethod.IsDefined(typeof(ReadOnlyAttribute)))
                {
                    throw new StandbySlotException(lease.Slot);
                }

                // The persistence guard: waits for reload or in-place recovery to finish before the call proceeds.
                await neuron.GuardActivationAsync().ConfigureAwait(true);
            }
        }

        await context.Invoke().ConfigureAwait(true);
    }
}
