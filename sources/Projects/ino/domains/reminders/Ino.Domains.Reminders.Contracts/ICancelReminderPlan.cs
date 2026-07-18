using Ino.Core.Hosting;

namespace Ino.Domains.Reminders.Contracts;

/// <summary>
/// Cross-silo plan grain interface for the <c>reminders.cancel</c> neuron.
/// Looks up the user's most recent <see cref="ReminderSet"/> matching the
/// prompt's description and invokes <see cref="IRemindersNeuron.CancelAsync"/>.
/// </summary>
public interface ICancelReminderPlan : INeuronPlan
{
}
