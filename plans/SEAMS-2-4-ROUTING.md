# Seams 2–4 routing (Architect → Eng Desk)
**Date:** 2026-08-12 · After FINAL Grill PASS R4 / Vlad GO  
**Constraint:** code-native tip names OK; no new product scope; Seam 1 (Chat Actor strip / MCP auth) stays ahead.

## Order
`Seam 1 (live) → Seam 2 A18 → Seam 4 Sdk rails honesty (can overlap late A18) → Seam 3 Core eviction (after classify, not parallel folder thrash)`

Do **not** start Seam 3 folder moves before A18 delivery Principal + Connect refuse are green.

---

## Seam 2 — Principal partition / A18

**What lands (two slices):**

| Slice | Lands | Owner |
|---|---|---|
| **2a Delivery + Connect** | Principal immutable on `SynapseDelivery` / outbox; drain re-enter from delivery Principal; cross-principal `Connect` refuses by default | **Kernel Engineer** (Core + Abstractions shape) |
| **2b Product grain keys** | Principal-scoped product grains already shaped tip-true stay; fix defects: owner-scoped corpus/inbox/registry/graph → principal-scoped **or** grant-gated; MCP slots stay `(serverKey, PrincipalId)` | **Kernel** for graph/registry/inbox grains in Core path; **Modules Engineer** for corpus/library stores; **Integrations** for outbound MCP token slots / webhook subscription identity |
| **2c Northbound MCP parity** (if not finished in Seam 1) | Kill `alice\|bob\|operator` Enter spoof; real `ActorContext` mint → `VerifiedActor.Enter` → Fire | **Kernel** (DigitalBrain.Mcp host) with Integrations review on Sdk auth helpers |

**Done when:**
1. No product path mints Principal from tool string enums.
2. Cross-principal Connect denied without explicit grant path.
3. Owner-scoped stores either partitioned or explicitly grant-gated + listed as residual with owner.
4. Gate green; Product Grill on multi-user smoke (two principals, no cross-bleed).

---

## Seam 3 — Core leakage eviction

**Move list (tip `src/Kernel/DigitalBrain.Core/` — classify then move; contracts may stay Abstractions):**

| Tip folder | Destination owner | Notes |
|---|---|---|
| `Core/Behavior` | Modules / future Behavior host (Modules Engineer + design hold) | Not interconnect; Studio still blocked on host design |
| `Core/Cell` | Modules (Cell/Kinds) or keep thin runtime in Core **only if** Kind apply/snapshot stays grain runtime — Architect default: **Cell grain runtime stays Core**; Kind programs / CalculatorKind → Modules |
| `Core/Library` | Modules (OS library) | |
| `Core/Corpus` | Modules (Memory/OS) | After A18 keys |
| `Core/Repository` | Modules | |
| `Core/Workspace` | Kernel host / OS boundary (R7) — **not** Core interconnect | Neurons leave Core; contracts stay Abstractions |
| `Core/Grants` | Kernel host / OS boundary (R7) | Same |
| `Core/Registry` | Modules or Kernel-OS — **not** interconnect | After A18 |

**Stay in Core:** Neuron/Journal/Outbox/Turn/Pipeline/Concurrency/DeliveryPolicy · `SynapseGraphNeuron` · `ConnectionRelayNeuron` · Broadcast\* · `VerifiedActor` ambient · CapabilityInvocation + `FrameworkInterfaces` allowlist · runtime hooks.

**Done when:**
1. Written classify table PR’d (this list + any tip surprises) **before** bulk move.
2. Each move is one seam PR; Core project references do not pull chat/UI/Salesforce.
3. No opportunistic rename of `ISynapseGraph` / grain `synapsegraph:`.

---

## Seam 4 — Sdk rails ownership honesty

**Owner: Integrations** (platform rails), Kernel host only for callback mint edge.

| Rail | Owner | Done |
|---|---|---|
| `McpServerNeuron` / list+call | Integrations | Live catalog; no parallel module MCP clients |
| OAuth/PKCE + `PrincipalTokenSlot` unify (R10) | Integrations; Kernel `MapOAuthCallback` host-only | One slot model; tokens protected; no journal secrets |
| `WebhookIngressNeuron` + verify | Integrations | Per-subscription ingress; Emit Accepted\|Duplicate\|Conflict; no Core webhook types |
| Durable payload protection | Integrations + Platform/AppHost secrets | StateProtectionKey wired |

**Done when:** Modules call Sdk rails only (no second OAuth/webhook stack); ownership doc matches folder (`DigitalBrain.Sdk` vs module); gate green.

---

## Eng Desk cheat sheet
- Seam 2 → Kernel lead; Integrations on MCP slots/webhooks; Modules on corpus/library stores  
- Seam 4 → Integrations lead; Kernel callback edge only  
- Seam 3 → classify first (Architect signoff on table), then Modules/Kernel-OS movers; Kernel keeps interconnect  
