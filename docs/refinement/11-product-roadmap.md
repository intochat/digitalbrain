# 11 — Product Roadmap

Sequenced product increments. Each delivers a coherent user or platform outcome (no "refactor everything" phases), states the architectural prerequisites, and defines validation criteria. Ordering follows the prioritization rules: trust/correctness → duplicate authority → external-effect/self-evolution safety → identity/recovery → product journeys → deletion/simplification → reliability/performance. Cross-refs: [01](01-product-north-star.md) journeys, [12](12-implementation-plan.md) execution detail.

## Increment 0 — "Trustworthy floor" (platform; prerequisite for everything user-facing)

**User/platform outcome:** the system is safe to run and demo without a foot-gun. No behavior the user *sees* changes, but the dangerous machinery is switched off and the fail-open defaults are closed.

**Scope:**
- Disable Foundry Run/Deploy and pack embodiment by default (single fail-closed gate) — removes the Critical code-exec cluster from the live surface ([06](06-security-threat-model.md) P1).
- Close auth fail-open: environment-gate `DevAutoLogin`, require a bootstrap secret for first-user provisioning, remove plaintext passwords from journaled vocabulary ([06](06-security-threat-model.md) P3).
- Encrypt the DataProtection key ring; enforce SSRF egress allowlist; pin the `codegraph` npm build step ([06](06-security-threat-model.md) P5/P6).
- Delete the verified-dead duplicate stratum (legacy gateway/proto/stubs, second auth authority, dead Core/kernel/Flutter code) — attack-surface reduction ([09](09-code-quality-and-cleanup.md)).

**Architectural prerequisites:** none — this is deletion + config + point fixes.

**Validation:** Production rejects `DevAutoLogin`; no password material reaches any journal; key ring encrypted-at-rest test; Foundry-disabled boot test; build succeeds with network egress blocked; full test suite green after deletions.

**Why first:** it is the cheapest, removes the most risk, and unblocks an honest demo. It is mostly *deletion and disabling*, per the repo's own method.

## Increment 1 — MLP: "Governed Gmail + Salesforce" (the lovable v1)

**User outcome:** *"INO reads across my inbox and CRM, proposes exact CRM changes with a plain-language diff, applies them only after I approve, verifies the result, writes everything down, and lets me see my connections, history, and permissions and revoke them — and recovers cleanly if something is uncertain."* (Journeys 1, 2, 6, 7 from [01](01-product-north-star.md).)

**Scope:**
- Harden the connector auth loop: add PKCE to Google, unify the OAuth state machine, add Gmail `OutcomeUnknown` + idempotent send ([04](04-connectors-and-auth.md)).
- Ship the **control surface**: connections, proposals/approvals, history (journal, user-legible), permissions (grants) with revoke. The data exists; build the UI.
- Fix client reachability (macOS `network.client` entitlement, Android `INTERNET`, mic declarations) so the app actually runs on its target platforms ([platform-and-skills](file-audit/platform-and-skills.md)).
- Delete the orphaned v1 client rail; constrain the RFW action surface + URI scheme allowlist ([06](06-security-threat-model.md) P7).

**Architectural prerequisites:** Increment 0. Uses the existing strong INO/V2 substrate — no rail hardening needed because self-evolution is off.

**Validation:** the four journeys demonstrated end-to-end on a real macOS/Android/web build; Salesforce preview→apply→verify with a real conflict and a real verification-failure; an uncertain-outcome recovery demonstrated; injection-corpus test on read→propose; no token leakage (existing negative tests extended).

**Why:** this is the shortest path to a genuinely lovable, defensible product, and it rides the half of the codebase that is already strong.

## Increment 2 — "One identity, one core" (platform hardening)

**Platform outcome:** the system has a single identity/tenancy model and a delineated trusted core — the precondition for safe multi-user and safe self-evolution.

**Scope:**
- Converge identity/session onto `RuntimeSessionAuthority`; make principal/tenant mandatory on `Synapse` (additive → enforced) ([03](03-operating-system-assessment.md), [10](10-target-architecture.md)).
- Delineate the TCB; move Core crypto/rate-limiter into it; give the self-evolution rail a tenant boundary.
- Durability groundwork: deterministic synapse ids, explicit `[Id]`, contract-freeze tests, journal compaction ([07](07-performance-and-reliability.md)).

