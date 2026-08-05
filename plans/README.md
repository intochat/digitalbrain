# Plans — product resurrection via Core stability

## Documents

| File | Purpose |
|---|---|
| [PLAN-2000.md](PLAN-2000.md) | **2000 ordered actions** end-to-end |
| [BATCHES.md](BATCHES.md) | 40 batches × 50 items (execution units) |
| [TRACE.md](TRACE.md) | 50 scenarios RED/GREEN matrix |
| [EXECUTION-LOG.md](EXECUTION-LOG.md) | What ran and what passed |

## Goal

Extremely flexible **self-aware OS** on thin neuron/synapse Core. Prove stability against **50 user scenarios** using **mock neurons** (X, Gmail, Salesforce, …) until real integrations exist.

## How to execute (not 1000 concurrent agents)

Spawning 1000 live subagents in one session is **unsafe** (contention, thrash, unverifiable output). The plan is sized for **1000 agent-slots of work** as **40 batches of 50 items**, executed with **small parallel fan-out (≤10 agents)** per wave, Core gate green after each wave.

```
Batch 01–04  → Core proofs + harden     (A)
Batch 05–10  → Mock platform            (B)
Batch 11–30  → Scenarios 01–50          (C)
Batch 31–36  → Product resurrection     (D)
Batch 37–40  → Self-aware OS loops      (E)
```

## Non-negotiables

1. `CORE-ARCHITECTURE.md` physics.
2. Real Orleans TestCluster + journals for e2e.
3. Mocks are **neurons** emitting **synapses**, not silent method stubs.
4. Prefer delete. No dual bus. No fat Abstractions.
5. Scenario green only when TRACE row says GREEN with quoted proof.

## Current baseline

- Stage-1 Core: thin Abstractions + runtime, **6 tests green**.
- Next: Batch 01 (Core P0 proofs).
