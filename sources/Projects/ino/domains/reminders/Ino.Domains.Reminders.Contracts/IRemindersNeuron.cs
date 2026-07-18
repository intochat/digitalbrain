using Ino.Core.Hosting;
using Orleans;

namespace Ino.Domains.Reminders.Contracts;

/// <summary>
/// Cross-silo grain interface for the per-user reminder journal + scheduler.
/// Keyed by user id — each user has their own scheduled-jobs dictionary
/// (inherited from IAW <c>Agent.Scheduling</c>) and reminder event log.
///
/// Writes go through the explicit <see cref="SetAsync"/> / <see cref="CancelAsync"/>
/// methods rather than <c>IFirePort</c> — IAW's scheduling primitives are
/// stateful per-user, while <c>IFirePort</c> routes by correlation. Reads
/// flow through <see cref="IJournaledNeuronQuery{TEvent}"/> for cross-silo
/// plans that want to inspect a user's reminder history (e.g.
/// "did I already set a reminder for this?").
/// </summary>
public interface IRemindersNeuron : IGrainWithStringKey, IJournaledNeuronQuery<ReminderEvent>
{
    /// <summary>
    /// Schedules a one-shot reminder. Persists an <see cref="Orleans.DurableJobs"/>
    /// job (via IAW <c>Agent.ScheduleJob</c> inherited through
    /// <c>LlmNeuron</c>) and journals a <see cref="ReminderSet"/>. The
    /// returned name is the unique key the caller can later pass to
    /// <see cref="CancelAsync"/>.
    /// </summary>
    Task<string> SetAsync(string description, TimeSpan delay, string correlationId);

    /// <summary>
    /// Cancels a previously-set reminder by name. Removes the IAW durable job
    /// and journals a <see cref="ReminderCancelled"/>. No-op (returns false)
    /// if the name isn't currently scheduled.
    /// </summary>
    Task<bool> CancelAsync(string name, string correlationId);
}
