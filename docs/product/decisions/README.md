# Decision register (ADRs)

This register is the index of recorded decisions. Per `highlevel.md` §12: *"Every decision below is
**proposed**. A ratified decision becomes an ADR in `decisions/`."* Every D and T row in highlevel
§12 still carries an unchecked **☐ accept / ☐ change** box, so none of them is recorded here as
ratified. Nothing in this register is a legal, company or provider fact.

Status vocabulary:

- **accepted** — an ADR recorded in this folder and in force.
- **proposed** — a recommendation in `highlevel.md` §12, not ratified.
- **working assumption** — the plan proceeds on it (`plan` §0.1), but it is not ratified.
- **deferred** — assigned to a later epic or a hard gate.

## Accepted ADRs

| ADR | Title | Status | Source |
|---|---|---|---|
| [0001](0001-process-runner-ownership.md) | One shared process runner | accepted | plan P0.1 |
| [0002](0002-one-live-table-contract.md) | One live-table contract | accepted | plan P1.3; C09/D6 |

## Proposed product decisions (highlevel §12, not ratified)

| ID | Decision | Register status | Where it lands |
|---|---|---|---|
| D1 | Primary customer and first release | working assumption | plan §0.1; Phase 1 |
| D2 | Deployment and tenancy | working assumption | plan §0.1; Phase 2 |
| D3 | How "show me / draw me" UI is produced | working assumption | plan §0.1; Phase 1 |
| D4 | How values are stored | working assumption | plan §0.1; P0.3/P2.4 |
| D5 | What an app is, and who may publish | working assumption | plan §0.1; P2.6 |
| D6 | Where data lives, and what the assistant may read | working assumption | plan §0.1; P1.3 |
| D7 | Compute model | working assumption | P1.4; prepaid wallet gated to P5a |
| D8 | Pricing and margin | target to ratify | P1.4/P2.2 shadow; Phase 3 |
| D9 | Take rate and merchant of record | target to ratify | Phase 4 entry; gate G-2 |
| D10 | Approvals | working assumption | P3.1 |
| D11 | Role of BDD | working assumption | P2.6/P4.2 |
| D12 | Discovery | working assumption | P2.9 |
| D13 | Vocabulary and the OS framing | working assumption | docs/CONTEXT.md |
| D14 | Cleanup scope and dev defaults | working assumption | P0.6 |
| D15 | LeadGenerator | working assumption | P2.10; gate G-6 |
| D16 | Selling entity, markets, customer type | **not decided — owner/legal** | gate G-1 |
| D17 | Planning governance | working assumption | this register; plan |

## Proposed technical decisions (highlevel §12, not ratified)

| ID | Decision | Register status | Where it lands |
|---|---|---|---|
| T1 | Stored-state migration | working assumption | P2.4 |
| T2 | Caller context and gateway access | working assumption | P2.1 |
| T3 | Ledger store | working assumption | P2.2 |
| T4 | Vault encryption | working assumption | P1.6 |
| T5 | Metering sources | working assumption | P0.5/P2.2 |
| T6 | Topology | working assumption | P2.5/two-silo test |
| T7 | Forms | working assumption | P1.5 |
| T8 | Durable jobs | working assumption | P2.7 |
| T9 | Orleans Journaling | working assumption | P0.3 |
| T10 | Payment providers | working assumption | P3.1/P4.3; gate G-2 |
| T11 | Prerelease dependencies | working assumption | P2 exit |

## Hard gates (not decisions)

The gates that no code can close are `plan` §0.3 G-1…G-10 (entity/tax, Stripe, counsel,
data protection, design partners, LeadGenerator sources, live-model keys, penetration test, VAT
adviser, assumption tests). They stay open until the owner/legal/provider closes them with the
evidence named there. A gate is never closed by a working assumption.

## How to add an ADR

Copy [`epics/_templates/adr.md`](../epics/_templates/adr.md) into this folder as the next
`NNNN-<slug>.md`, set status, and add a row to the accepted table. Ratifying a proposed D decision
means recording it; it never rewrites `highlevel.md` or `current-state.md`.
