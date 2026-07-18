using Core.Contracts;
using IAW.Core;
using Ino.Core;
using Ino.Core.Hosting;
using Ino.Domains.Reminders.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans;
using Orleans.Journaling;

namespace Ino.Domains.Reminders.Neurons;

/// <summary>
/// Per-user reminder neuron. Inherits IAW's <see cref="Agent"/> via
/// <see cref="LlmNeuron{TEvent}"/>, so <c>ScheduleJob</c> / <c>CancelJob</c> /
/// <c>ListJobs</c> / <c>OnScheduledJobDueAsync</c> come for free — ino just
/// adds the neuron-shaped surface (<see cref="SetAsync"/> /
/// <see cref="CancelAsync"/>) and the journal of <see cref="ReminderEvent"/>.
///
/// Naturally placed by Orleans on the Reminders silo — only that silo's
/// assembly registers this grain class. Per-user keying via the grain
/// primary key; <c>OnScheduledJobDueAsync</c> reconstructs a
/// <see cref="NeuronContext"/> from the key when IAW's <see cref="Orleans.DurableJobs"/>
/// runtime delivers a tick.
/// </summary>
public sealed class RemindersNeuron(
    [AgentState] AgentDurableState durableState,
    IChatClient chatClient,
    [FromKeyedServices("journal")] IDurableList<EventEnvelope<ReminderEvent>> journal,
    IFirePort firePort,
    ILogger<RemindersNeuron>? log = null)
    : LlmNeuron<ReminderEvent>(durableState, chatClient, journal), IRemindersNeuron
{
    private readonly IFirePort _firePort = firePort;
    private readonly ILogger _log = (ILogger?)log ?? NullLogger.Instance;

    protected override string Instructions =>
        "You schedule personal reminders. Surface them when due and don't lose them across restarts.";

    protected override string DisplayName => "Reminders";

    public async Task<string> SetAsync(string description, TimeSpan delay, string correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        if (delay <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(delay), "Reminder delay must be positive.");

        // Short-but-unique name keyed off Ulid so Cancel can target it later.
        var name = Ulid.NewUlid().ToString();
        var dueAt = DateTimeOffset.UtcNow + delay;

        // ScheduleJob (inherited from IAW Agent) persists an Orleans DurableJob
        // and stores a ScheduledJobItem in the agent's durable dictionary. The
        // prompt becomes the description we'll narrate when the job fires.
        await ScheduleJob(name, delay, description, AgentCancellation);

        await RaiseAsync(new ReminderSet(name, description, dueAt), BuildSelfContext(correlationId));
        return name;
    }

    public async Task<bool> CancelAsync(string name, string correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        if (!ScheduledJobs.ContainsKey(name)) return false;

        await CancelJob(name, AgentCancellation);
        await RaiseAsync(new ReminderCancelled(name), BuildSelfContext(correlationId));
        return true;
    }

    /// <summary>
    /// IAW's <c>Agent.ExecuteJobAsync</c> calls this when a scheduled job comes
    /// due. We override the default LLM-driven behaviour: instead of asking the
    /// model to react to the prompt, we journal a <see cref="ReminderDue"/>
    /// and broadcast a <see cref="ReminderNarration"/> so the gateway streams
    /// the reminder text back to the user.
    /// </summary>
    protected override async Task OnScheduledJobDueAsync(ScheduledJobItem job, CancellationToken ct)
    {
        var userId = this.GetPrimaryKeyString();
        var ctx = BuildSelfContext(correlationId: Guid.NewGuid().ToString("n"));

        await RaiseAsync(new ReminderDue(job.Name, job.Prompt, DateTimeOffset.UtcNow), ctx, ct);

        try
        {
            await _firePort.FireBroadcast(
                new ReminderNarration(job.Prompt, userId), ctx, ct);
        }
        catch (Exception ex)
        {
            // Narration is best-effort — the journal entry is the source of
            // truth, the broadcast is delivery. Swallow + log so a transient
            // gateway issue doesn't deactivate the neuron and lose the
            // recurring-reschedule path inside Agent.ExecuteJobAsync.
            _log.LogWarning(ex,
                "RemindersNeuron: narration broadcast failed for user {User}, job {Job}",
                userId, job.Name);
        }
    }

    NeuronContext BuildSelfContext(string correlationId) =>
        new(
            SynapseId: SynapseId.New(),
            CorrelationId: new CorrelationId(correlationId),
            Source: new Caller.FromDomain(DomainId.From("Ino.Domains.Reminders")),
            SourceStream: new StreamKey($"reminders:{this.GetPrimaryKeyString()}"),
            UserId: this.GetPrimaryKeyString())
        {
            FirePort = _firePort,
            Logger = _log,
        };
}
