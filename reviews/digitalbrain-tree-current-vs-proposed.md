# DigitalBrain — Current Tree vs Proposed Tree

Companion to `reviews/digitalbrain-vs-iaw-architecture-review.md`.

- **Repo:** `E:\brain`
- **Snapshot HEAD:** `2426fca5` (master, 2026-07-16)
- **Baseline product commit:** `d3f7c4b3`
- **Goal:** show the accidental sprawl as it exists today, then the smallest coherent layout after deletion-first simplification.
- **Not implemented** — proposal only.

Legend:

| Mark | Meaning |
|--|--|
| keep | stays, same role |
| rename | same code, clearer product language |
| compress | keep behavior, cut structure/LOC |
| merge | fold into another project/folder |
| delete | remove after caller proof / gate green |

---

## 1. Current (as-is) tree

```
brain/
├── AGENTS.md
├── CLAUDE.md
├── README.md
├── LICENSE
├── Brain.slnx
├── Directory.Build.props
├── Directory.Build.targets
├── Directory.Packages.props
├── nuget.config
├── global.json                    # if present
├── aspire.config.json
├── .mcp.json
├── .lsp.json
├── remove.md                      # Wave A–D cleanup ledger (executed)
│
├── .agents/                       # Aspire skills
├── .codegraph/
├── .github/workflows/
├── .superpowers/                  # residual agent/meta noise
│
├── docs/                          # residual docs (mostly trash candidate)
├── reviews/                       # architecture reviews (this folder)
├── scripts/
├── shared/
├── deploy/
│   └── DigitalBrain.Deploy.csproj
│
├── src/
│   ├── DigitalBrain.Kernel.Contracts/
│   │   ├── Core/                  # INeuron, NeuronId, conversation/OAuth surface, durable Ino contracts
│   │   └── Runtime/               # capabilities, feature grain contracts, web-search neuron, effect plans
│   ├── DigitalBrain.Kernel/
│   │   ├── Capabilities/          # dispatcher, catalog, grants, connection health
│   │   ├── Config/
│   │   ├── Features/              # hub/install grains, draft transitions (~1.8k LOC), run projection
│   │   ├── Hosting/
│   │   ├── Llm/
│   │   ├── Memory/
│   │   └── Runtime/               # AgentFrameworkWorkflowRunner, Ino* workers, effect store, sessions
│   ├── DigitalBrain.Features.Sdk/
│   ├── DigitalBrain.Features.Testing/
│   └── DigitalBrain.Mcp/          # gRPC UI + MCP edge + FeatureAuthoringService + protos
│       └── Protos/
│
├── hosts/
│   ├── DigitalBrain.AppHost/      # Aspire topology
│   │   └── Composition/
│   ├── DigitalBrain.RuntimeHost/  # Orleans kernel silo
│   ├── DigitalBrain.FeatureHost/  # isolated feature worker
│   ├── DigitalBrain.FeatureBuilder/
│   └── DigitalBrain.ServiceDefaults/
│
├── integrations/
│   ├── DigitalBrain.Integrations.Google/
│   ├── DigitalBrain.Integrations.Google.Contracts/
│   ├── DigitalBrain.Integrations.Salesforce/
│   ├── DigitalBrain.Integrations.Salesforce.Contracts/
│   ├── DigitalBrain.Integrations.Web/
│   └── DigitalBrain.Integrations.Web.Contracts/
│
├── features/
│   ├── EmailSummarizer/
│   ├── EmailSummarizer.Tests/     # Reqnroll .feature + steps
│   ├── EnrichSalesforce/
│   └── EnrichSalesforce.Tests/
│
├── tests/
│   ├── DigitalBrain.UnitTests/
│   ├── DigitalBrain.AppHostTests/
│   ├── DigitalBrain.IntegrationContractTests/
│   ├── DigitalBrain.E2ETests/
│   └── DigitalBrain.OrleansTests/
│       ├── Capabilities/
│       ├── Features/
│       ├── Salesforce/
│       ├── TestSupport/
│       └── Legacy/                # ~57 files, ~12.5k LOC — historical shapes
│           ├── Architecture/
│           ├── Capabilities/
│           ├── Core/
│           ├── Features/
│           ├── Integrations/
│           ├── Kernel/
│           ├── Llm/
│           ├── Runtime/
│           ├── Steps/
│           ├── TestSupport/
│           └── Ui/
│
└── app/                           # Flutter product UI
    ├── lib/
    │   ├── core/
    │   ├── digital_brain_ui/
    │   ├── features/
    │   │   ├── activity/
    │   │   ├── catalog/
    │   │   ├── live/
    │   │   ├── releases/
    │   │   ├── shared/
    │   │   └── studio/
    │   ├── grpc/                  # generated protobuf (treat as build output conceptually)
    │   ├── rfw_host/              # includes residual Synapse* naming
    │   ├── runtime/
    │   │   ├── buses/
    │   │   ├── protocol/
    │   │   └── widgets/           # chat, connections, etc.
    │   ├── shell/
    │   ├── telemetry/
    │   ├── theme/
    │   ├── ui_kit/
    │   └── widgets/
    └── test/
        ├── core/
        ├── features/
        ├── goldens/
        ├── grpc/
        ├── runtime/
        ├── shell/
        └── ui_kit/
```

