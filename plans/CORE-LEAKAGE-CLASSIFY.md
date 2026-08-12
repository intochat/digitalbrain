# Core leakage classify table (Seam 3)

**Status:** Architect + Kernel SIGNED — bulk moves may proceed per order below  
**Date:** 2026-08-12 · Tip @ `/workspace/digitalbrain` · FINAL R6/R7 + SEAMS-2-4-ROUTING  
**Rule:** Contracts/DTOs may stay in Abstractions. Neurons leave interconnect-only Core. No opportunistic `ISynapseGraph` rename.

## Stay in Core (interconnect)

| Path | Why |
|---|---|
| `Neuron/`, `Outbox/`, journal pieces under Neuron | journal-is-outbox |
| `BroadcastCatalog.cs`, `BroadcastRoute.cs`, `BroadcastTopology.cs` | Emit fan-out |
| `DeliveryPolicy.cs`, `DeclarativeSynapseTransform.cs`, `ISynapseTransform.cs` | Edge morphs |
| `Identity/VerifiedActor*`, Principal* helpers used by delivery | ambient + re-enter |
| `Capabilities/`, `Filters/` (FrameworkInterfaces allowlist) | reification |
| `Hosting/`, `IModule.cs`, `Grain*` | runtime hooks |
| `Serialization/`, `SynapseAlias.cs`, `SynapseTypeIndex.cs` | wire helpers (or Abstractions later — not this seam) |
| **Cell grain runtime:** `Cell/CellNeuron.cs`, `Cell/CellState.cs`, `Cell/ICellKind.cs` | one compiled cell grain = interconnect substrate |

`SynapseGraphNeuron` / `ConnectionRelayNeuron` paths (wherever tip-located) stay Core interconnect.

---

## Leave Core (move after sign)

| Tip path | Destination owner | Target home (proposed) | Move notes | A18 coupling |
|---|---|---|---|---|
| `Core/Behavior/*` | Modules Engineer | `src/Modules/…` Behavior host (or holding `Modules/Execution` until host design) | Neuron + state; Studio still design-blocked — **move code only, no Studio UI** | installs/runs may touch corpus — coordinate 2b |
| `Core/Cell/CalculatorKind.cs` | Modules Engineer | Modules (Kinds) | Kind **program** leaves; CellNeuron stays | none |
| `Core/Library/*` | Modules Engineer | Modules/OS Library | Shared catalog stays `ForOwner` — see Library 2b **(a)** below | publish/discover grant-gate residual |
| `Core/Corpus/*` | Modules Engineer | Modules/Memory or OS Corpus | Flip callers to `ICorpus.ForPrincipal` (2b) | **before or with move** |
| `Core/Repository/*` | Modules Engineer | Modules | | |
| `Core/Workspace/*` | Kernel Engineer (host/OS boundary R7) | Kernel host / OS — **not** Core interconnect | contracts stay Abstractions | grants coupling |
| `Core/Grants/*` | Kernel Engineer (host/OS boundary R7) | Kernel host / OS | contracts stay Abstractions | A18 grants path |
| `Core/Registry/InstanceRegistryNeuron*` (+ state) | Kernel Engineer (host/OS R7) | Kernel-OS with Workspace/Grants | live instance identity + PrincipalPartition — not Modules/UI | PrincipalPartition |
| `Core/Registry/KindRegistryNeuron*` (+ state) | Modules Engineer | Modules/Kinds with CalculatorKind | leave alongside Kind programs | none |

---

## Library 2b (Architect decision — locked)

**(a) grant-gate publish/discover residual** — keep owner-wide shared catalog (`ILibrary.ForOwner` / tip comment: published artifact catalog). Installs remain principal-tagged + registry via `PrincipalPartition` (tip-shaped). Do **not** add `ForPrincipal` library catalogs (that breaks Discover + deliberate install).

Corpus remains **(b)-shaped**: `ForPrincipal` exists — stage caller flip off `ForOwner`/"main" now (Eng Desk already directed Modules).

---

## Execution order (after Kernel sign)

1. Sign this table (Kernel Engineer reply = sign; Architect amends if Kernel vetoes a row).  
2. One PR per row group (Behavior · CalculatorKind+KindRegistry · Library · Corpus · Repository · Workspace+Grants+InstanceRegistry).  
3. No Seam 3 bulk move parallel to Kernel Chat Actor strip / 2a delivery Principal.

## Sign-off

```text
Architect: SIGNED draft 2026-08-12 — Library 2b=(a); CellNeuron stay / CalculatorKind leave
Kernel Engineer: SIGNED 2026-08-12 — InstanceRegistry → Kernel-OS (with Workspace/Grants R7); KindRegistry → Modules/Kinds with CalculatorKind
Amendments:
- InstanceRegistryNeuron seats with Kernel-OS (identity of live instances + PrincipalPartition), not Modules/UI.
- KindRegistryNeuron leaves with Kinds module alongside CalculatorKind.
- PrincipalGraph/Registry/Grants ambient helpers stay in Core until Workspace/Grants/Registry neurons physically move (call-site follow in same PRs).
- No ISynapseGraph / synapsegraph grain rename in Seam 3.
```
