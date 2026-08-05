# Scenario 13: Multi-device session handoff (phone → desktop)

## User intent
Owner starts a renewal prep on a phone train ride (research + draft), then sits at a desktop and continues without re-explaining — drafts, pending approvals, and open asks transfer with full journal continuity.

## Trigger
Phone chat activity, then desktop shell login/open same work thread; explicit "Continue on desktop" optional control.

## Imagined modules
- Shell (mobile + desktop)
- Chat
- Approvals
- Session/Presence
- Salesforce/Gmail (in-flight work)
- Sync/Edge sessions

## Neuron map
| Neuron (kind/name) | Role |
|---|---|
| shell/mobile | Phone UI session |
| shell/desktop | Desktop UI session |
| presence/owner | Device presence facts |
| chat/northwind-renewal | Durable thread state via journal |
| approvals/owner | Pending bundles visible everywhere |
| uiedge/phone-session | Push channel phone |
| uiedge/desktop-session | Push channel desktop |

## Synapse choreography
1. Phone: `UserMessaged` / drafts → chat journals; **broadcasts** `EmailDraftProposed` still pending.
2. Phone **broadcasts** `DevicePresenceChanged` (device=phone, status=active).
3. Owner opens desktop → **broadcasts** `DevicePresenceChanged` (desktop=active); presence may **broadcast** `SessionHandoffSuggested`.
4. Desktop edge **directs** `ThreadStateAsked` → chat → `ThreadStateAnswered` (last cards, open asks, draft refs) built from journal not from phone RAM.
5. Desktop **broadcasts** `UiSessionSubscribed` (threadKey); phone may **broadcast** `UiSessionParked`.
6. Pending `ApprovalRequired` still open: desktop shows same bundleId; decision **directs** `ApprovalDecisionRecorded` once.
7. Continuation of deferred assistant answers delivers to chat neuron; both edges hear `AssistantResponded` / card updates via owner streams.
8. Optional: phone shows "Continued on desktop" toast from `SessionHandoffCompleted` broadcast.

## Orleans / Core surface exercised
DurableGrain journals as handoff source of truth; streams/observers per UI session; request context; placement of chat grain; outbox; reminders for parked drafts; grain call filters; serialized turns so dual devices cannot double-commit conflicting edits without ordering.

## Rich experience
Desktop opens mid-thread with draft editor focused; approval tray synced; phone switches to companion mode (notifications only). Conflict banner if both type same draft — OT or last-writer with `DraftRevisionConflicted`.

## Failure / adversarial cases
- Split brain dual approve: approvals neuron serializes decisions by bundleId.
- Phone offline sends late draft: revision vector / sequence; reject stale with `DraftStale`.
- Wrong account device: device tokens bound to owner; no handoff across owners.
- Losing in-flight model call: assistant completion still journals; both UIs catch `AssistantResponded` on reconnect snapshot.
- Presence flapping: debounce `DevicePresenceChanged`.

## Capability claim
Work is owned by durable neurons and journals, so devices are interchangeable views — handoff is subscription change, not export/import of a chatbot transcript file.
