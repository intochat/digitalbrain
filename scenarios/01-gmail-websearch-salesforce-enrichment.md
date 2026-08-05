# Scenario 01: Gmail → web search → Salesforce account enrichment

## User intent
A sales owner receives an inbound Gmail from an unknown company domain and wants the brain to research the sender, propose a Salesforce account enrichment (industry, headcount, tech stack, recent news), and wait for one-tap approval before writing CRM fields.

## Trigger
External event: Gmail module polls/webhook delivers a new message matching owner filters (`from domain not in known accounts`, `label:inbound-sales`).

## Imagined modules
- Gmail (inbox watch, thread fetch, send)
- WebSearch (query planner + result normalizer)
- Salesforce (Account read/write, field schema map)
- Chat (approval surface, enrichment card)
- Memory (prior enrichments, owner preferences)
- Shell UI (account enrichment pane)

## Neuron map
| Neuron (kind/name) | Role |
|---|---|
| gmail/owner-inbox | Emit inbound email facts; fetch thread bodies on ask |
| accountenricher/default | Orchestrate research + SF draft; never write without approval |
| websearch/default | Answer search questions with ranked snippets |
| salesforce/owner-org | Read accounts; apply approved field patches |
| chat/sales-desk | Present proposal card; capture owner approval/reject |
| memory/owner-profile | Recall prior enrichment style and blocked domains |
| shell/primary | Open enrichment scene when proposal lands |

## Synapse choreography
1. `gmail/owner-inbox` **broadcasts** `EmailReceived` (messageId, from, domain, subject, snippet, threadId).
2. `accountenricher/default` hears `EmailReceived`, **broadcasts** `EnrichmentTriggered` (correlation via Cause chain).
3. Enricher **directs** `AccountLookupAsked` → `salesforce/owner-org` (domain/email match).
4. Salesforce answers with directed `AccountLookupAnswered` (hit|miss, accountId?).
5. On miss/thin account: enricher **directs** `WebSearchAsked` → `websearch/default` (company + domain + recent funding queries).
6. WebSearch answers directed `WebSearchAnswered` (snippets, urls, extracted entities).
7. Enricher may **direct** second `WebSearchAsked` for tech-stack or news; same answer path.
8. Enricher **broadcasts** `AccountEnrichmentProposed` (accountId?, fieldDiff, sources[], confidence).
9. `chat/sales-desk` hears proposal, **broadcasts** `AssistantCardRendered` (enrichment card actions: Approve / Edit / Dismiss).
10. Owner taps Approve → shell **directs** `OwnerActionActivated` → chat → enricher as `EnrichmentApprovalGranted`.
11. Enricher **directs** `AccountPatchAsked` → Salesforce; Salesforce answers `AccountPatchApplied`.
12. Enricher **broadcasts** `AccountEnrichmentCompleted`; chat **directs** `AssistantResponded` to the desk thread summarizing what changed.

## Orleans / Core surface exercised
Serialized grain turns; DurableGrain journals; outbox durability; request context (owner/correlation inheritance); grain call filters (owner scoping on Salesforce/Gmail); streams or pub-sub for ambient `EmailReceived`/`AccountEnrichmentProposed`; module catalog registration of listeners; no reentrancy into the enricher from its own emitted broadcast handlers.

## Rich experience
Chat card with company blurb, confidence bar, field-diff table (old → proposed), source links, and Approve/Edit actions. Shell may open a side pane with full web snippets. Optional map pin if HQ found.

## Failure / adversarial cases
- Reentrancy deadlock if `AccountEnrichmentProposed` handler on enricher awaits a path that delivers back into the same neuron turn.
- Double-apply: two `EmailReceived` retries must watermark-dedup; `AccountPatchAsked` must be idempotent by proposalId so approval replay does not write twice.
- Cross-owner leak: another owner's Gmail listener must never hear this inbox's `EmailReceived` (owner-scoped placement/filters).
- Partial web failure: enricher must journal `EnrichmentResearchFailed` and still surface a thin proposal or explicit failure — never silent drop.
- Approval after account already merged: Salesforce answers conflict; chat shows `AccountPatchConflicted`.

## Capability claim
A single inbound email fact fans out through research and CRM as durable, approvable synapses with one correlation chain — not a chatbot tool dump the user must re-drive by hand.
