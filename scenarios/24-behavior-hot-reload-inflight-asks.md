# Scenario 24: Behavior hot-reload while asks are in flight

## User intent
Owner has a live behavior that asks Salesforce during `EmailReceived`. They deploy a fixed revision while a prior ask is still unanswered. The system must finish or safely abandon in-flight work without corrupting journals, double-creating tasks, or leaving orphan approvals.

## Trigger
`BehaviorActivateAsked` for revision N+1 while revision N has open deferred work (outstanding `AccountLookupAsked` / timer).

## Imagined modules
- Behaviors / BehaviorHost / Catalog
- Gmail
- Salesforce
- Tasks
- Chat
- Shell studio

## Neuron map
| Neuron (kind/name) | Role |
|---|---|
| behaviorcatalog/owner | Revision pointer |
| behaviorhost/worker | Run generations of kinds |
| gmail/owner-inbox | Traffic during deploy |
| salesforce/owner-org | Slow answers |
| tasks/owner | Side effects |
| chat/owner-desk | Status |
| shell/primary | Deploy UI |

## Synapse choreography
1. Rev N behavior receives `EmailReceived` → **directs** `AccountLookupAsked` (open ask; deferred answer).
2. Owner deploys rev N+1 → `BehaviorCompileAsked` ok → `BehaviorActivateAsked`.
3. Catalog **broadcasts** `BehaviorSuperseded` (old=N, new=N+1, policy=drain|cutover).
4. Under drain policy: host keeps rev N grain generation until open asks complete; new emails route to N+1.
5. Salesforce answers `AccountLookupAnswered` → delivered to rev N continuation handler → may `TaskCreateAsked` once → `BehaviorGenerationDrained`.
6. Under cutover policy: open asks get `DeliveryFailed` / `AskAbandoned` journaled on behavior; no task create; UI shows abandoned count.
7. Catalog **broadcasts** `BehaviorActivated` (N+1) when ready; topology updates.
8. Studio shows dual-generation status until drain completes.

## Orleans / Core surface exercised
Grain versioning / generation fencing; DurableGrain journals; outbox; serialized turns; journal storage ETag fencing stale activation; module catalog; request context; deferred multi-turn asks; placement; no silent drop of answers — terminal fact required.

## Rich experience
Deploy banner: "Draining 1 in-flight ask"; generation table; force cutover button with warning; journal deep links for abandoned asks.

## Failure / adversarial cases
- Two activations both handle same answer → fence by generation; only matching generation accepts Answers metadata.
- Task double-create on retry after upgrade: idempotency key includes behaviorRev+emailId.
- Host process crash mid-drain: on restart, reopen from journal open-asks list.
- Rev N+1 fails boot answerer cardinality: activation refused; N remains; `BehaviorActivationFailed`.
- Reentrancy during cutover: still no neuron-await-neuron.

## Capability claim
Hot-reloaded user code participates in the same durable ask/answer lifetime rules as shipping modules — upgrades are journaled state transitions, not process roulette.
