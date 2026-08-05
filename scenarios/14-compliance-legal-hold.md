# Scenario 14: Compliance hold freezes destructive actions

## User intent
Legal places a hold on account "Contoso" for litigation. The brain must continue to capture and retain related facts (email, chat, CRM changes) but block deletes, purges, aggressive archive, and certain outbound communications until the hold lifts.

## Trigger
Admin/legal control: `LegalHoldPlaced` for subject keys (accountId, custodians[], modules[]).

## Imagined modules
- Compliance/LegalHold
- Gmail
- Salesforce
- Chat
- Memory/Storage GC
- Audit export
- Shell UI (admin)

## Neuron map
| Neuron (kind/name) | Role |
|---|---|
| compliance/org | Authoritative hold register |
| gmail/owner-inbox | Honor suppressions on delete/send policies |
| salesforce/owner-org | Block destructive field wipes / record delete |
| chat/* | Retain transcripts; block purge |
| gc/brain | Skip journal compaction subjects under hold |
| audit/export | Produce hold packages |
| shell/admin | Hold console |

## Synapse choreography
1. Legal UI **directs** `LegalHoldPlaceAsked` → compliance → on success **broadcasts** `LegalHoldPlaced` (holdId, subjects[], policy).
2. All sensitive modules hear and update local enforcement state as consequence of journaled fact (not silent config).
3. User tries chat "delete all Contoso emails" → gmail **broadcasts** `DestructiveActionBlocked` (reason=legal_hold, holdId); chat relays.
4. Normal `EmailReceived` for Contoso still **broadcasts** and journals; retention extended `RetentionExtended`.
5. Salesforce delete record attempt → `DestructiveActionBlocked`.
6. GC neuron before compact: **directs** `HoldCheckAsked` → compliance → deny compact for sequences in subject set.
7. Auditor **directs** `HoldExportAsked` → modules contribute → `HoldExportPackageReady`.
8. Lift: `LegalHoldLifted` **broadcast**; modules resume policies; fact remains for audit.

## Orleans / Core surface exercised
Grain call filters as enforcement point for destructive asks; DurableGrain journals (append-only alignment with hold); module catalog; request context for admin authz; pub-sub of hold facts; outbox; no silent memory-only flag; transactions not required if every denial is journaled.

## Rich experience
Red banner on any Contoso surface: "Legal hold active". Attempted delete shows blocked modal with holdId. Admin console: subjects, since, export button.

## Failure / adversarial cases
- Hold only in UI: insufficient — modules must hear `LegalHoldPlaced` or filters read compliance answers every time.
- Bypass via behavior script: behavior host still passes call filters; cannot emit forged Source for deletes without going through gated asks.
- Partial module miss (module down at place time): on activate, module must **direct** `ActiveHoldsAsked` and catch up.
- Cross-owner: hold is org-scoped; cannot leak Contoso content to another owner export.
- Double lift/place: idempotent holdId state machine journaled.

## Capability claim
Governance is a first-class synapse policy layer over the same journals that power features — compliance is not an external side database the assistant can ignore.
