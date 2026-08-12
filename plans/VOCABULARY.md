# DigitalBrain product vocabulary (clarity campaign)

**Status:** Aligned with FINAL-ARCHITECTURE **v1.4** §2 — Contract/Edge/ConnectionGraph. Grill PASS bar A only.  
**Tip baseline:** `stage1-outcome-rail` @ `aa5dfb35`  
**Rule:** Product English is clean **now**. Tip C# names / wire aliases may lag. Bare word **Synapse** is **not** a product synonym for “edge.”

---

## 1. Teaching sentence (Kernel — memorize)

> **Emit/Send Contracts along Edges in the ConnectionGraph** (tip: `ISynapseGraph` / Connections).

| Product term | Meaning | Tip today |
|---|---|---|
| **Neuron** | Logical compute / durable Orleans grain (journal + journal-is-outbox + turns). All graph endpoints are Neurons. | `INeuron`, `Neuron` |
| **Contract** | Typed **fact/payload** on the wire | tip type `Synapse` / `RequestSynapse<T>` + `[Alias]` |
| **ContractAlias** | Stable string id of a Contract type (`ui.note`, `db.connect`, …) | `SynapseAlias` / Orleans `[Alias]` |
| **Edge** (primary) | Durable directed link: Source --[ContractAlias (+ optional Morph)]--> Target | tip `SynapseConnection` |
| **Connection** | **Synonym** for Edge (allowed everywhere) | same tip `SynapseConnection` |
| **Morph** | Optional transform hanging on an **Edge** | `Transform` / `ISynapseTransform` / `to:alias{…}` |
| **ConnectionGraph** | Living graph of **Edges** (product name, RATIFIED) | tip `ISynapseGraph` / `SynapseGraphNeuron` / grain `synapsegraph:…` (tip gloss only) |
| **Cell** | Neuron subtype for Kind-driven apply/snapshot | `ICell : INeuron` |
| **Kind** | Cell program | tip e.g. `CalculatorKind` + KindRegistry |
| **Behavior** | Approved runnable Neuron (C#/worker) — not a Cell Kind | tip `BehaviorNeuron` |
| **Schedule** | Time Neuron that Emits Contracts on ticks | Time `ISchedule` |
| **Integration** | Credentials — never an Edge | OAuth / `PrincipalTokenSlot` |

**Vlad bridge (honest):** Biology “synapse ≈ connection” is right intuitively. We still **do not** overload bare product term **Synapse** = edge (Kernel veto — tip type `Synapse` already means payload; dual use is the mush). Edges are **Edge/Connection**. Payloads are **Contracts**. Graph product name is **ConnectionGraph**. Tip type name `Synapse` / tip `ISynapseGraph` are glosses only — never product nouns without the tip-code cell.

---

## 2. Architect pick: **Contract** = tip `Synapse` (payload)

| Candidate | Verdict |
|---|---|
| **Contract** | **PICK** — already in `fire(contract,…)`, find_capabilities, Contracts assemblies. Disambiguate “wire Contract” vs “Contracts assembly” when needed. |
| Fact | Reject — collides with journal/outcome prose. |
| Impulse | Reject — no tip foothold; sounds ephemeral. |

---

## 3. Rename map

| Product term | Tip type / API today | Wire alias permanence | Migration note |
|---|---|---|---|
| Neuron | `INeuron`, `Neuron` | grain type strings | keep |
| **Contract** | `Synapse`, `RequestSynapse<T>` | per-type `[Alias("…")]` **permanent once data exists** | Product English **now**; C# type rename `Synapse`→`Contract` = later dedicated seam |
| **ContractAlias** | `SynapseAlias`, `[Alias]` | alias strings permanent | rename helpers later |
| **Edge** / **Connection** | `SynapseConnection`; `db.connect` / `db.disconnect` | **`db.synapse-connection`**, `db.connect*` stay until deliberate wire rev | Product English **Edge/Connection now**; do **not** call the edge “Synapse” in product prose |
| Morph | `Transform` on connection | transform strings are data | Morph **on Edge** |
| **ConnectionGraph** | tip `ISynapseGraph` / `SynapseGraphNeuron` | grain family sensitive | product name only; tip API lag |
| Emit/Send/Fire Contract | `EmitAsync` / `SendAsync` / `FireAsync` | — | “emit **Contract** X along **edges**” |
| Cell / Kind / Behavior / Schedule / Integration | as tip | — | see pocket card |
| Route outcome Contract | `RouteOutcome` | `db.route-outcome` permanent | product: outcome Contract |

---

## 4. Phrase discipline

| Forbidden | Required |
|---|---|
| “emit a synapse” (payload sense) | “emit a **Contract**” |
| bare product “Synapse” = edge | “**Edge**” / “**Connection**” |
| “synapse graph” / “SynapseGraph” as product | “**ConnectionGraph**” (tip: `ISynapseGraph`) |
| “morph on a contract” | “Morph on an **Edge**” |
| “Fire a connection” | “Emit/Send/Fire a **Contract**; **edges** route it” |
| Teaching tip “Synapse = message” without saying **tip type** | Always mark tip type names: tip `Synapse` (product: **Contract**) |

**Allowed:** tip-code gloss only — “tip type `Synapse`”, “tip `SynapseConnection`”, “tip `ISynapseGraph`”. **Forbidden as product:** bare Synapse, “synapse graph”.

---

## 5. Emit path (product English)

```text
Neuron Emit(Contract c)
  → receivers = rare [Broadcast] ∪ EdgesFrom(self, ContractAlias.Of(c))   // tip: ConnectionsFrom
  → outbox entries
  → Deliver Contract along each Edge
       (if Edge.Morph: relay Neuron applies Morph → Send adapted Contract)
```

---

## 6. Walkthrough

```text
webhook → ingress Neuron
  → Emit(Contract …)
  → EdgesFrom(ingress, alias)          // tip: ConnectionsFrom
  → Edge ingress --[alias + Morph]--> chat:alice/main
  → outbox → relay? → chat handles Contract ui.note
  → Flutter observes journals
optional Edge → cell:owner/calculator@alerts → Schedule Neuron later Emits Contracts
```

---

## 7. Appendix L3 (fantasy — not present vocabulary)

Future optional code world where C# renames tip `SynapseConnection` → `Synapse` (biology) and tip `Synapse` → `Contract`. **Not** current product English. Do not teach L3 as today’s language.

---

## 8. Grill bar

PASS only if: teaching sentence uses **ConnectionGraph**; zero product “synapse graph” / bare Synapse outside tip-gloss cells; Contract/Edge pocket clean; L3 fantasy only; FINAL §2 matches v1.4.1 (Grill R3 nits).
