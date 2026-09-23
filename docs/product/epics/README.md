# Epics

An epic is a reviewable unit of delivery with one ID, a status, its tasks and its acceptance. The
master plan is `docs/superpowers/plans/2026-09-23-intochat-product-delivery.md`; the execution ledger
is `docs/superpowers/plans/2026-09-23-intochat-product-delivery-verification.md` (evidence lives
there, not here).

Status vocabulary: **not started**, **in progress**, **blocked** (names the gate), **done**.

## Index

| Epic | Name | Phase | Status | Plan tasks |
|---|---|---|---|---|
| E0.1 | Trash & truth | P0 | in progress | P0.1, P0.2 |
| E0.2 | Lean profile, telemetry hygiene, usage capture | P0 | not started | P0.3–P0.8 |
| E1.1 | Ask & see (J1) | P1 | not started | P1.1–P1.4 |
| E1.2 | Draw & keep safely (J2a/J2b) | P1 | not started | P1.5–P1.9 |
| E2.1 | Durable foundations | P2 | not started | P2.1–P2.5 |
| E2.2 | Make it yours (J5) | P2 | not started | P2.6–P2.8 |
| E2.3 | Find & install (J3) | P2 | not started | P2.9, P2.10 |
| E3.1 | Spend limits & billing | P3 | blocked (G-1, G-2, G-3, G-9, G-10) | P3.1, P3.2 |
| E4.1 | Broker gateway & remote apps | P4 | blocked (G-3) | P4.1 |
| E4.2 | Publisher program, certification, earnings | P4 | blocked (G-1, G-2, G-3) | P4.2, P4.3 |
| E5a.1 | Creators publish | P5a | blocked (G-1, G-3) | P5a.1 |
| E5b.1 | Sandboxed code apps | P5b | blocked (G-8) | P5b.1 |

## Working assumptions

Every D and T decision is a working assumption until recorded in
[`../decisions/`](../decisions/README.md); the plan §0.1 assumptions apply to every epic below.

## How to add an epic

Copy [`_templates/epic.md`](_templates/epic.md) to `<phase>/<id>-<slug>.md`, set its status, and add a
row above. An epic that depends on a gate is `blocked` and names the gate; it is never `done` while a
required gate is open.
