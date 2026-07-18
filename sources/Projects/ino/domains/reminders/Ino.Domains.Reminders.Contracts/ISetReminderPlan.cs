using Ino.Core.Hosting;

namespace Ino.Domains.Reminders.Contracts;

/// <summary>
/// Cross-silo plan grain interface for the <c>reminders.set</c> neuron.
/// First IAW→ino bridge neuron: extracts (description, delay) from the
/// user prompt then calls <see cref="IRemindersNeuron.SetAsync"/>, which
/// itself rides on IAW's <see cref="Orleans.DurableJobs"/> runtime.
/// </summary>
public interface ISetReminderPlan : INeuronPlan
{
}
