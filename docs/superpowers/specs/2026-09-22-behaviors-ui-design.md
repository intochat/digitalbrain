# Behaviors application

Approved direction: a workspace-scoped behavior manager in the existing desktop, using the existing assistant. The user can understand purpose, verify readiness, change a draft, validate and deploy it, stop/start, inspect failures, and roll back.

## Experience

Replace the disabled Automations / Dev Studio launcher with Behaviors (Create and manage automations). Open one persistent application window per workspace. Use a searchable master list and detail pane; below 700 logical pixels, selecting a row opens its detail with a Back button. Filters: All, Running, Needs attention, Drafts. Never display cached readiness as a live success during disconnect.

Detail header: name, purpose, state and contextual Start/Stop/Deploy changes actions. Tabs: Overview, Activity, Code & versions. Keep Edit with assistant accessible. Overview shows declared trigger → behavior → effect cards, explicitly described as authored documentation, not a verified execution graph. Missing declarations say so. Configuration is editable JSON, persisted only on deployment. Changes do not alter the currently running version.

New behavior collects name and intent, persists a catalog entry, opens a new conversation with a reviewable prompt to generate and validate that exact logical ID, and keeps the manager open. It does not silently send the message. Repair and edit likewise open a focused conversation with explicit behavior context; the agent reads authoritative draft/check/worker state. Chat tool results have an Open behavior action.

Runtime and draft states are separate: successful compilation/tests never mean Running. Running is shown only with Ready=true. Starting/Stopping remain pending until a refresh confirms completion. Connection failure shows a stale-state warning and disables mutations. Poll visible views without overlapping reads, cancel timers on disposal, and ignore responses for superseded selections.

## Persistence and interfaces

Add an application-owned catalog keyed by authenticated workspace scope and logical behavior ID. Store display name, purpose, declared triggers/effects, metadata revision and creation time. Both chat and HTTP draft saves register entries through the same scoped tool service. Catalog metadata never owns execution state. Existing programs with opaque hashed IDs cannot be attributed to a workspace safely; users can register a known logical ID to reconnect them.

Add list, describe and detail endpoints under the existing scoped behavior routes. Detail joins catalog metadata, current draft, latest check, program state, bounded logs, and retained checked source snapshots. Capture a source snapshot for an artifact only when the corresponding draft revision and source hash match. UI deployment requires a passing check for the current draft; backend artifact validation remains authoritative. All mutations preserve expected revision and operation identity semantics; conflicts refresh state without automatic retries.

Source and tests are readable; edits use the assistant. A simple line comparison against a retained deployed source explains draft changes. Deployment history displays actual revisions and supports explicit rollback. No draggable graph editor, second chat panel, generic event simulation, fabricated event history, or full IDE.

## Acceptance

Chat-created drafts appear in their own workspace only and survive restart. Metadata edits detect conflicts. List shows true runtime/draft state. Create/edit/repair flows use the existing assistant. Failed checks cannot enable Deploy; deploying does not label a worker Ready prematurely. Stop/start and rollback use current revisions. New drafts do not replace running code. Narrow layout remains usable. Reconnect refreshes state. Unit/widget tests cover isolation, conflict, stale responses, action gating and scope. Native UI and separate-process E2E verify lifecycle behavior.
