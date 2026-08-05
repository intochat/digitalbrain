# Scenario 09: Cross-module correlation on one owner thread

## User intent
Owner starts a single chat thread "Close the Northwind renewal this week." Over hours, email replies, Salesforce stage changes, calendar holds, and assistant notes must remain one causal conversation — one correlation lineage the owner can audit end-to-end.

## Trigger
Initial `UserMessaged` that opens/names a work thread; subsequent external events attach via account/thread keys and shared correlation.

## Imagined modules
- Chat (thread neuron)
- Gmail
- Salesforce
- Calendar
- Tasks
- Correlation/Index (optional projector)
- Shell UI (thread timeline)

## Neuron map
| Neuron (kind/name) | Role |
|---|---|
| chat/northwind-renewal | Owner thread; primary narrative |
| gmail/owner-inbox | Inbound related mail |
| salesforce/owner-org | Opportunity stage changes |
| calendar/owner | Related holds |
| tasks/owner | Checklist items |
| correlation/owner | Index facts by correlationId + business keys |
| shell/primary | Thread inspector |

## Synapse choreography
1. Chat turn1: `UserMessaged` → assistant **broadcasts** `WorkThreadOpened` (threadKey=northwind-renewal, correlation root).
2. Assistant **directs** SF read; **broadcasts** `OpportunityLinked` (oppId, threadKey).
3. Later: Gmail `EmailReceived` (In-Reply-To matches) → gmail **broadcasts** with headers; correlation projector **broadcasts** `FactCorrelated` (emailRef → threadKey).
4. Chat neuron hears `FactCorrelated` / filtered email → **broadcasts** `ThreadFactAttached` (kind=email).
5. Salesforce webhook → `OpportunityStageChanged` **broadcast**; correlated → `ThreadFactAttached` (kind=sf).
6. Owner in same chat: "send the proposal" → multi-tool with same request context correlation; `EmailSent` Cause-linked.
7. Calendar hold `CalendarEventCreated` attaches similarly.
8. Owner opens inspector: **directs** `ThreadTimelineAsked` → correlation → `ThreadTimelineAnswered` ordered by timestamp with synapseRefs across modules.
9. Chat can **direct** `AssistantResponded` summaries that only cite attached refs.

## Orleans / Core surface exercised
Request context propagation (correlation id); DurableGrain journals; pub-sub + filters; grain call filters for owner; streams for high-volume SF webhooks; placement of long-lived chat thread neuron; outbox durability; watchers for live thread UI.

## Rich experience
Single chat thread with interleaved system cards (email, SF stage chip, calendar). Right rail: correlation graph. Filter by module. "Show raw journal" deep link.

## Failure / adversarial cases
- Correlation pollution: opportunistic keyword match attaches wrong email → require strong keys (oppId, message-id, threadKey) before auto-attach; soft attach needs owner confirm.
- Context loss on hop: any module that drops request context breaks the chain — call filters must rehydrate from Cause.
- Two owners same account name: owner scope always first.
- Replay of webhooks duplicates `ThreadFactAttached` — dedup by source synapseRef.
- Thread rename: immutable threadKey; display title can change via `WorkThreadRenamed`.

## Capability claim
An owner workstream is one durable correlation fabric across modules — the product can show a single causal thread where normal chatbots only have disconnected tool transcripts.
