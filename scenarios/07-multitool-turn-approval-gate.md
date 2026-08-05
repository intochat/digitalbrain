# Scenario 07: Multi-tool assistant turn with approval gate

## User intent
Owner asks the assistant to "prep the Acme renewal: pull the account, draft a discount email, and schedule a call next Tuesday" — multiple tools may run, but any external side effect (send email, create event) requires an explicit approval gate before commit.

## Trigger
Chat `UserMessaged` with multi-intent prep request.

## Imagined modules
- Assistant / Chat (planner, tool loop)
- Salesforce (account + opportunity read)
- Gmail (draft/send)
- Calendar (propose slots / create event)
- Approvals (gate neuron)
- Shell UI (approval tray)

## Neuron map
| Neuron (kind/name) | Role |
|---|---|
| chat/owner-desk | User turn surface |
| assistant/owner | Plan tools; emit proposals; never side-effect without grant |
| salesforce/owner-org | Read account/opp |
| gmail/owner-inbox | Draft/send on approved commands |
| calendar/owner | Create events on approved commands |
| approvals/owner | Hold pending grants; answer decisions |
| shell/primary | Approval cards tray |

## Synapse choreography
1. `UserMessaged` → chat/assistant journals turn; **broadcasts** `CapabilityRequested` (goal).
2. Assistant **directs** `AccountLookupAsked` → Salesforce → `AccountLookupAnswered` (read path, no gate).
3. Assistant **broadcasts** `EmailDraftProposed` (to, subject, body, accountRef) — ambient for UI.
4. Assistant **broadcasts** `CalendarEventProposed` (attendees, window, title).
5. Assistant **directs** `ApprovalBundleAsked` → `approvals/owner` (items=[emailSend, eventCreate], expiresAt).
6. Approvals **broadcasts** `ApprovalRequired` (bundleId, items[], risk).
7. Shell/chat renders dual-action card; owner approves email only → **directs** `ApprovalDecisionRecorded` (bundleId, granted=[emailSend], denied=[eventCreate]).
8. Approvals answers original ask with `ApprovalBundleAnswered`.
9. Assistant continuation: **directs** `EmailSendAsked` (only if granted) → Gmail → `EmailSent`; for denied event **broadcasts** `CalendarEventDiscarded`.
10. Assistant **broadcasts** `CapabilityCompleted`; **directs** `AssistantResponded` summarizing done vs skipped.
11. Throughout: tool selection facts `CapabilityToolSelected` are journaled for audit.

## Orleans / Core surface exercised
Serialized grain turns (assistant occupied for multi-tool chain); DurableGrain journals; multi-turn ask deferral (approval open until decision); outbox durability; request context correlation id shared across tools; grain call filters; reminders for approval expiry; module catalog; reentrancy rules — approval UI must not re-enter assistant mid-turn unsafely (decision arrives as new delivery).

## Rich experience
Stepper UI: Research ✓ → Draft email (pending) → Meeting (pending). Diff view of email. Calendar mini-week. Approve all / approve selected / edit draft. Expiry countdown.

## Failure / adversarial cases
- Double approval click: watermark/idempotency on bundleId; second grant is no-op.
- Tool runs before grant: Core/module policy — send paths require `ApprovalRef` in ask or refuse with `ApprovalMissing`.
- Expiry race: timer fires `ApprovalExpired` while user taps approve → one terminal winner journaled; loser surfaces conflict.
- Partial grant: must not send meeting invite email that assumes event exists.
- Deadlock: assistant must not await approval inside same serialized turn via neuron-await; uses deferred answer pattern.

## Capability claim
One owner ask becomes a multi-tool plan with durable, selective human gates on side effects — not a model that fires tools and apologizes after the email already left.