### Current conceptual map (too many overlapping nouns)

```
Client (Flutter / MCP)
  → Edge (DigitalBrain.Mcp)
  → "Ino" conversation/operation path  ──┐
  → Feature hub / installation path    ──┼→ RuntimeHost (Kernel)
  → Capability dispatch                ──┤
  → Effect plan / approval             ──┘
  → FeatureHost (pack execution, isolated)
  → Integrations (Google / Salesforce / Web)
```

Overlapping product words today: **Neuron · Ino · Feature · Capability · Workflow · Integration · Activity**.

---

## 2. Proposed tree (delete-first, one product model)

Authoritative unit: **Feature**  
Supporting product nouns: **capability · connector · effect · activity**  
Dropped product nouns: **INeuron, dual Ino branding, IAW Agent as parallel runtime**

```
brain/
├── README.md
├── CLAUDE.md
├── AGENTS.md                      # keep thin pointer to CLAUDE.md
├── LICENSE
├── Brain.slnx
├── Directory.Build.props
├── Directory.Build.targets
├── Directory.Packages.props
├── nuget.config
├── aspire.config.json
├── .mcp.json                      # developer tools only — not product runtime
├── .github/workflows/
│
├── reviews/                       # living architecture notes (optional; delete when stale)
│
├── src/
│   ├── DigitalBrain.Contracts/    # rename ← Kernel.Contracts
│   │   ├── Identity/              # BrainOwnerId, ActorId, FeatureInstallationId (no INeuron)
│   │   ├── Features/              # feature grain contracts, drafts, runs, grants, publications
│   │   ├── Capabilities/          # capability descriptors, dispatch contracts
│   │   ├── Effects/               # prepared effect / approval / outcome contracts (ex-InoEffect*)
│   │   ├── Connectors/            # OAuth callback shapes, connection health contracts
│   │   └── Chat/                  # conversation surface only if still needed at edge
│   │
│   ├── DigitalBrain.Kernel/       # keep name or rename DigitalBrain.Runtime
│   │   ├── Features/              # hub + installation grains (compressed transitions)
│   │   ├── Capabilities/          # dispatcher, catalog, grants, connection health
│   │   ├── Effects/               # effect plan store, executor, approval gateways wiring
│   │   ├── Operations/            # rename ← Runtime/Ino* worker + outbox (one chat→feature path)
│   │   ├── Memory/                # keep if live
│   │   ├── Llm/                   # single chat-client path; no model-agent zoo
│   │   └── Hosting/
│   │
│   ├── DigitalBrain.Features.Sdk/         # keep — pack author surface
│   ├── DigitalBrain.Features.Testing/     # keep — Reqnroll/helpers for packs
│   └── DigitalBrain.Edge/                 # rename ← DigitalBrain.Mcp
│       ├── Ui/                    # gRPC UI endpoints, sessions
│       ├── Mcp/                   # developer/product MCP if still required
│       ├── Authoring/             # FeatureAuthoringService, build/lifecycle
│       └── Protos/
│
├── hosts/
│   ├── DigitalBrain.AppHost/      # Aspire: kernel, feature-host, edge, storage
│   ├── DigitalBrain.RuntimeHost/  # Orleans kernel
│   ├── DigitalBrain.FeatureHost/  # isolated pack worker — KEEP
│   ├── DigitalBrain.FeatureBuilder/
│   └── DigitalBrain.ServiceDefaults/
│
├── connectors/                    # rename ← integrations (clearer product word)
│   ├── Google/                    # impl
│   ├── Google.Contracts/          # keep split for FeatureHost isolation
│   ├── Salesforce/
│   ├── Salesforce.Contracts/
│   ├── Web/
│   └── Web.Contracts/
│
├── features/                      # installable packs only
│   ├── EmailSummarizer/
│   ├── EmailSummarizer.Tests/
│   ├── EnrichSalesforce/
│   └── EnrichSalesforce.Tests/
│
├── tests/
│   ├── DigitalBrain.UnitTests/
│   ├── DigitalBrain.AppHostTests/
│   ├── DigitalBrain.ContractTests/    # rename ← IntegrationContractTests
│   ├── DigitalBrain.E2ETests/
│   └── DigitalBrain.OrleansTests/     # NO Legacy/ folder
│       ├── Features/
│       ├── Capabilities/
│       ├── Effects/
│       ├── Connectors/
│       └── TestSupport/
│
├── app/                           # Flutter — product surfaces only
│   ├── lib/
│   │   ├── core/
│   │   ├── shell/
│   │   ├── theme/
│   │   ├── ui_kit/
│   │   ├── runtime/               # session, transport, feed
│   │   ├── features/
│   │   │   ├── chat/              # rename/clarify from live + runtime widgets chat
│   │   │   ├── activity/
│   │   │   ├── studio/
│   │   │   ├── catalog/
│   │   │   ├── connections/       # OAuth connections UI (if not under runtime)
│   │   │   └── releases/
│   │   └── grpc/                  # generated — prefer untracked or build step
│   └── test/
│
├── deploy/
└── scripts/                       # only scripts that still pay for themselves
```

