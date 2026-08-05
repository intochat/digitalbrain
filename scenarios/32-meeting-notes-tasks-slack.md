# Scenario 32: Meeting notes → tasks → Slack/Teams post

## User intent
After a customer meeting, the owner drops notes (or a transcript) and wants structured tasks in their task system, CRM activity logged, and a short summary posted to the right Slack/Teams channel—with human confirm on external posts.

## Trigger
Chat paste / file upload `MeetingNotesSubmitted`, or calendar `MeetingEnded` with attached notes.

## Imagined modules
- MeetingNotes / NLP extract
- Tasks module
- CRM (Salesforce activity)
- Slack/Teams adapter
- Chat confirm UI
- Memory (meeting memory)

## Neuron map
| Neuron (kind/name) | Role |
|---|---|
| Chat / post-meeting | User entry + confirmations |
| NotesExtractor / default | Parses notes → TaskCandidates, CrmActivityDraft |
| TaskStore / personal | Creates tasks on confirm |
| CrmActivity / default | Logs activity |
| CollabPost / slack | Posts only after ConfirmPost |
| UiProjector / shell | Checklist + channel picker |

## Synapse choreography
1. `MeetingNotesSubmitted` (broadcast in chat context).
2. NotesExtractor hears → Emits `TasksProposed`, `CrmActivityProposed`, `SummaryDrafted`, `UiSurface(ConfirmPanel)`.
3. Owner taps Create tasks → `ConfirmTasks` directed/broadcast; TaskStore Emits `TasksCreated`.
4. Owner selects channel + Confirm → `ConfirmCollabPost`; CollabPost Asks `PostMessage`; on success `MessagePosted`.
5. CRM path may run in parallel after `ConfirmCrm` or auto if policy allows; `CrmActivityLogged`.
6. Memory hears `MeetingSummarized` for later “what did we promise Acme?”.
7. Chat gets directed `AssistantResponded` linking task ids and permalink.

## Orleans / Core surface exercised
Announce/listen chain; Ask/Answer for external posts; DurableGrain journals; outbox for external side effects; Connect topology between notes and task kinds; request context for meeting id.

## Rich experience
Multi-pane: notes left, proposed tasks center with checkboxes, Slack preview right; tables of owners/due dates; action buttons Confirm all / Edit / Skip CRM.

## Failure / adversarial cases
- Partial confirm: tasks created, Slack fails → DeliveryFailed triggers retry without duplicating tasks (idempotent task keys).
- Auto-post without confirm if behavior misauthored → policy module should require ConfirmCollabPost for egress.
- Nested asks that re-enter extractor → continue pattern only.
- Cross-channel wrong team → OAuth scopes and explicit channel id in journal.

## Capability claim
DigitalBrain turns a meeting dump into a multi-system choreography with confirm gates and a single causal journal—not a copy-paste checklist for the human.
