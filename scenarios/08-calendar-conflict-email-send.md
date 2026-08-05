# Scenario 08: Calendar conflict resolution with email send

## User intent
Owner tries to book a 60-minute customer call; the slot conflicts with an existing internal meeting. The brain proposes resolutions (shorten, move internal, offer alternate slots), and on choice sends a polite reschedule or invite email automatically.

## Trigger
Shell calendar action or chat: "Book Acme call Thursday 14:00" → `MeetingScheduleAsked` / control activation.

## Imagined modules
- Calendar (free/busy, create, move)
- Contacts (attendee resolution)
- Gmail (invite/reschedule mail)
- Chat (negotiation copy)
- Approvals (if moving someone else's meeting)
- Shell UI (conflict modal)

## Neuron map
| Neuron (kind/name) | Role |
|---|---|
| calendar/owner | Conflict detect; apply chosen resolution |
| contacts/owner | Resolve emails/names |
| gmail/owner-inbox | Send reschedule/invite messages |
| chat/owner-desk | Natural language offers |
| approvals/owner | Gate moves affecting co-owned events |
| shell/primary | Conflict UI |

## Synapse choreography
1. Edge **directs** `MeetingScheduleAsked` (title, start, end, attendees[]) → `calendar/owner`.
2. Calendar checks busy; **broadcasts** `CalendarConflictDetected` (requested, conflictingEventIds[], overlap).
3. Calendar **directs** `ConflictResolutionPlanAsked` (internal planner or assistant neuron) → `ConflictResolutionPlanAnswered` (options: ShiftRequested, ShortenInternal, Hybrid, CancelRequest).
4. Calendar **broadcasts** `ConflictResolutionsProposed` (options[] with previews).
5. Shell shows modal; owner picks "move internal standup −30m and keep Acme" → `OwnerActionActivated` → **directs** `ConflictResolutionChosen` to calendar.
6. If internal event has co-attendees: **directs** `ApprovalBundleAsked` for move; on grant continue.
7. Calendar **directs** `CalendarEventMoveAsked` (internal) → self/apply → `CalendarEventMoved`.
8. Calendar **directs** `CalendarEventCreateAsked` (Acme) → `CalendarEventCreated`.
9. Calendar **directs** `EmailComposeAsked` → gmail/chat for reschedule notice to standup attendees + invite to Acme.
10. Gmail **broadcasts** `EmailSendProposed` then on policy **directs** send → `EmailSent` (two threads possible).
11. Calendar **broadcasts** `MeetingScheduleCompleted`; chat **directs** `AssistantResponded` with links.

## Orleans / Core surface exercised
Serialized turns on calendar neuron; DurableGrain journals for event mutations; reminders for upcoming meeting; transactions only if justified — prefer saga of journaled steps over DTC; outbox durability; request context; grain call filters; pub-sub for conflict ambient facts; no reentrant Deliver into calendar mid-move.

## Rich experience
Conflict modal with timeline overlay (red overlap); option cards with before/after; email preview tabs (internal vs external); undo window 30s → `MeetingScheduleUndoAsked`.

## Failure / adversarial cases
- Double-book race: two creates for same slot — calendar must re-check busy at apply time; second fails with `CalendarConflictDetected` again.
- Email sent but event create fails: compensating `EmailCorrectionSent` or hold send until event committed (outbox order: event then email).
- Moving shared event without approval: hard fail.
- Attendee address wrong: contacts miss → `AttendeeUnresolved`; do not send.
- Timezone mismatch: all facts store UTC + owner tz display field.

## Capability claim
Scheduling is a multi-party synapse saga with conflict facts, approvals, and outbound mail — not a chatbot that invents a free slot without writing the calendar.
