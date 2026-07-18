# Brain core/kernel migration assessment

**Date:** 2026-06-25
**Source doc:** `Projects/docs/projects-survey-comparison.md`
**Current target:** `brain/` plus `app/`
**Donor/archive area:** `Projects/`

## Position

`Projects/` should now be treated as old source/prototype material. The product target is `brain/` for the kernel and `app/` for the client. The first priority is not more UI, marketplace polish, or another prototype format. The key is a proper core:

1. Typed synapses and neurons are the substrate.
2. Dual journals are the durable truth.
3. Causation/correlation is first-class.
4. Checkpoint/branch/restore is kernel behavior, not a demo.
5. Runtime distribution must install typed C# behavior into the already-running brain.

The current `brain/` implementation has moved past the original survey in several rows, especially Core, Kernel, Foundry, trust, SDK, context, and MCP. The remaining important gap is making installed packs handle real typed synapses as first-class behavior, not only the current `IPackBehavior.Respond(string)` capability triggered through `ExperienceUsed`.

## Implementation progress

**2026-06-25 implementation pass:**

- Started typed pack dispatch v2: `IPackBehavior` now has backward-compatible typed `CanHandle(Synapse)` / `Handle(Synapse)` hooks, and `GeneratedNeuron` dispatches embodied packs through them.
- Added install-to-typed-dispatch proof: a compiled pack can handle `DemoMessageSynapse`, emit `PackEmission`, and preserve host causation/correlation.
- Made unsigned-pack rejection configurable with `DigitalBrain:Marketplace:RejectUnsignedPacks`.
- Made `KernelTaskProgress` real for the default task flow (`planning`, execution mode, `finalizing`).

## Core and kernel first

| Area | Done now | How it works now | Bring next |
|---|---|---|---|
| Typed neuron model | Done | `DigitalBrain.Protocol/INeuron.cs`, `IHandle.cs`, `NeuronId.cs`, and `Synapse.cs` define the typed Orleans grain model. `INeuron` exposes `FireAsync`, `DeliverAsync`, timelines, checkpoints, branch, and restore. | Keep this as the non-negotiable center. Do not re-import INO behavior semantics into Core. |
| Causal synapses | Done | `Synapse` now carries `CorrelationId`, stable `SynapseId`, and `CausationId`. `Synapse.Stamp(...)` flows correlation down a reaction chain and points causation to the immediate predecessor. Tests cover JSON round-trip and marketplace causation. | Add query helpers over journals for causal graph traversal so UI/MCP/debugging do not reimplement this. |
| Dual journals | Done | `DigitalBrain.Silo/Neuron.cs` uses keyed durable lists `in-journal` and `out-journal`. `FireAsync` writes outgoing, `DeliverAsync` writes incoming, and missing journal wiring fails fast. Local/test uses in-memory journals; hosted mode wires Azure Blob journal storage. | Add operational probes for journal health and retention/compaction policy before relying on long-running cloud history. |
| Checkpoint | Done | `CreateCheckpointAsync` deduplicates incoming/outgoing snapshots by `SynapseId`. `CheckpointProtector` serializes polymorphic synapses and optionally encrypts with AES-GCM via `DigitalBrain:Checkpoint:Key`. | Wire production key management through Key Vault and make protected checkpoints the default for any persisted/exported state. |
| Branching | Done | `BranchAsync` creates a new grain of the same concrete Orleans grain type and replays the checkpoint into it. Tests verify same-type branch and source isolation. | Bring final's broader `ForkBrain` semantics only if we need multi-brain/workspace branches. The current same-grain branch is the right kernel primitive. |
| Restore | Done | `RestoreCheckpointAsync` seeds the incoming journal from a checkpoint without redispatching handlers. This separates state restore from branch replay. | Add restore safety checks: source compatibility, snapshot version, and protected-checkpoint verification. |
| Kernel tasks | Partial but usable | `IKernelTask` and `KernelTaskNeuron` create/start/progress/complete/cancel tasks and derive `KernelTaskInfo` from journals. The default task handler now emits basic `KernelTaskProgress` milestones. | Add cooperative cancellation and long-running task resume. This is a core UX feature because tasks are the OS work unit. |
| Reqnroll/kernel tests | Done for current scope | `NeuronCore.feature` covers core scenarios; `CodeFoundry.feature` covers Foundry run/deploy paths. xUnit tests cover causation, checkpoint, branch, restore, signing, install embodiment, SDK, context, UI, and sandbox. | Port final's distribution scenarios as Reqnroll tests around the new typed-C# ALC path, not the old pre-known-grain path. |
| Self-update/restart | Partial | `CodeDeployNeuron` verifies build, writes generated source, calls `IResourceController.RestartSiloAsync`, and emits `SiloRestartRequested`. `IAspireNeuron` records `StartDistributedApp` and `RestartResource`. | Bring final's live Aspire self-update proof. Current code requests restart; it does not yet prove physical restart, rejoin, and post-restart continuation in one test. |

## Current core shape

The current design decision is:

