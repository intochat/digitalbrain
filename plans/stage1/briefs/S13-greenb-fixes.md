# S1.3-GREEN-b — close the four GRILL BLOCKERs   (role: GREEN, surgical, security-critical)

Report path: `plans/stage1/reports/S13-oauth-greenb.md`

Read `plans/stage1/reports/S13-grill.md` in full first — it rejected the rail with 4 BLOCKERs.
Fix exactly those (take its MAJOR/MINOR findings only where they fall out of the same fix).
The core defect: **the principal appears on records but is not a verified control plane.**

## Fixes required

1. **BLOCKER 1 — Begin recovery is principal-blind.** Idempotent `Begin` recovery must require
   BOTH the same `CommandId` AND the same bound `Actor.PrincipalId`. A recovery attempt with a
   different principal refuses settled, indistinguishable from an unknown transaction (no
   existence leak).
2. **BLOCKER 2+4 — client-facing secret Take.** Raw authorization `Code` + `CodeVerifier` must
   NEVER cross a `[ClientEntryPoint]` surface. Remove `TakeCompletedCode` (and any secret-
   bearing reply) from the client-reachable interface; the rail consumes codes host-side only.
   If an internal grain-to-grain path needs it, put it on a non-client interface listed
   appropriately (mind kernel trap 3 for framework interfaces) and keep it one-shot.
3. **BLOCKER 3 — Actor is client-asserted on `db.mcp.*`.** Establish the trust boundary:
   - The server-side session/fire pipeline (where the authenticated principal from S1.2 lives)
     UNCONDITIONALLY overwrites the `Actor` on every actor-bearing synapse it dispatches —
     model- or client-supplied actor values never survive the boundary.
   - Verify where `db.mcp.*` synapses can originate: HTTP host paths (stamped), assistant
     session fire (stamp it now), direct `IDigitalBrain` cluster clients (operator scripting —
     document explicitly in the report as the owner-trusted domain, per the ratified scripting
     stance).
   - `McpServerNeuron` keeps refusing missing actors settled.
4. **Tests that prove the boundary** (TDD these first):
   - Forged actor through the session pipeline is replaced by the session's real principal
     (tool call executes under the true caller; forged principal's tokens untouched).
   - Cross-principal `Begin` recovery by stolen CommandId refuses like an unknown transaction.
   - The client contract surface exposes no code/verifier (compile-level: assert the shape).
   - Existing S1.3 pins stay green.

## Constraints
Smallest honest diff. No new packages. Settled refusals. Never log/journal secrets. Full gate
before report. No git. Note the known `ChartVocabularyProofs` load flake — a single timeout
there alone is not your failure; rerun cleanly to confirm.

## Definition of done
Gate green; all four BLOCKER attack paths provably closed by tests; report maps each BLOCKER →
fix → test name; any GRILL MAJOR/MINOR you did NOT take is listed explicitly for the backlog.
