# DigitalBrain V2 Product Architecture

**Status:** Approved — 2026-08-06  
**First delivery:** a complete Account Enrichment vertical slice

## Product outcome

DigitalBrain turns a trusted input into a durable, explainable product outcome. The first outcome is account enrichment:

1. A chat command or a new-Gmail webhook starts an enrichment run.
2. Gmail and web-research evidence produce a prepared Salesforce change.
3. Approvals freezes the complete proposal: evidence, intended mutation, deadline, and allowed action.
4. A user approves or rejects the whole proposal.
5. Salesforce performs exactly the approved mutation, then reports confirmed or outcome-uncertain.

The same architecture supports a sales query such as “show sales for the last week”: the product emits typed sales data and a declarative chart surface; it does not make the renderer or chart library a Core concern.

## Architectural decision

Core is a small durable behavior model, not an application framework. Hosting and Access enforce the mechanics modules cannot safely provide. Product modules own their domain facts, choreography, projections, and integrations.

| Layer | Owns | Must not own |
| --- | --- | --- |
| Core | bound behavior turn, typed synapse emission, optional durable behavior state, journal language | tenants, users, permissions, workflow templates, UI, provider SDKs |
| Hosting | physical workspace scope, catalog, directed delivery, one recorded turn, outbox/recovery, serialization, deployment lifecycle | product decisions or business approval policy |
| Access | authenticated ingress, scope-bound capabilities, opaque action validation, journal authorization | module choreography or provider credentials in UI facts |
| Product modules | typed domain facts, correlations, run state, proposal semantics, provider adapters, durable outcomes | Orleans, Hosting internals, raw journal access, direct native dispatch |
| Presentation edge | Base UI Kit rendering, context-preserving drawer, inbox badge, chat delivery | correctness of an approval or external mutation |

## Non-negotiable invariants

- One DigitalBrain is one workspace/tenant. A workspace scope is owned by Hosting and Access, never carried in a module fact or accepted from a user request.
- A physical host key is workspace scope plus the public relative NeuronId. The public NeuronId remains a module-local identity.
- A successful behavior turn records input, all outputs, touched state, and the deduplication watermark in one durable write. Delivery occurs only afterward.
- A directed output is recorded with its receiver snapshot before it is delivered. It cannot silently fall back to broadcast or a different workspace.
- Ordinary broadcasts do not loop back to their producing behavior. A module that needs to compensate for its own terminal delivery failure uses a separate observer behavior and an explicit internal fact; this preserves the non-reentrant host model rather than creating an awaited self-delivery path.
- Access capabilities are already bound to a trusted scope. A caller cannot select a scope through PublishAsync, ReadAsync, or a UI action.
- External ingress is explicit and capability-bound: a source channel can publish only the registered input synapse types it was issued for. An ingress type is external-only, so a behavior cannot emit it; a reusable module exposes a distinct external command and ingress adapter whenever the same domain transition also has an internal producer. Product outputs such as `ApprovalGranted` cannot be source-authored.
- Every journaled synapse has authoritative origin authority (`ExternalIngress` or `Internal`) as well as its source identity. Hosting preserves it across journal reads, outbox delivery, reactivation, and replay, so a behavior can safely distinguish a trusted edge command from an internal fact without trusting caller-supplied data.
- Hosting stamps every source publication with an authoritative occurrence time. Approval input carries only the decision and frozen-proposal proof; actor and decision time are derived from the authenticated source origin, not caller payload. The timestamp fences late input and supports audit; it is not, by itself, a distributed reservation of a pending proposal across independent outboxes.
- Hosting selects workspace-local implementations of narrow provider interfaces while constructing a behavior. Modules receive interfaces such as `ISalesforceGateway`, never a scope key, raw credential, or generic service locator.
- Approval correctness is durable proposal state. A drawer, toast, push notification, or chat delivery is only a presentation.
- A Salesforce mutation follows prepared, invoking, confirmed, or outcome-uncertain semantics. Unknown external outcomes are never reported as success and are not blindly retried.
- Product correlations such as EnrichmentRunId, ProposalId, RequestId, and SalesQueryId remain typed product vocabulary. Core causation is provenance, not workflow correlation.

## V2 module map

| Module | First V2 responsibility | Boundary |
| --- | --- | --- |
| Conversation | chat command and result facts | triggers an enrichment or sales-query run; no direct provider calls |
| Presentation | declarative Base UI Kit surfaces and ActionRef routing | renders facts; does not decide or mutate |
| Connections | provider authorization custody and reauthorization interactions | separate from business approval |
| Google / Gmail | mailbox evidence and webhook adaptation | webhook becomes a trusted ingress fact |
| Web Research | bounded evidence acquisition | returns evidence or failure facts |
| Salesforce | prepare, invoke, confirm/reconcile Salesforce mutations | never self-approves |
| Approvals | frozen proposal, durable pending inbox, decision audit, expiry outcome | reusable product module, explicitly not Core |
| Time | durable proposal deadline facts | no generic cron, schedule language, or recurring jobs yet |
| Memory | typed store/search/remove and optional enrichment context | a miss or outage cannot block or alter a Salesforce change |
| Account Enrichment | end-to-end run choreography | owns the first vertical slice instead of a generic Tasks protocol |
| Sales Insights | typed query result and chart/table surface facts | chart rendering remains Presentation |

