# 02 — Current System Map

Factual map of the system as built at commit `72400e3e`. Components, ownership boundaries, runtime topology, and the control/data flows that matter for trust. Implemented-vs-aspirational is called out explicitly. Evidence is in the per-subsystem [file-audit/](file-audit/) documents.

## Solution structure (projects)

| Project | Layer | Role |
|---|---|---|
| `src/DigitalBrain.Core` | Shared contracts | Synapse vocabulary, self-evolution + foundry + INO contracts, model catalog, some crypto/rate-limiting (mislocated — `core:ARCH-001`). |
| `src/DigitalBrain.Kernel.Abstractions` | Kernel contracts | `Neuron`/`NeuronJournals` base, `INeuron`, `IConnector`, synapse dispatch. |
| `src/DigitalBrain.Kernel` | Trusted core | Grains, self-evolution rail, Foundry (codegen/exec/sandbox), LLM wiring, INO runtime, auth, hosting, gRPC, config. |
| `src/DigitalBrain.Pack.Contracts` | Extension SDK | Pack authoring DSL; `PackSignatureVerifier`/`PublisherTrust` (unused — `connectors:SEC-405`). |
| `src/DigitalBrain.Ui.Contracts` / `Ui.Runtime` | UI surface | `UiSurface`, widget-tree/RFW-card DTOs, surface runtime. |
| `src/DigitalBrain.Mcp` | Edge / transport | MCP server (`ino_interact` tool), **V2 UI gRPC gateway** (`UiGrpcService`), session authority, surface feed. |
| `src/DigitalBrain.Aspire` | Orchestration lib | Aspire resource/topology helpers. |
| `integrations/DigitalBrain.Google` | Connector | Gmail read/send + Google OAuth. |
| `integrations/DigitalBrain.Salesforce` | Connector | Salesforce read + preview→apply→verify mutation + OAuth (PKCE). |
| `hosts/DigitalBrain.AppHost` / `ServiceDefaults` | Host | Aspire AppHost topology + OTEL/service defaults. |
| `app/` | Client | Flutter client: v2 runtime rail (live), legacy v1 rail (orphaned — `flutter-runtime:ARCH-700`), RFW host, ui_kit, SDK package. |
| `tests/` | Tests | 471 tests; strong on INO/connectors, gaps on neuron/foundry/SDK/UI. |

## Ownership boundaries (who is allowed to do what)

- **Trusted computing base (must be small):** the Orleans silo host (`Program.cs`, hosting extensions), identity/session authority, crypto (checkpoint/state protectors, DataProtection), the self-evolution apply registry, and the INO effect-plan authority. Today this boundary is **blurred**: Core (a "shared" layer) ships HMAC token crypto and a rate limiter (`core:ARCH-001`); the self-evolution rail (a mutation authority) runs as an ordinary grain with no privileged trust distinction (`kernel-runtime:SEC-056`).
- **Edge:** only `DigitalBrain.Mcp` terminates external transport. It hosts the **only server-wired gateway** (`UiGrpcService`) and the MCP tool. The kernel process itself has **no ASP.NET authentication** — all auth lives in Mcp (`kernel-hosting` answer).
- **Connectors:** own provider I/O and OAuth; tokens are stored per-principal via the kernel's encrypted `PackConfigStore`. Provider concerns *mostly* stay in `integrations/*`, but leak into the kernel through hardcoded provider-name checks (`connectors:ARCH-401`).

## Runtime topology (Aspire)

- AppHost composes: the **kernel/Mcp silo** (Orleans server + gRPC edge co-hosted), **Redis** (clustering), **Azure Storage** (clustering/persistence/journaling/blobs), optional **Ollama**, and the **Flutter client** as a separate app. OTEL is wired via ServiceDefaults (`mcp-hosts-build`).
- **Single-replica edge is a pinned SPOF** (`mcp-hosts-build:PROD-501`); the surface feed is a 250 ms per-client poll with 2–4 grain calls per delivered item, sharing a 32-slot semaphore with all unary traffic (`mcp-hosts-build:PERF-500/501`).
- **Toolchain is preview/alpha end to end**: `net11.0`, `aspnet:11.0-preview` base image, CI `dotnet-quality: preview`, Orleans preview + Journaling alpha (`mcp-hosts-build:PROD-500`, `kernel-runtime:FRAME-050`).

## Control/data flow — the live path (INO / V2)

```
Flutter (v2 runtime rail)
  → gRPC-Web → UiGrpcService (Mcp)            [session token auth per RPC; capability action tokens]
    → RuntimeSessionAuthority / RuntimeSurfaceFeed
    → McpInoCommandHandler / ConversationStateClient
      → ConversationNeuron (Runtime)          [encrypted state, principal-scoped]
        → InoEffectPlanNeuron / Authority     [signed effect plan, preview]
          → PlanInoToolGateway                [fail-closed; ClosedInoToolGateway default]
            → InoOperationWorkerGrain         [lease/fence, idempotency, outcome-unknown]
              → Connector (Gmail/Salesforce)  [OAuth on demand, encrypted tokens, preview→apply→verify]
```