### Proposed conceptual map (one spine)

```
Client (Flutter / Edge)
  → Auth / session
  → Operation (chat) OR Feature authoring
  → Capability resolve (server-selected)
  → Feature install run (deterministic pack or bounded extract→invoke)
  → Effect gate (prepared payload + human decision)
  → Connector adapter (OAuth-backed Google / Salesforce / Web)
  → Activity projection
```

No parallel: **IAW Agent runtime · CodeOrchestrator scripts · raw external MCP tools · generic Neuron/Synapse runtime**.

---

## 3. Side-by-side: projects

| Current project | Proposed | Action |
|--|--|--|
| `DigitalBrain.Kernel.Contracts` | `DigitalBrain.Contracts` | rename + re-folder (Features / Capabilities / Effects / Connectors) |
| `DigitalBrain.Kernel` | `DigitalBrain.Kernel` (or `DigitalBrain.Runtime`) | compress Features + Operations; delete empty Neuron surface |
| `DigitalBrain.Mcp` | `DigitalBrain.Edge` | rename; keep gRPC + authoring + optional MCP |
| `DigitalBrain.Features.Sdk` | same | keep |
| `DigitalBrain.Features.Testing` | same | keep |
| `DigitalBrain.RuntimeHost` | same | keep |
| `DigitalBrain.FeatureHost` | same | keep (isolation is product safety) |
| `DigitalBrain.FeatureBuilder` | same | keep |
| `DigitalBrain.AppHost` | same | keep |
| `DigitalBrain.ServiceDefaults` | same | keep |
| `DigitalBrain.Integrations.*` | `connectors/*` | rename folder/product language |
| `DigitalBrain.Integrations.*.Contracts` | `connectors/*.Contracts` | keep split for FeatureHost |
| Feature packs | same under `features/` | keep |
| `tests/.../Legacy` | **gone** | delete after coverage map |
| IAW packages / agents | **not present** | do not add |

---

## 4. Side-by-side: Kernel internals

| Current (`src/DigitalBrain.Kernel*`) | Proposed | Action |
|--|--|--|
| `Contracts/Core/NeuronId.cs` → `INeuron` | delete marker; typed grains only | delete |
| `Contracts/Core/*Ino*` | `Contracts/Effects/*` + `Contracts/Chat/*` | rename |
| `Contracts/Runtime/*` | split into Features / Capabilities / Effects | re-folder |
| `Kernel/Features/*` (huge transitions) | `Kernel/Features/*` compressed | compress |
| `Kernel/Runtime/InoOperationWorkerGrain` | `Kernel/Operations/OperationWorkerGrain` | rename |
| `Kernel/Runtime/AgentFrameworkWorkflowRunner` | `Kernel/Operations/CapabilityWorkflow` (thin) | simplify |
| `Kernel/Runtime/FeatureCapabilityInvoker` | stay under Operations or Features | keep |
| `Kernel/Capabilities/*` | same | keep |
| `Kernel/Memory/*` | same if live | keep |
| `Kernel/Llm/*` model zoo leftovers | single provider config | delete unused |