`InstallFromMarketplace` -> `GeneratedNeuron` host -> compile `NeuroPack.Code` -> collectible ALC -> instantiate `IPackBehavior` -> run behavior -> journal `PackEmission`.

That is implemented in:

- `DigitalBrain.Silo/SystemNeurons.cs`
- `DigitalBrain.Silo/Foundry/PackAlcEmbodier.cs`
- `DigitalBrain.Protocol/Distribution/IPackBehavior.cs`
- `DigitalBrain.Tests/UnitTest1.cs`
- `DigitalBrain.Tests/Foundry/PackAlcEmbodierTests.cs`

This differs from the original survey's "register each pack as an Orleans grain" target. The new approach is simpler and matches the user decision in `brain/CONTINUITY.md`: a single host grain owns dynamically compiled capability objects. That is fine for Core as long as the host becomes a real typed synapse dispatcher.

The first core upgrade has started:

```text
IPackBehavior v1: string Respond(string input)
IPackBehavior v2: typed synapse dispatch contract
```

Implemented first step:

- `IPackBehavior` keeps `Respond(string)` for compatibility.
- New packs can override `CanHandle(Synapse)` and `Handle(Synapse)`.
- `GeneratedNeuron` preserves pack identity when emitted `PackEmission` values are journaled.

Still open:

- Add a manifest/contract catalog so a pack declares the exact synapse record types it handles.
- Add upgrade/unload/version replacement tests around typed dispatch.
- Add stronger loop prevention/policy for pack-emitted synapses that target the same handler type.

This is the main "proper core" gap left after the current migration.

## Full table reassessment

| Feature from survey | Current state in `brain/` and `app/` | What to bring next |
|---|---|---|
| Core: typed `INeuron`/`Synapse`/`NeuronId` | Done. Core contracts are in `DigitalBrain.Protocol`. | Keep. Do not fork another abstraction from `Projects/`. |
| Core: `correlationId` + `causationId` | Done. `SynapseId`, `CorrelationId`, and `CausationId` are stamped and tested. | Add journal query APIs for causal graph/debug views. |
| Core: dual incoming/outgoing journals | Done. `Neuron` maintains incoming and outgoing durable journals with fail-fast wiring. | Add journal health, retention, and compaction policies. |
| Kernel: `IKernelTask` with progress | Partial. Lifecycle, status, and basic progress milestones are journal-derived. | Implement cooperative cancellation and resumable long-running work. |
| Kernel: Reqnroll testing/interpreter | Done for core/foundry behavior. `.feature` files are tests/specs, not the runtime behavior language. | Keep Reqnroll as verification. Avoid reviving INO-lang as core behavior. |
| Kernel: checkpoint state snapshot | Done. Snapshot dedup is by `SynapseId`; encrypted protector exists. | Key Vault-backed keys and protected checkpoint persistence/export. |
| Kernel: branching replay into branch | Done. Same concrete grain type is forked and isolated. | Bring `ForkBrain` only when branching needs whole-brain/workspace semantics. |
| Kernel: self-update via Aspire restart | Partial. Restart request path exists; live Aspire restart survival is not fully proved. | Port final's self-update/rejoin test pattern. |
| SDK: typed integration neurons | Done. Git, Shell, FileSystem, DotNet, NuGet, Winget, and Roslyn contracts use typed grain RPC and static-virtual metadata. | Add narrower safety policy per integration and more typed infra agents only when product workflows need them. |
| SDK: `IAspire` abstraction on neurons | Partial. `IAspireNeuron` exists and AppHost has `AddDigitalBrain`, but the neuron is still a thin start/restart journal surface. | Bring richer Aspire resource commands from final/IAW after Core restart semantics are tested. |
| Marketplace: publish/install | Done for local/current kernel. Publish/list/install are journal-driven; install delivers the full `NeuroPack` to the host and runs typed C# if compilable. | Add durable/global marketplace sync only after strict trust is enabled. |
| Marketplace: signing/trust chain | Mostly done. ECDSA P-256 pack signing verifies code hash and rejects tampering. Unsigned-pack rejection is now configurable through `DigitalBrain:Marketplace:RejectUnsignedPacks`; default remains warn-only for local seeds. | Default strict for any remote/untrusted install. Add key identity/trust registry. |
| Marketplace: Google auth/economics | Economics partial/done; Google auth missing. License neuron and premium install gate exist; Stripe gateway is wired behind config; synthetic payment flow is tested. | Bring Google auth identity binding from `digitalbrain` when marketplace users become real accounts. |
| Awesome: engineering-team experience | Done in a better form. `SoftwareEngineeringReviewerNeuron` performs real project/content review and emits typed `ReviewResult`. | Bring richer final scenarios later, but keep them above Core, not inside Core. |
| Awesome: `WingetNeuron` | Done. Current implementation is net-new over shared `ProcessRunner`. | Add OS/platform guards and user approval policy before exposing install/upgrade remotely. |
| Awesome: `IWasm` / WASM sandbox | Missing as WASM. Current repo has `OutOfProcessSandbox` and capability-gated ALC execution. | WASM remains net-new. Defer unless third-party untrusted pack execution requires it. |
| MCP: server exposing neuron tools | Done. Shared `DigitalBrain.Mcp.Tools` exposes real grain tools; standalone stdio and in-silo HTTP reuse the same tool class. | Keep tools honest: no fabricated fallback responses. Add auth before external mutation tools. |
| MCP: HTTP transport, remote reachable | Partial. HTTP MCP is co-hosted inside the silo on port 8081, but intentionally internal-only. | Add ingress/auth/policy if remote MCP is a product requirement. Transport code itself is no longer the blocker. |
| Ino: assistant neuron | Done as assistant. `InoNeuron` uses journals, tasks, branches, skills, and optional LLM. | Keep INO as a user/assistant layer. Do not let it become the core behavior language again. |
| Ino: context graph/vector | Partial-to-good. Journal context, hybrid recall, in-memory vector store, Qdrant backend, and document ingestion exist. Real embeddings and live Qdrant infra are pending. | Bring real embedding provider, Qdrant Aspire resource, and PDF/text-source adapters when context becomes a product focus. |
| UI Kit: typed widgets/server-driven UI | Partial. `UiSurface` samples, `RfwCard`, `HomeFeedBus`, `WatchHomeFeed`, and Flutter panel manager exist. | Reconcile App generated gRPC clients with current Brain proto before adding more UI. Current app expects methods/services Brain does not implement. |
| UI Kit: Chat neuron handles `VisualizeDataRequest` | Done server-side. `ChatNeuron` handles `VisualizeDataRequest`, journals an `RfwCard`, and broadcasts it. | Wire this through app-visible flows and add gateway/client tests for streaming cards. |
| Dist: Roslyn compile of pack code | Done. `PackAlcEmbodier` compiles `NeuroPack.Code`; Foundry run path also compiles generated source. Pack behavior is now typed-synapse-aware while preserving `Respond(string)` compatibility. | Add manifest/contract catalog and policy around emitted synapses. |
| Dist: collectible ALC load/unload | Done with caveat. Unit tests validate compile/load/respond/unload path. Full Orleans activation may retain roots until grain deactivation/GC. | Add operational unload/version-replacement tests around installed pack upgrades. |
| Dist: install -> embody tested e2e | Done. Tests prove publish/install -> generated host -> compiled `IPackBehavior` -> `PackEmission`, including signed pack flow and typed-synapse dispatch with causation preservation. | Convert this into Reqnroll distribution scenarios and add upgrade/unload/version N+1 tests. |
| Self-improve loop tested: generate -> compile -> load -> verify | Partial. Foundry Reqnroll covers generation, run, deploy build, rollback, and restart request. It does not prove physical restart survival or LLM-generated quality. | Bring final's live distribution harness and extend it to the ALC path. |
| Typed C# only, no INO-lang for behavior | Mostly true for Core distribution. Installed compiled behavior is typed C# via `IPackBehavior`. However, repo docs, `start.cs`, app scenarios, and INO editor assets still carry INO/prototype language. | Keep `.feature` as tests/spec descriptions only. Remove or quarantine INO behavior paths from Core docs and runtime claims. |

