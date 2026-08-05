# Scenario 17: Team standup synthesis from many sources

## User intent
Each morning before standup, the owner wants a synthesized brief for their team: yesterday's closed tasks, open blockers from chat, calendar load, notable emails, and SF deal movements — delivered as a structured standup card they can paste or present.

## Trigger
Reminder schedule `StandupBriefDue` at owner-local 09:05 weekdays, or chat "prep standup".

## Imagined modules
- Time/Reminders
- Tasks
- Chat (team channels imagined)
- Calendar
- Gmail
- Salesforce
- StandupProjector
- Shell / Chat

## Neuron map
| Neuron (kind/name) | Role |
|---|---|
| standup/owner-team | Orchestrate brief build |
| time/scheduler | Fire schedule facts |
| tasks/team | Completions & blockers |
| chat/team-room | Recent blocker phrases |
| calendar/owner | Meeting load |
| gmail/owner-inbox | VIP mail |
| salesforce/owner-org | Deal deltas |
| shell/primary | Standup scene |

## Synapse choreography
1. Scheduler **broadcasts** `StandupBriefDue` (teamId, range).
2. Standup neuron **directs** parallel range asks: tasks, chat highlights, calendar, gmail salience, SF deltas.
3. Continuations assemble → **broadcasts** `StandupBriefBuilt` (yesterday[], todayPlan[], blockers[], shoutouts[], metrics).
4. Chat **directs** `AssistantResponded` to owner desk with brief; shell opens standup scene.
5. Owner edits "today plan" → `StandupBriefEdited` → optional **broadcast** `StandupBriefPublished` to team room.
6. During live standup, owner marks blocker resolved → `BlockerResolved` → tasks update.
7. Next day memory can hear prior brief for continuity `StandupFollowUpCarried`.

## Orleans / Core surface exercised
Reminders/timers; DurableGrain journals; multi-ask continue pattern; request context team/owner scope; pub-sub; placement; grain call filters; outbox; stateless workers optional for NLP highlight extraction.

## Rich experience
Three-column standup card (yesterday / today / blockers); SF sparkline; "copy to clipboard" action; team publish toggle; speaking timer widget optional.

## Failure / adversarial cases
- Missing module: brief shows explicit empty section with `sourceFailed`.
- Over-fetch: hard caps per source; summarize with refs not full bodies.
- Publishing to wrong team room: teamId from brief, not free text.
- Timezone: schedule in owner tz; range stored UTC.
- PII in email salience: redaction policy before publish.

## Capability claim
Standup prep is a scheduled multi-module journal projection — not a morning person typing into a generic chatbot with no access to tasks or CRM deltas.
