# Scenario 25: Two owners, one silo — isolation under adversarial crosstalk

## User intent
Prove (as a product scenario) that two owners sharing a DigitalBrain silo/cluster cannot read each other's mail, journals, approvals, or dashboards even when neuron Names collide ("default", "desk") and when a malicious behavior tries to emit or ask across the fence.

## Trigger
Parallel activity: Owner A Gmail + chat; Owner B Salesforce + chat; Owner B installs a behavior that attempts to listen broadly and scrape.

## Imagined modules
- Security/tenancy
- Gmail (per owner)
- Salesforce (per owner)
- Chat
- Behaviors
- JournalQuery
- Shell sessions
- Grain call filters (Core-aligned)

## Neuron map
| Neuron (kind/name) | Role |
|---|---|
| gmail/a-inbox | Owner A mail |
| gmail/b-inbox | Owner B mail |
| chat/a-desk | Owner A chat |
| chat/b-desk | Owner B chat |
| salesforce/a-org | Owner A CRM |
| salesforce/b-org | Owner B CRM |
| behaviorhost/b-worker | B's sandboxed behaviors |
| journalquery/a | A-scoped query |
| journalquery/b | B-scoped query |
| security/tenancy | Owner binding on calls |

## Synapse choreography
1. Owner A: `EmailReceived` on `gmail/a-inbox` **broadcast** within A scope only.
2. Owner B chat asks "show all emails in the silo" → assistant **directs** `JournalRangeAsked` → `journalquery/b` → answer contains only B-visible facts; no A mail.
3. B behavior declares `INeuron<EmailReceived>` → activated under B host; does **not** receive A's `EmailReceived` (subscription scoped by owner/composition).
4. B behavior attempts `Ask` to `salesforce/a-org` → grain call filter **rejects**; sender journals `DeliveryFailed` / `AuthorizationDenied` with reason; no data returned.
5. Name collision: both have `chat/desk` kind/name pattern — keys must include owner partition (grain key encoding) so activations never merge.
6. Shared module kind code path: same assembly, different Name/owner — state and journals separate.
7. UI session for A subscribes to streams with owner token; forged owner header stripped/overridden by filter from auth context.
8. Audit: security **broadcasts** `CrossOwnerAttemptObserved` (to ops) without leaking payload.

## Orleans / Core surface exercised
Grain call filters; request context (owner as authenticated context, not forgeable module field); placement; DurableGrain journals per neuron identity; module catalog per composition; pub-sub scoping; outbox; grain key encoding; fencing; no transactions required.

## Rich experience
Admin/ops dashboard of denied cross-owner attempts (metadata only). Each owner shell remains clean. Behavior studio for B shows activation succeeded but "heard 0 foreign emails" live test.

## Failure / adversarial cases
- Trusting args.ownerId over token: forbidden pattern; filters must ignore.
- Broadcast bus global without owner partition: design failure — Core/composition must not offer unscoped ambient for private facts.
- Journal export tool with elevating bug: elevating role must be explicit, audited, break-glass.
- Timing side channel: constant-time deny where practical; never return "exists but denied" for other owners' neuron names if that leaks.
- Stale activation after owner offboarding: decommission `OwnerDecommissioned` stops grains and seals journals.

## Capability claim
Multi-tenant agentic OS isolation is enforced at delivery and journal boundaries — two owners on one Orleans silo still get separate nervous systems, not a shared chatbot memory puddle.