## App integration note

`app/` is not just old prototype code; it is the current Flutter client. But its generated gRPC surface appears ahead of the current Brain server contract:

- Current Brain proto: `Health`, `Ask`, `Fire`, `Timeline`, `WatchHomeFeed`.
- Flutter generated/client usage also references `Send`, `GetRfwLayout`, `PushFlutterPerf`, `WatchVisualLoadHint`, `BrainWatch`, and `UiGateway`.

That mismatch should not drive Core design. The right order is:

1. Freeze the minimal kernel gateway contract for Core/Kernel.
2. Regenerate the Flutter gRPC clients from the current Brain proto.
3. Reintroduce `BrainWatch`, `UiGateway`, perf streaming, and layout APIs only when the Brain side has real implementations and tests.

## Recommended next build order

1. **Typed pack dispatch hardening.** Add a pack contract catalog/manifest, loop-prevention policy, upgrade/unload/version tests, and Reqnroll scenarios around typed pack dispatch.
2. **Strict trust mode hardening.** Default unsigned rejection to strict for remote/untrusted installs and add author key identity/trust registry.
3. **Kernel task hardening.** Add cooperative cancellation and resumable long-running work.
4. **Distribution Reqnroll suite.** Port final's distribution feature shape, but assert the current path: publish signed typed C# pack -> install -> compile -> ALC -> dispatch typed synapse -> journal response -> upgrade/unload.
5. **Restart survival test.** Prove Foundry deploy/restart/rejoin once under Aspire before expanding self-update promises.
6. **App proto reconciliation.** Bring the Flutter client back into contract with the Brain gateway after Core/Kernel is stable.

## What not to bring back

- A second runtime language for behavior.
- A new marketplace/distribution manifest before `NeuroPack` is exhausted.
- UI-specific kernel shortcuts.
- Old prototype grains that duplicate current `Neuron`, `Synapse`, journal, checkpoint, or pack embodiment primitives.

Use `Projects/` as a quarry for specific proven mechanisms. Do not let it become a second architecture.