This path is coherent and fail-closed. Approvals ride back through `UiGrpcService.SubmitAction` → capability-token authorization → `DecideApprovalAsync`, bound to the authenticated principal.

## Control/data flow — the self-evolution / Foundry path (weak substrate)

```
(caller) → CodeFoundryClosedLoopNeuron.FireAsync(FoundryRequest)   [NO external entry point wires this today]
  → CreateCheckpointAsync            [appends full timeline into journal — ARCH-051/REL-101]
  → CodeGenNeuron (LLM generates C#)
  → StageApplyAsync → SelfEvolutionProposal → SelfEvolutionNeuron
      HandleAsync(SelfEvolutionDecision):
        - checks DecidedBy non-empty ONLY               [SEC-050: forgeable]
        - does NOT read RequiresHumanApproval           [SEC-051]
        - trusts proposer-supplied Risk                 [SEC-052]
        → SelfEvolutionApplyRegistry.ApplyAsync         [allowlisted, fail-closed — the one good part]
            → FoundryRunApplyHandler → CodeRunNeuron
                → ICodeExecutor = InProcessAlcExecutor  [in-process FULL TRUST — SEC-302]
                    → CapabilityGate (static, bypassable, "not a boundary") [SEC-300]
            → FoundryDeployApplyHandler → CodeDeployNeuron [NO gate at all — SEC-308]
```

Additional bypasses of this already-weak path:
- `CodeRunNeuron`/`CodeDeployNeuron` are public grains — a cluster-internal caller can `FireAsync` execution directly, skipping the proposal/approval entirely (`foundry:SEC-304`).
- The automation `ScriptRunner` path (reachable via `AutomationNeuron`) gates against a **zero-reference compilation**, so `CapabilityGate` returns empty and the script then runs with full references (`foundry:SEC-301`).
- `TrustedAutoApply` config flag skips human approval (emits a journaled `AuditBypass`) (`foundry:SEC-303`).

## Implemented vs. aspirational (explicit)

| Capability | Status | Evidence |
|---|---|---|
| INO effect-plan runtime (sign/preview/lease/outcome-unknown) | **Implemented, strong** | `kernel-runtime` positive notes |
| V2 UI transport (session + capability tokens) | **Implemented, strong** | `UiGrpcService` |
| Connector auth/token isolation, OAuth state/replay | **Implemented, strong** | `connectors` |
| Salesforce preview→apply→verify | **Implemented, strong** | `connectors` |
| Self-evolution propose→decide→apply *structure* | **Implemented shape, unenforced substance** | `kernel-runtime:SEC-050/051` |
| Self-evolution apply-handler allowlist | **Implemented, fail-closed** | `kernel-runtime` |
| Foundry out-of-process sandbox | **Placeholder (registered, never invoked)** | `foundry:SEC-302` |
| Foundry in-process execution | **Implemented, unsafe** | `foundry:SEC-153` |
| Checkpoint / rollback | **Aspirational (appends, does not restore)** | `kernel-runtime:ARCH-051` |
| Kernel rolling self-update | **Simulation (logs only, hardcoded replicas, test hook in prod)** | `kernel-runtime:ARCH-050/PROD-101` |
| Pack signing / publisher trust | **Aspirational (code exists, zero callers)** | `connectors:SEC-405` |
| General connector capability model | **Not met (auth-only interface)** | `connectors:ARCH-400` |
| Legacy `DigitalBrainGateway` (raw synapse Send) | **Dead (no server impl; client rail orphaned)** | `kernel-hosting:ARCH-100`, `flutter-runtime:ARCH-700` |
| Second auth authority (`UserSessionNeuron`/`DevAuth`) | **Dead/latent, fail-open** | `kernel-hosting:ARCH-101/SEC-100/101` |
| Bounded/compacted journals | **Missing (unbounded)** | `kernel-runtime:PERF-100/REL-050` |
| GeneratedNeuron Gmail "insights" | **Fabricated sample data presented as analysis** | `kernel-runtime:PROD-100` |
| Mobile/desktop client reachability | **Broken as configured (macOS/Android)** | `platform-and-skills:PROD-1000/1001` |

## The one-paragraph truth

The system that actually runs when a user drives the Flutter app through the V2 rail is a careful, fail-closed, encrypted, principal-scoped orchestration layer over two well-built connectors. Surrounding it is a second, older layer — a raw-synapse gateway, a second identity model, a self-evolution/Foundry engine, and a large body of dead vocabulary — that is where nearly every Critical and High finding lives, and which the product's headline feature unfortunately depends on.
