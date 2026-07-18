# E:\projects survey — best-of-breed comparison & "what we need"

**Date:** 2026-06-23
**Method:** 7 parallel read-only surveys of every .NET tree under `E:\projects` (ino, final, v4, v3, digitalbrain,
self-improving, IAW) against the target architecture, with the hard constraint **typed C# only — no INO lang**.
**Legend:** ✅ solid + tested · 🟡 partial/prototype · 🔶 stub or untested · ❌ absent.
"current" = the repo we are in (`E:\digitalbraintech`).

## Answer to "is core distribution already tested somewhere?" — YES
- **`final`** — the strongest: `DistributionDynamicHandlers.feature`, **16 Reqnroll scenarios** proving pack →
  publish → install → **N+1 handler growth** in the live cluster, contract-only (no-impl) install, two-account +
  peer install, Aspire self-update. *Embodiment = Orleans grain activation of packed types (source-gen + reflection
  dispatch), NOT dynamic Roslyn compile.*
- **`digitalbrain`** — `MarketplaceInstallTests`: ZIP → unpack → **ECDSA-sign + license** → Roslyn compile (`.ino`→C#)
  → register → Orleans activation. Tested, with Stripe checkout. *But INO-centric and in-proc (no ALC).*
- **`v3`** — `CreatorSimulation`/Gate: parse → transpile → Roslyn compile → **Collectible ALC** → run the pack's own
  embedded scenario → `NeuronActivated`. Tested **in-memory** (no marketplace/signing/persistence).
- **`v4`** — `CollectibleAssemblyLoadContextTests` (compile→load→**unload+GC**), `InterpretedNeuron` hot-reload, signed
  `BundleRegistry`. *Units tested; the full pack→download→compile→ALC→dispatch chain is NOT tested end-to-end.*
- **`ino`** — `L1LoopAcceptanceTests`: missed-intent → Roslyn `CSharpScript` → registry → plan grain, **no restart**.
  Tested; marketplace install itself = Aspire silo restart (binary rebuilt outside the cluster).
- **`self-improving`** — N+1 install gate tested; the generate→compile→load→verify loop exists but is **not** in the
  gate (the `Simulation.feature` that would cover it is `@ignore`).
- **`IAW`** — generates C# and runs it **via shell**; no in-cluster ALC embodiment (distribution untested).

## Master feature matrix

| Feature | current | final | digitalbrain | v4 | v3 | ino | self-impr | IAW |
|---|---|---|---|---|---|---|---|---|
| Core: typed INeuron/Synapse/NeuronId | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| Core: correlationId + causationId | 🟡 | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| Core: dual incoming/outgoing journals | ✅ | ✅ | ✅ | 🟡 | ❌ | ✅ | ✅ | ✅ |
| Kernel: IKernelTask { progress } | ✅ | ✅ | 🟡 | 🟡 | ❌ | 🟡 | 🟡 | 🟡 |
| Kernel: Reqnroll testing/interpreter | ✅ | ✅ | ✅ | ✅ | ✅(xUnit) | ✅ | ✅ | ✅(xUnit) |
| Kernel: Checkpoint (state snapshot) | ✅ | ✅ | ✅(encrypted) | 🟡 | ❌ | 🟡 | 🟡 | 🟡 |
| Kernel: Branching (replay into branch) | ✅ | ✅(ForkBrain) | 🟡 | ❌ | ❌ | ❌ | ❌ | ❌ |
| Kernel: Self-update (Aspire restart) | 🟡 | ✅ | ✅ | 🟡 | ❌ | 🟡 | 🟡 | ✅ |
| SDK: typed integration neurons | 🟡 | 🟡 | ✅ | 🔶(empty) | 🟡 | 🟡 | 🟡 | **✅✅** |
| SDK: IAspire abstraction on neurons | 🟡 | ✅ | ✅ | 🟡 | ❌ | 🟡 | 🟡 | ✅ |
| Marketplace: publish/install | 🔶(stub) | **✅✅** | ✅ | ✅ | ❌ | 🟡 | ✅ | ❌ |
| Marketplace: signing / trust chain | ❌ | ✅(Ed25519) | ✅(ECDSA+license) | ✅ | ❌ | ❌ | 🟡 | ❌ |
| Marketplace: Google-auth / economics | ❌ | 🟡 | ✅(Stripe+license) | 🟡 | ❌ | ❌ | ❌ | ❌ |
| Awesome: engineering-team experience | 🟡 | ✅(typed+tested) | ❌ | 🟡 | ❌ | 🟡(travel) | ❌ | 🟡 |
| Awesome: WingetNeuron | ❌ | ❌ | 🟡(process) | 🟡 | ❌ | ❌ | ❌ | 🟡(IShell) |
| Awesome: IWasm / WASM sandbox | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| MCP: server exposing neuron tools | ✅(stdio) | ✅(stdio) | 🟡 | ❌ | ❌ | 🔶 | ❌ | ✅ |
| MCP: HTTP transport (remote-reachable) | ❌ | ❌ | 🟡(gRPC) | ❌ | ❌ | ❌ | ❌ | **✅✅** |
| Ino: assistant neuron | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ | 🟡 | ✅ |
| Ino: Context = graph and/or vector | 🟡 | ❌ | 🟡 | 🟡 | ❌ | ❌ | ❌ | **✅✅(Qdrant+RAG)** |
| UI Kit: typed widgets / server-driven UI | 🟡 | 🟡 | **✅✅(RFW+gRPC)** | 🔶 | ❌ | 🟡 | 🟡 | ✅(IUISession) |
| UI Kit: Chat neuron (IHandle<Visualize…>) | 🟡 | 🟡 | ✅ | ❌ | ❌ | 🟡 | 🟡 | 🟡 |
| Dist: Roslyn compile of pack code | ✅(Foundry) | ❌(serialize) | ✅ | ✅ | ✅ | ✅ | 🟡 | 🟡(validate) |
| Dist: Collectible ALC load/unload | 🟡 | ❌ | 🔶 | **✅✅(tested)** | ✅ | 🔶 | ❌ | ❌ |
| **Dist: install→embody TESTED e2e** | 🔶 | ✅(grain-activate) | ✅(.ino, in-proc) | 🔶(units only) | ✅(in-memory) | 🟡(L1) | 🔶 | ❌ |
| Self-improve loop tested (gen→compile→load→verify) | 🔶 | 🟡 | 🟡 | 🟡 | ✅ | ✅ | 🔶 | 🟡 |
| **Typed C# only (no INO-lang for behavior)** | 🔶(INO-leaning) | ✅(INO optional) | ❌(INO-centric) | ✅(InoLang=transpiler) | ✅(INO=2nd notation) | ✅ | ✅(INO=rules only) | ✅✅(pure typed) |

## Best-of-breed per layer (where to harvest from)
- **Purest typed-C# model (the constraint):** **IAW** (`IAgent` static-virtual metadata; typed `IFileSystem/IAspire/IShell/IGit/IRoslyn/IDotNet/INuGet`), then **final**, **v3**, **self-improving**.
- **Core + journals + N+1 dispatch (source-gen):** **final** / **self-improving** (shared `DigitalBrain.Protocol`).
- **Checkpoint + Branching:** **current** (`CreateCheckpointAsync`/`BranchAsync`) and **final** (`ForkBrain`); encrypted checkpoint in **digitalbrain**.
- **Typed integration SDK neurons:** **IAW** (clear winner) — 9 typed infra agents, compiler-verified, zero-reflection dispatch.
- **Marketplace trust + economics:** **digitalbrain** (ECDSA + license + Stripe, tested) and **final** (Ed25519, 16 scenarios, peer/global sync).
- **Embodiment engine (typed C# → running grain):** **v4** `CollectibleAssemblyLoadContext` (compile→load→unload) + **v3** Gate (compile→ALC→run embedded scenario→`NeuronActivated`).
- **Most-tested live distribution:** **final** (16 Reqnroll scenarios; N+1 proof).
- **MCP HTTP (remote/deployable):** **IAW** (production HTTP transport) — directly answers the original 2B.
- **Context graph/vector:** **IAW** (Qdrant + RAG context providers).
- **Server-driven UI:** **digitalbrain** (RFW + bidirectional gRPC `UiGateway` + perf hints).
- **Live-substrate test harness:** **v3** (`Simulation` boots a real silo, fires real synapses — no mocks).

## The gap — what NObody has end-to-end
A single **typed-C#** pipeline, **tested as one chain**:
> publish typed-C# pack (signed) → install into the **already-running** cluster → Roslyn compile → **Collectible ALC**
> load → register as Orleans grain → dispatch a synapse → assert response in the timeline → unload on new version.

- `final` proves install + N+1 + self-update but **activates pre-known grain types** (no dynamic compile).
- `v4` has the ALC compile/load/unload **as units** but never chains them through marketplace install.
- `v3` chains compile→ALC→run but **in-memory only** (no marketplace/signing/persistence/cluster).
- `digitalbrain` chains compile→register **but via `.ino`, in-proc (no ALC), INO-centric** — violates the constraint.
- **WASM/`IWasm` sandboxed embodiment: absent everywhere** — net-new if sandboxing is required.

## What we need (assemble from best-of-breed, typed-C# only)
1. **Keep `current` Core/Kernel** (typed Protocol, journals, `CreateCheckpoint`/`Branch`, Pass-2A in-silo gRPC) as the base; backfill `correlationId/causationId` from `final`.
2. **Embodiment engine:** lift **v4 `CollectibleAssemblyLoadContext`** + **v3 Gate** flow; wire it to marketplace install so `InstallFromMarketplace` actually compiles+loads `pack.Code` (the "add-back-10%" the Algorithm-pass doc named) — replacing the current stub.
3. **Marketplace trust/economics:** lift **final** (Ed25519) or **digitalbrain** (ECDSA+Stripe+license) signing/license.
4. **Tested chain:** port **final's** Reqnroll distribution harness, extended with the dynamic-compile+ALC assertions from **v3/v4**, to prove the full chain end-to-end.
5. **Typed SDK neurons:** adopt **IAW's** typed integration neuron pattern (`IFileSystem/IAspire/IShell/IRoslyn/...`) for the SDK layer.
6. **MCP remote:** lift **IAW's** HTTP MCP transport (this is the original 2B, solved).
7. **Context + UI:** harvest **IAW** (Qdrant/RAG Context) and **digitalbrain** (RFW+gRPC UI) when those layers come up.
8. **WASM:** net-new, deferred unless sandboxing is a hard requirement.

## Recommended next concrete build (smallest tested vertical, typed-C# only)
> **"Install a signed typed-C# pack and watch it embody + handle a synapse in the running silo."**
> Port **v4's CollectibleALC** + **v3's compile→load→run** into `current`; wire `InstallFromMarketplace` → compile
> `pack.Code` (C#) → ALC load → register Orleans grain → fire a synapse the pack handles → assert the journal shows its
> response. Reuse **final's** Reqnroll distribution feature as the test template. No `.feature`/INO-lang in the pack — the
> pack *is* C#. This single vertical closes the one gap no tree has and makes the marketplace real.

## Out of scope / deferred
WASM sandbox, Telegram experience, miniapp, Context graph/vector, server-driven UI, marketplace economics polish —
harvested later from the trees named above once the embodiment chain is real and tested.
