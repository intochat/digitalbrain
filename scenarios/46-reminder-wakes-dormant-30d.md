# Scenario 46: Reminder wakes dormant neuron after 30 days

## User intent
The owner says “Remind me in 30 days to review the Acme contract.” For a month the contract neuron is dormant. On due day it must wake, surface a rich reminder with the original context, and optionally open tasks—without a always-on process.

## Trigger
Chat/natural language schedule → `Schedule(ContractReviewDue, due=30d)` / reminder registration; later, reminder fires.

## Imagined modules
- Assistant (parse intent)
- ContractReview behavior
- Tasks
- Calendar optional
- Ui / push notify
- Memory link to contract doc

## Neuron map
| Neuron (kind/name) | Role |
|---|---|
| Chat / desk | User request |
| Assistant / desk | Creates reminder intent |
| ContractReview / acme | Dormant target; handles due fact |
| Scheduler / core-facing | Materializes Schedule |
| TaskStore / personal | Optional task create |
| Notifier / push | Wake notification |
| UiProjector / shell | Reminder card |

## Synapse choreography
1. UserMessaged → Assistant Emits `ReminderRequested(ContractReview, acme, at)`.
2. ContractReview (or Scheduler) applies Core `Schedule(ContractReviewDue, …)`—schedule table durable with the neuron.
3. Grain deactivates; only reminder/schedule infrastructure remains.
4. After 30 days, Core delivers `ContractReviewDue` → activation → journal + handler.
5. Handler Emits `UiSurface(ReviewCard)`, `NotifyOwner`, optional `TaskCreate`, Ask `FetchContractSnapshot`.
6. Owner completes → `ContractReviewed` Unschedule if periodic, or one-shot done.
7. If delivery fails while owner offline, retry policy / next login projection from journal.

## Orleans / Core surface exercised
Reminders/timers; Schedule/Unschedule/ScheduleFailed Core synapses; grain activation from reminder; DurableGrain journals; outbox for notify; placement after long sleep.

## Rich experience
Push: “Acme contract review”; deep link opens card with original chat quote, doc preview, Snooze 1 week button (`SnoozeReminder` → new Schedule), Mark done.

## Failure / adversarial cases
- Clock jump / DST → store absolute UTC due.
- Duplicate reminder fire → journal `HandledDue(epoch)` idempotency.
- ScheduleFailed consecutive → surface to owner; don’t silent-lose.
- Wrong contract id after 30 days renamed → snapshot blobRef stored at schedule time.

## Capability claim
DigitalBrain makes month-later continuity a durable scheduled fact on a dormant neuron—not a calendar entry disconnected from the brain’s journals and modules.
