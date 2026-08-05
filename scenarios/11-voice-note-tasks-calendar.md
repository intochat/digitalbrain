# Scenario 11: Voice note → tasks + calendar holds

## User intent
Walking between meetings, the owner records a 90-second voice note: "Need to call Priya about the contract Monday morning, send the redlines today, and block two hours Friday for the board deck." The brain transcribes, extracts actionables, creates tasks, and proposes calendar blocks for confirmation.

## Trigger
Mobile shell: hold-to-talk control → `VoiceNoteCaptured` (audio blobRef).

## Imagined modules
- Voice/Speech-to-text
- Assistant (action extraction)
- Tasks
- Calendar
- Chat (confirmation cards on phone + desktop)
- Memory (who Priya is)
- Shell UI (multi-device)

## Neuron map
| Neuron (kind/name) | Role |
|---|---|
| shell/mobile | Capture audio control |
| voice/default | Transcribe audio |
| assistant/owner | Extract actions; propose side effects |
| contacts/owner | Resolve "Priya" |
| tasks/owner | Create tasks |
| calendar/owner | Propose/create holds |
| chat/owner-desk | Confirmation UX |
| shell/desktop | Mirror cards |

## Synapse choreography
1. Shell **broadcasts** `VoiceNoteCaptured` (blobRef, duration, deviceId).
2. Voice hears → **directs** STT IO → **broadcasts** `VoiceTranscriptReady` (text, confidences[]).
3. Assistant hears transcript → **broadcasts** `ActionablesExtracted` (items: Call, Email/Send, FocusBlock).
4. Assistant **directs** `ContactResolveAsked` ("Priya") → `ContactResolved`.
5. For each actionable without hard side effect: **directs** `TaskCreateAsked` → `TaskCreated` (may auto-create if policy allows voice→task).
6. For calendar-affecting items: **broadcasts** `CalendarEventProposed` × N; **directs** `ApprovalBundleAsked` if create is gated.
7. On grants: calendar creates → `CalendarEventCreated`; chat **broadcasts** `AssistantCardRendered` summary.
8. Cross-device: desktop session hears same `ActionablesExtracted` / cards via owner pub-sub.
9. Owner corrects one item on desktop → `ActionableCorrected` → task/calendar patch asks.

## Orleans / Core surface exercised
DurableGrain journals; request context multi-device same owner; streams for transcript progress; timers none critical; outbox; grain call filters; serialized assistant turn for extraction; module catalog.

## Rich experience
Waveform + live partial transcript; checklist of extracted actions with edit chips; calendar ghost events; push notification "3 actions from voice note".

## Failure / adversarial cases
- Bad audio: low confidence → `VoiceTranscriptUnreliable`; do not create tasks silently.
- Wrong contact: always show resolved identity before call task finalizes.
- Duplicate voice uploads: blob hash dedup.
- Partial create: task made, calendar denied — journal both; UI shows mixed state.
- Background mobile kill: blob and `VoiceNoteCaptured` durable; processing resumes on silo not on phone.

## Capability claim
Ephemeral speech becomes durable, editable, cross-device action synapses wired into tasks and calendar — not a transcript dump trapped in a chat bubble.