There is no V2 Tasks migration. The V1 Tasks module mixed worker dispatch, OAuth custody, reminder mechanics, and an unimplemented approval marker. V2 retains its useful invariants—opaque exact action binding, stale/expiry fencing, idempotency, and outcome uncertainty—but locates them in Connections, Approvals, Salesforce, Time, and each typed workflow.

## Approval and dynamic UI model

Approvals stores a frozen semantic proposal. It contains a redacted evidence snapshot, an exact intended change, expiry/deadline, permitted decisions, and the audit actor once decided. Its semantic payload never changes after proposal creation. `ApprovalProposalSubmitted` is the explicit edge command, while `ApprovalProposed` is the internal fact emitted by an owning workflow; this avoids making a workflow output source-authorable. `ApprovalPending` is the redacted cross-module projection: it deliberately excludes the action binding and execution target, which remain only in Approval's durable state.

A user-facing decision supplies only proposal proof, decision identity, and approve/reject; the trusted ingress records the source actor and Hosting-stamped time before it reaches the proposal state machine. A single verified decision that reaches Approval before the proposal's independent outbox delivery is buffered durably and applied only after the proposal is frozen. In the current event-driven composition, a UI must regard the decision as accepted only when that authority records its resulting lifecycle outcome. This retains monotonic expiry when independent outboxes reorder a decision and deadline signal. If product policy requires “source publication before due always wins,” the next iteration is a proposal-keyed, capability-bound decision-admission endpoint at the Access/Edge boundary, or an explicit durable closing barrier—not a reversal from expired to approved inside Approval state.

Presentation can choose at runtime how to render that payload from the Base UI Kit:

- a compact chat card and context-preserving review drawer for a proposal carrying the frozen opaque chat context;
- an inbox-only review surface for a webhook-triggered proposal with no chat context;
- a detail, diff, evidence, or risk-oriented layout depending on the proposal.

The renderer receives declarative surface nodes plus opaque, scope-bound ActionRef values. The review context is an opaque product route, never a workspace scope, credential, or executable action. Review evidence links are reduced to safe HTTPS path references before they cross into the renderable pending/surface facts. The renderer may adapt layout, but it cannot invent a mutation, action, or authorization. Custom third-party UI renderers and the Flutter browser integration are deliberately later work.

## Marketplace posture

The initial marketplace supports trusted, approved publisher packages composed into a static catalog at deployment/startup. Assembly load contexts are lifecycle and unload mechanics only; they are not a security sandbox. Runtime catalog epochs, package activation/drain, compatibility, and untrusted package policy are later Hosting work. Installed modules remain unable to reference Hosting, Access, Orleans, or the testing host.

## Delivery sequence

1. Scope and Access binding: physical identity is scoped before a new cross-name route is exposed.
2. Directed durable dispatch: preserve broadcast, add a receiver snapshot for explicit target delivery, and keep it inside the sender’s Hosting scope.
3. Product choreography: create typed run/proposal/mutation facts; do not add a generic workflow engine.
4. Time and Approvals: proposal expiry/deadline and frozen whole-proposal decisions.
5. Memory: V2 typed vector store with a real Qdrant container suite; optional-only enrichment context.
6. Gmail, web research, Salesforce, and Account Enrichment: complete chat and webhook paths with provider fakes in normal tests.
7. Presentation and Sales Insights: dynamic Base UI Kit surfaces, pending approval inbox, and a last-week-sales chart.
8. Later only when evidence requires it: bounded worker lanes, catalog epochs, runtime subscriptions, marketplace lifecycle, roles/permissions, custom renderer hosts.

## Provider binding

Scope-bound channels protect ingress and journal reads, but provider work needs
the same boundary. A shared singleton Gmail, Salesforce, or Memory adapter
cannot safely infer which workspace invoked it. Hosting therefore owns
workspace-local provider binding: trusted composition registers a factory for a
narrow module interface, Hosting supplies an opaque workspace binding while it
constructs a behavior, and the behavior receives only the resulting interface.
The binding is not a product fact and modules cannot reference Hosting to obtain
it. This is the seam that lets one shared runtime safely host several product
workspaces and remains compatible with trusted marketplace modules.

## Testing policy

Tests express visible guarantees, not implementation call counts:

- use controlled Gmail, web-research, and Salesforce adapters in normal tests;
- use one real Qdrant container suite for Memory;
- do not use live Google or Salesforce accounts in normal CI;
- prove proposal freeze, action binding, expiry, duplicate/stale decision rejection, and no mutation before approval;
- prove a Salesforce outcome-uncertain result remains distinct from confirmation;
- prove scope isolation and directed delivery through recorded journals and recovery, not private method invocation;
- use the composed test host for Core/Hosting mechanics and module-specific integration seams for vertical behavior.

## Explicit non-goals for this iteration

- a generic Tasks or workflow engine;
- role and permission product design beyond recording the approving actor;
- generic cron or recurring scheduling;
- third-party/custom UI renderers;
- untrusted marketplace execution or runtime package activation;
- global journal search, cross-workspace journal reads, native request/reply, or dynamic subscriptions.
- a reconnectable workspace-wide pending-approvals aggregate. This slice emits durable per-proposal inbox transitions; the home dashboard’s aggregate/read model is the next Presentation iteration.