**Architectural prerequisites:** Increment 0 (dead auth authority deleted).

**Validation:** end-to-end cross-tenant negative test that actually exercises production isolation (closes `dotnet-tests:SEC-600`); journal-growth benchmark shows flat per-message cost after compaction; contract-freeze tests pass.

## Increment 3 — "Teach it a trick, safely" (self-evolution v1)

**User outcome:** journeys 3–5 — *"propose an automation → approve → watch it,"* and *"teach a new behavior → evaluate → approve → use → roll back."* This is the project's headline capability, shipped only once it is actually governed.

**Scope:**
- Lift the self-evolution rail onto the INO evidence model: principal-bound, content-hashed, single-use approvals verified in the TCB; enforce `RequiresHumanApproval`; in-kernel risk classification ([05](05-self-evolution.md)).
- Real checkpoint/restore; retriable idempotent apply; verify phase; tamper-evident audit journal.
- Route **all** execution out-of-process (Run, Deploy, scripts) with timeouts/resource caps; gate Deploy; make executor grains reachable only via the apply registry; fix the ScriptRunner zero-reference gate ([06](06-security-threat-model.md) P1).
- Enforce pack signing + publisher trust before embodiment.
- Per-tier policies (T0–T5); remove/hard-restrict `TrustedAutoApply`.

**Architectural prerequisites:** Increments 0–2 (TCB delineated, identity unified, durability groundwork).

**Validation:** escape-attempt tests (reflection/`dynamic`/env/`Exit`/infinite-loop) must fail to execute; forged/unbound/replayed approval rejected; a full teach→evaluate→approve→use→**rollback that actually restores** demonstrated; unsigned pack refused.

**Why last of the user increments:** it is the highest-risk capability and depends on the trusted core being real. Shipping it before Increments 0–2 would be shipping the current Critical findings.

## Increment 4 — "Connect to anything" (platform extensibility)

**Platform/user outcome:** a third connector can be added without touching kernel orchestration; users gain new capabilities without a kernel release.

**Scope:**
- Introduce `ConnectorRegistry` + `CapabilityManifest`; migrate Gmail/Salesforce to manifests; delete hardcoded provider branches; extract the shared `ConnectorHost` (rate-limit/backoff/pagination/minimization/egress) ([04](04-connectors-and-auth.md)).
- Generalize Salesforce's preview→apply→verify into the shared mutation contract.

**Architectural prerequisites:** Increments 1–2.

**Validation:** add a third connector (e.g. a calendar or ticketing provider) with **zero** kernel/INO edits, exercising read + previewed mutation through the registry.

## Increment 5 — "Scale and operate" (reliability/performance)

**Platform outcome:** the system runs multi-replica with real health, real rolling deploys, and measured capacity.

**Scope:**
- Multi-replica edge (replica-safe session/feed; resolve Orleans double-config); fix `/health` to reflect Orleans connectivity; real drain/verify rolling update (replace the simulation).
- Run the benchmark plan ([07](07-performance-and-reliability.md)); set explicit capacity SLOs.
- Decide the `net11.0`/alpha-journaling toolchain question deliberately (pin to LTS or accept+document) ([08](08-framework-and-dependency-audit.md)).

**Architectural prerequisites:** Increments 0–2 (durability, identity).

**Validation:** zero-downtime deploy demonstrated across ≥2 replicas; published capacity numbers; health reflects real dependencies.

## Sequencing summary

| Inc | Outcome | Depends on | Delivers journeys |
|---|---|---|---|
| 0 | Trustworthy floor | — | (safety; enables demo) |
| 1 | Governed Gmail+Salesforce (MLP) | 0 | 1, 2, 6, 7 |
| 2 | One identity, one core | 0 | (platform) |
| 3 | Governed self-evolution | 0,1,2 | 3, 4 |
| 4 | Connect to anything | 1,2 | 5 |
| 5 | Scale and operate | 0,2 | (platform) |

No increment is a pure refactor: 0 is safety-you-can-demo, 2 unblocks multi-user and self-evolution, 5 delivers measured scale. Increments 1, 3, 4 are directly user-visible value.
