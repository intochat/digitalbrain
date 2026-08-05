# Scenario 30: User corrects AI mid-stream; cancel + replan

## User intent
While the assistant is streaming a long answer and has already fired a capability (e.g., drafting three emails), the owner interrupts: “Stop—only draft the one to Priya, and make it shorter.” In-flight work must cancel, partial side effects must not send mail, and a new plan must start with clear UI.

## Trigger
User cancel/correct action during SSE token stream (chat interrupt, Stop button, or new UserMessaged superseding turn).

## Imagined modules
- Chat streaming edge
- Assistant / model team
- CapabilityBroker
- EmailDraft module
- Outbox (send vs draft boundary)
- UiProjector (streaming markdown + cancel)

## Neuron map
| Neuron (kind/name) | Role |
|---|---|
| Chat / thread-12 | Serializes turns; owns transcript facts |
| Assistant / thread-12 | Streams tokens; selects tools |
| CapabilityBroker / thread-12 | Tracks CapabilityRequested correlation |
| EmailDraft / default | Builds drafts; does not send until confirmed |
| SendGate / default | Only hears ConfirmedSend |
| UiProjector / shell | Stream deltas + Stop control |

## Synapse choreography
1. `UserMessaged` → Assistant emits streaming `AssistantDelta` (broadcast to UI) and `CapabilityRequested(DraftEmails×3)`.
2. EmailDraft begins work; journals `DraftStarted` per recipient; no `EmailSent`.
3. Owner taps Stop / sends correction: edge emits `TurnCancelled(correlation)` and new `UserMessaged` (directed into Chat).
4. Chat journals cancel; Emits `CapabilityCancel(correlation)` broadcast/directed to broker and draft neurons.
5. EmailDraft hears cancel: abandons incomplete drafts; completed draft bodies stay as `DraftAbandoned` or soft-delete facts—not sends.
6. Assistant aborts stream (`AssistantStreamEnded(reason=cancelled)`); starts new turn with correction context from journal, not ghost tokens.
7. New plan: single `CapabilityRequested(DraftEmail Priya)`; on success `AssistantResponded` + `UiSurface(DraftCard, actions=[Send, Edit])`.

## Orleans / Core surface exercised
Serialized grain turns (one chat turn chain); cancellation tokens into handlers; DurableGrain journals for correlation ids; outbox durability (nothing sent without SendGate fact); request context correlation; grain call filters tagging cancel; no reentrancy deadlock on cancel path.

## Rich experience
Streaming markdown freezes with “Stopped”; abandoned draft chips grey out; new plan card appears; undo banner if one draft was already confirmed pre-cancel (edge case).

## Failure / adversarial cases
- Cancel arrives after SendGate already emitted EmailSent → cannot unsend silently; must journal `CancelTooLate` and surface truth.
- Double-apply: retry of cancelled capability after restart → correlation must be terminal in journal.
- Stream tokens continue after cancel due to race → UI and journal must treat post-cancel deltas as dropped; Core delivery respects turn epoch.
- Correct message processed before cancel → ordering on Chat neuron must serialize; no mixed plan.

## Capability claim
DigitalBrain makes interrupt, cancel, and replan durable causal facts across modules—not a client-only “stop generating” that still fires side-effecting tools.
