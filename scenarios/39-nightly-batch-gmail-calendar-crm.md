# Scenario 39: Nightly batch job across Gmail+Calendar+CRM

## User intent
Every night at 02:00 local, the brain reconciles: unanswered important email, tomorrow’s calendar gaps, CRM opportunities with no next step—and produces a morning pack without the owner being online.

## Trigger
`Schedule` / reminder / Pulse clock fact `NightlyReconcileDue` (cron-like module).

## Imagined modules
- Time / Scheduler
- Gmail
- Calendar
- CRM
- NightlyReconcile behavior
- MorningBrief store
- Notify (optional push at 07:00)

## Neuron map
| Neuron (kind/name) | Role |
|---|---|
| NightlyScheduler / owner | Emits NightlyReconcileDue on schedule |
| NightlyReconcile / batch | Fan-out asks; writes pack |
| GmailQuery / inbox | Answers FindUnansweredImportant |
| Calendar / personal | Answers GetDayAgenda, FindGaps |
| CrmOps / default | Answers OppsMissingNextStep |
| MorningBrief / tomorrow | Stores BriefReady |
| Notifier / morning | Optional 07:00 push |

## Synapse choreography
1. Schedule table fires → neuron receives `NightlyReconcileDue` (time as fact).
2. NightlyReconcile Asks Gmail, Calendar, CRM in parallel; join on journal.
3. Emits `ReconciliationPartial` as sections complete (optional).
4. Final `MorningBriefReady` broadcast; MorningBrief journals pack.
5. `Schedule(NotifyOwner, due 07:00)` or separate reminder grain.
6. Morning: owner opens shell → UI projects brief; or push `NotifyOwner`.
7. Failures per system produce `SourceDegraded` sections rather than failing whole pack.

## Orleans / Core surface exercised
Reminders/timers; Schedule/Unschedule Core synapses; DurableGrain journals; fan-out join; outbox; placement for batch; stateless workers if heavy CRM pages; AskExpired per source.

## Rich experience
Morning brief pane: email table, calendar timeline with gap highlights, CRM list with “propose next step” buttons prepared offline; badge counts.

## Failure / adversarial cases
- Job overlaps next night if still running → mutex via journal `RunInProgress` and skip/queue.
- Reminder storm after downtime → idempotent day key.
- Partial OAuth failure → degrade section, not silent empty success.
- Cluster failover mid-job → restart continues from journal, no duplicate CRM writes.

## Capability claim
DigitalBrain runs multi-system nightly operations as scheduled durable choreography with per-source degradation—not a fragile cron script outside the agent’s memory.
