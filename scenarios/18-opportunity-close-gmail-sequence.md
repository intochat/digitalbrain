# Scenario 18: Opportunity closed-won → Gmail sequence + internal fan-out

## User intent
When a Salesforce opportunity moves to Closed Won, the brain starts a coordinated sequence: thank-you email to champion, internal win notice, customer success task, kickoff calendar hold — with spacing and cancellation if the stage reverts.

## Trigger
External SF stream/webhook: stage becomes ClosedWon.

## Imagined modules
- Salesforce
- Gmail (sequences)
- Tasks (CS handoff)
- Calendar
- Chat (notify owner)
- SequenceRunner (behavior or module)
- Shell

## Neuron map
| Neuron (kind/name) | Role |
|---|---|
| salesforce/owner-org | Emit stage changes |
| winsequence/owner | Orchestrate multi-step sequence |
| gmail/owner-inbox | Send sequence steps |
| tasks/cs | Create onboarding task |
| calendar/owner | Kickoff hold |
| chat/owner-desk | Win toast |
| shell/primary | Sequence progress UI |

## Synapse choreography
1. SF **broadcasts** `OpportunityStageChanged` (oppId, from, to=ClosedWon, amount).
2. Win sequence hears → **broadcasts** `WinSequenceStarted` (sequenceId, oppId).
3. Immediate: **directs** `EmailSendAsked` thank-you (or propose+approve per policy) → `EmailSent` step1.
4. **broadcasts** `InternalWinNotified` content; chat card to stakeholders via `AssistantCardRendered`.
5. **directs** `TaskCreateAsked` for CS → `TaskCreated`.
6. **directs** `CalendarEventProposed` / create kickoff → `CalendarEventCreated`.
7. Reminder +2 days: `SequenceStepDue` → gmail step2 case study soft-ask email.
8. If SF **broadcasts** `OpportunityStageChanged` to not-won: **broadcasts** `WinSequenceCancelled`; cancel pending reminders; do not send step2.
9. Completion: `WinSequenceCompleted` with step journal refs.

## Orleans / Core surface exercised
Reminders for sequence spacing; DurableGrain journals; pub-sub; outbox durability; serialized sequence neuron; request context; grain call filters; streams from SF; cancellation token of saga via journaled cancel fact not thread abort.

## Rich experience
Win confetti card with amount; sequence timeline (step states); pause/resume controls; links to opp, email, task, event.

## Failure / adversarial cases
- Duplicate ClosedWon webhooks: sequenceId keyed by oppId+wonTransitionId; second start no-op.
- Step email fails: `SequenceStepFailed` retry policy; do not skip journal.
- Stage flip after step1: cancel must be best-effort on unsent steps; cannot unsend — journal honesty.
- Wrong template champion email: contact role resolution required before send.
- Reentrancy: sequence must not handle its own email bounce in a way that deadlocks — bounce is new delivery.

## Capability claim
CRM state changes drive durable, cancelable cross-module sequences — business process as synapses, not a Zapier black box beside a chatbot.
