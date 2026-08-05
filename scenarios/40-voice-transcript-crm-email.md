# Scenario 40: Voice call transcript → CRM note + follow-up email draft

## User intent
After a phone/VoIP call, the owner wants the transcript summarized into a CRM contact note and a follow-up email draft ready to edit/send—automatically when the call ends.

## Trigger
Telephony adapter emits `CallEnded(callId, recordingRef)` or `TranscriptReady`.

## Imagined modules
- Telephony / transcription
- Summarizer
- CRM contacts
- EmailDraft
- Chat/UI confirm
- Memory of commitments

## Neuron map
| Neuron (kind/name) | Role |
|---|---|
| TelephonyIngress / default | Call lifecycle facts |
| Transcriber / call-77 | Audio → TranscriptReady |
| CallCoach / call-77 | Summary, action items |
| CrmNotes / default | Writes activity on contact |
| EmailDraft / default | Drafts follow-up |
| UiProjector / shell | Draft + CRM preview |
| CommitmentMemory / life | Stores promises |

## Synapse choreography
1. `CallEnded` → Transcriber work (worker) → `TranscriptReady` broadcast.
2. CallCoach hears → Emits `CallSummarized`, `ActionItemsProposed`, Ask `ResolveContact(phone/email)`.
3. On contact resolved: directed `AppendCrmNote`; CRM replies `CrmNoteLogged`.
4. Parallel: `FollowUpEmailDrafted` + UiSurface; owner ConfirmSend → SendGate.
5. CommitmentMemory hears ActionItemsProposed → durable commitments for later briefings.
6. Assistant may notify: “Call with X processed” with links.
7. Correlation = callId across all journals.

## Orleans / Core surface exercised
Streams or chunked transcript progress; DurableGrain journals; Ask/Answer; outbox for CRM/email; serialized call context grain; timers if transcription SLA exceeded.

## Rich experience
Post-call pane: transcript search, summary bullets, CRM note preview, email draft editor, buttons Save CRM / Send email / Create tasks; waveform optional.

## Failure / adversarial cases
- Wrong contact resolution → confirm gate before CRM write when confidence low.
- Transcript partial then final → don’t double-append CRM notes (idempotent callId).
- Send email without CRM success → independent paths; UI shows split status.
- Recording retention policy → blob lifecycle facts; journals never store raw audio secrets.

## Capability claim
DigitalBrain chains voice, CRM, and email as one call-scoped causal workflow with human gates—unlike a meeting-bot that only drops a transcript link.