---

## 5. Side-by-side: tests & UI

| Current | Proposed | Action |
|--|--|--|
| `tests/DigitalBrain.OrleansTests/Legacy/**` | removed | delete |
| 5 test projects | 5 test projects (maybe rename ContractTests) | keep count, cut mass |
| Flutter `features/live` + `runtime/widgets` chat | `features/chat` | clarify |
| Flutter `rfw_host` Synapse* names | neutral names | rename when safe |
| Generated `app/lib/grpc` treated as architecture | build artifact | do not design around it |

---

## 6. Explicit deletions from current tree

Immediate candidates (after proof):

```
tests/DigitalBrain.OrleansTests/Legacy/     # ~12.5k LOC
src/.../INeuron                             # empty extensibility fiction
.superpowers/                               # if unused by product
docs/                                       # if non-living
remove.md                                   # archive or delete after waves settled
```

Do **not** delete without replacement:

```
hosts/DigitalBrain.FeatureHost/
integrations/* (→ connectors/*)
src/.../Features/ (hub, installation, effect rails)
features/* packs + verification tests
app/lib/features/{studio,activity,catalog,...}
```

Do **not** introduce from IAW:

```
src/Agents/
CodeOrchestrator / ScriptGenerator / unsandboxed scripts
LLM wrapper agent swarm
Generic MCP client marketplace as product tools
PersonalAssistant / TaskSupervisor / CheckpointStore
```

---

## 7. Target package / dependency edges (proposed)

```
AppHost
  ├── RuntimeHost ──► Kernel ──► Contracts
  │                    ├──► Features.Sdk
  │                    └──► connectors/* impl ──► connectors/* Contracts ──► Contracts
  ├── FeatureHost ──► Features.Sdk
  │               ──► connectors/* Contracts ──► Contracts
  │               ──► Orleans client only (no Kernel impl)
  ├── Edge (Mcp) ──► Contracts
  │              ──► FeatureBuilder (authoring)
  │              ──► Orleans client
  └── FeatureBuilder

features/* ──► Features.Sdk + connector Contracts only
```

One edge path for product mutation:

`Edge → Operation/Feature → Effect gate → Connector`

---

## 8. Migration checkpoints (tree-shaped)

| Checkpoint | Tree change | Reversible |
|--|--|--|
| A | Document freeze (this file + architecture review) | yes |
| B | Delete `tests/.../Legacy` | git revert |
| C | Delete `INeuron`; keep typed grains | git revert |
| D | Rename Ino* → Effects/Operations folders | rename PR |
| E | Compress draft authoring under `Kernel/Features` | behavioral tests |
| F | Rename `Mcp` → `Edge`, `integrations` → `connectors` | mechanical |
| G | Flutter chat/connections naming cleanup | UI-only |

No checkpoint adds IAW packages or a second Agent product model.

---

## 9. One-page comparison

```
CURRENT                              PROPOSED
-------                              --------
Neuron + Ino + Feature + Workflow    Feature + Capability + Effect + Connector
Kernel.Contracts (Core/Runtime)      Contracts (Identity/Features/Capabilities/Effects/Connectors)
Kernel (Features + Runtime/Ino*)     Kernel (Features + Operations + Effects + Capabilities)
Mcp edge grab-bag                    Edge (Ui / Authoring / Mcp)
integrations/* × 6 projects          connectors/* × 6 (same isolation, clearer name)
OrleansTests/Legacy (~12.5k)         deleted
IAW-style agents/scripts             never added
FeatureHost isolation                FeatureHost isolation (kept)
Flutter: live/studio/activity/...    Flutter: chat/studio/activity/connections/...
```

---

## Related

- Full architecture verdict: `reviews/digitalbrain-vs-iaw-architecture-review.md`
- Cleanup ledger already executed for Waves A–D: `remove.md`
