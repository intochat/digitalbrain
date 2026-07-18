using Ino.Core;
using Ino.Core.Hosting;
using Ino.Domains.Reminders.Contracts;

namespace Ino.Domains.Reminders;

/// <summary>
/// The Reminders domain. First IAW→ino capability bridge: every routable
/// neuron here lands on <c>RemindersNeuron : LlmNeuron&lt;ReminderEvent&gt;</c>,
/// which inherits IAW's <c>Agent.Scheduling</c> primitives (ScheduleJob,
/// CancelJob, ListJobs, OnScheduledJobDueAsync) — ino's neuron surface,
/// IAW's durable scheduling underneath.
///
/// Two user-verb neurons in v0.1: <c>reminders.set</c> and
/// <c>reminders.cancel</c>. Both dispatch via <see cref="INeuronPlan"/>
/// (Phase 4 Slice A made plans the only routing path Cortex knows).
/// </summary>
public sealed class Reminders : IDomain
{
    public DomainId Id => DomainId.From("Ino.Domains.Reminders");
    public string Version => "0.1.0";

    public IReadOnlyList<Capability> DeclaredCapabilities =>
    [
        new Capability.Llm(LlmTier.Fast),
    ];

    public IReadOnlyList<INeuronDefinition> DeclaredNeurons =>
    [
        new NeuronDefinition(
            NeuronId.From("reminders.set"),
            DisplayName: "Set a reminder",
            Description: "Schedule a one-shot reminder for a description and a delay (e.g. 'remind me to call mom in 30 minutes').",
            CanonicalSynapseType: typeof(ReminderSet),
            PromptExamples: [
                "remind me to call mom in 30 minutes",
                "set a reminder to take out the trash in 2 hours",
                "remind me to drink water in 45 min"
            ])
        {
            PlanType = typeof(ISetReminderPlan),
        },
        new NeuronDefinition(
            NeuronId.From("reminders.cancel"),
            DisplayName: "Cancel a reminder",
            Description: "Cancel a previously-scheduled reminder by description.",
            CanonicalSynapseType: typeof(ReminderCancelled),
            PromptExamples: [
                "cancel my reminder to call mom",
                "never mind on the trash reminder",
                "forget the water reminder"
            ])
        {
            PlanType = typeof(ICancelReminderPlan),
        },
    ];
}
