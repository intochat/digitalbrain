# C1: Concept-Census Cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Delete everything the CoreV3 concept census kills — Execution, the marketplace waves, Corpus, the MCP authorization rail + stub modules, the hand-rolled auth, the Scripting project — with each deletion commit carrying its minimal inline replacement, so `dotnet build -warnaserror` and all three test suites stay green after every task.

**Architecture:** Pure subtraction plus thin seams: a chat turn becomes an awaited grain call; story facts move to the Memory module; auth collapses to a ~150-LOC cookie dev-auth serving exactly the Flutter shell's three endpoints. Core/Neuron internals (outbox/pipeline/broadcast/SynapseGraph) are C2 territory — this plan must NOT touch them beyond deleted-type call sites.

**Spec:** `docs/superpowers/specs/2026-08-18-brain-core-refactor-design.md` (§3 census, §5 C1 row, §6 risks). Fact base: the LOC/consumer sweep of 2026-08-18 (per-area numbers and consumer greps recorded in the session ledger).

## Global Constraints

- `E:\intochat\digitalbrain`, branch `finalv2` (HEAD `beba1d1e`; suites: Aspire 18/18, Simulation 6/6, E2E 2/2). NEVER read or write any path under `C:\Users\`.
- **Gate per task:** `dotnet build DigitalBrain.slnx -warnaserror` → exit 0, then ALL THREE suites green: `dotnet test tests/DigitalBrain.Aspire.Tests -c Debug`, `dotnet test tests/DigitalBrain.Simulation.Tests -c Debug`, `dotnet test tests/DigitalBrain.E2E.Tests -c Debug` (Docker required; ~25s). Timeout 600000 each. A task that cannot reach green does not commit — it reports BLOCKED.
- Deletions use `git rm`; git history is the archive (no parked folders, no commented-out code).
- Rides-along projects (Voice, Introspection, Team, Surface/Button/Diagram) get compile-fix patches ONLY when a deletion breaks them — smallest possible edits, noted in reports.
- Tests that pin intentionally-changing behavior are updated IN THE SAME task, and every changed pin is named in the task report.
- Untouchables in this plan: `Core/Neuron/*` internals (except deleting references to killed types), traffic-journal vocabulary, owner wall/filters, Grants, `CapabilityIndex`, the testing SDK's public API.
- Commits: two `-m` flags, trailer `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`. No suppressions; no meaningless comments.

---

### Task 1: Delete the marketplace waves (Library, Registry, Behavior, Workspace, Repository)

**Files:**
- Delete: `src/Kernel/DigitalBrain.Core/{Library,Registry,Repository,Behavior,Workspace}/` (whole folders; ~1,263 LOC) and `src/Kernel/DigitalBrain.Abstractions/{Library,Registry,Repository,Behavior,Workspace}/` (~624 LOC)
- Modify: `src/Kernel/DigitalBrain.Mcp/` — delete `LibraryBehaviorTools.cs`; **split `RegistryTools.cs`**: `read_chart` and the grant/revoke tools SURVIVE (move to a new `ChartTools.cs`-adjacent file or a renamed `GrantTools.cs` + keep `read_chart` where MCP surface consts point); registry/install tools die. Update `McpSurface.cs` consts accordingly.
- Modify: `src/Kernel/DigitalBrain.Core/Identity/PrincipalRegistry.cs` — DISCOVERY: read it first; it references `IRegistry`. If it only catalogs principals for auth/partitioning, inline what Grants/partitioning actually need (or move the needed remnant into `Identity/`); if it is itself dead post-waves, delete it. Quote your finding.
- Modify: `src/Kernel/DigitalBrain.Core/Neuron/SynapseGraphNeuron.cs` — ONLY the killed-type references (e.g. the `IRegistry.GrainTypeName` special case in `RequireKnownEndpoint`); everything else stays (C2 owns this file).
- Modify: global usings (30 files) — remove `global using` lines for deleted Abstractions namespaces (sed, mirror the phase-1 cell pattern).
- Modify: `tests/*` — any pinned facts referencing deleted concepts (grep first: `grep -rn "ILibrary\|IRegistry\|IBehavior\|IWorkspace\|IRepository" tests/`).

**Interfaces:** Produces a solution with no marketplace-wave symbols. BehaviorNeuron's corpus-append call site dies here (one less migration in Task 3).

- [ ] **Step 1:** Enumerate: `grep -rln "ILibrary\|LibraryNeuron\|IRegistry\b\|InstanceRegistry\|IBehavior\|BehaviorNeuron\|IWorkspace\|WorkspaceNeuron\|IRepository\|RepositoryNeuron" --include="*.cs" src/ tests/ | grep -v obj` — this is your working list; classify each hit delete/patch.
- [ ] **Step 2:** Delete folders; apply the RegistryTools split; resolve PrincipalRegistry per discovery; patch SynapseGraphNeuron + global usings + stragglers.
- [ ] **Step 3:** Straggler grep from Step 1 → zero hits. Build + all three suites green.
- [ ] **Step 4:** Commit: `"Delete the marketplace waves (Library, Registry, Behavior, Workspace, Repository)"`.

---

### Task 2: Delete the Execution module — a chat turn becomes an awaited call

**Files:**
- Delete: `src/Modules/Execution/` (whole module, 89 files / 3,545 LOC) + its two `<Project>` entries in `DigitalBrain.slnx` + its `ProjectReference` in `src/Kernel/DigitalBrain.Kernel/DigitalBrain.Kernel.csproj` and `src/Modules/UI/DigitalBrain.Modules.UI/DigitalBrain.Modules.UI.csproj` (verify with grep) + its assemblies in `src/Kernel/DigitalBrain.Kernel/ProductModules.cs`.
- Modify: `src/Modules/UI/DigitalBrain.Modules.UI/Chat/Chat.cs` (1,077 LOC — the heart of this task) and `ChatTurnWorker.cs`.
- Modify: `src/Modules/UI/DigitalBrain.Modules.UI/UiModule.cs` (worker allow-list registration if Execution-specific).
- Modify: `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/` — any Execution-contract references (`ChatTurnGoal`? read first).

**Interfaces:**
- Consumes: the current turn flow (read `Chat.cs` fully first: EnqueueTurnAsync → TryStartNextAsync → StartExecution → ChatTurnWorker → AttemptSucceeded → ApplyExecutionSnapshotToTurnAsync → TryEmitRespondedAsync).
- Produces: the same OBSERVABLE contract with the Execution middleman gone: `Send` still dedupes by CommandId, still emits `UserMessaged` → `TurnLifecycle(Pending)` → `TurnLifecycle(Running)` → (`Responded` + `TurnLifecycle(Completed)`) or (`TurnLifecycle(Failed/Cancelled)`) — the kernel SSE edge (`MapOwnerCommands.StreamDeltasAsync`) and Flutter depend on exactly these journal footprints; DO NOT change their shapes or order.

**Replacement design (the minimal correct shape):**
- `Chat` keeps its FIFO queue + dedupe. Starting a turn = fire-and-track an awaited call to the `ChatTurnWorker` grain (`InvokeOneWay`-style is NOT enough — the result must come back): `_activeTurn = RunTurnAsync(head)` where `RunTurnAsync` calls a new `IChatTurnWorker.RunAsync(goal)` grain method that performs the current `RunResponderAsync` work and RETURNS the `ChatTurnResult` (or throws). On completion, `Chat` emits `Responded` + lifecycle exactly as `TryEmitRespondedAsync` does today; on exception → `TurnLifecycle(Failed)`; `Cancel` → cancels via a turn-scoped flag the worker polls (grain calls can't be aborted — mirror how the current code cancels, read it) → `TurnLifecycle(Cancelled)`.
- Liveness: the current "execution parks / unstick" machinery dies; a single Orleans grain timer on `Chat` (mirror the existing `IRemindable` usage if present) fails turns exceeding `TurnPolicy`'s budget → `TurnLifecycle(Failed)` with a timeout message.
- `ChatTurnWorker` sheds its Neuron/worker-lease scaffolding ONLY where Execution-specific; it stays a grain (the AI call still runs off the chat's turn).

- [ ] **Step 1 (TDD):** Extend `tests/DigitalBrain.Simulation.Tests` — the fixture gains AI module assemblies + test-mode config + corpus path (`DigitalBrain:AI:Corpus:Path` → `tests/corpus`, which exists with `mvp-chart.feature`) — and add `ChatTurnTests`: drive `GetGrainProxy<IChat>("main").Send(new SendMessage(...))` with the mock-LLM corpus; `JournalWait` for `TurnLifecycle(Completed)` + `Responded` on the chat's Outgoing journal; assert the chart entity holds the scripted points. Run → this test is RED today only if fixture wiring is incomplete — get it GREEN against CURRENT code first (it was phase-3 T6's deferred test; ~the mock + corpus already work). THEN do the deletion and keep it green — that's the safety net for the rewrite.
- [ ] **Step 2:** Delete Execution; rewrite Chat/ChatTurnWorker per the replacement design.
- [ ] **Step 3:** `grep -rn "Execution" --include="*.cs" src/ | grep -v obj | grep -viE "ExecutionContext|DistributedApplicationExecution|ExecutionConfiguration"` → only legitimate Aspire/BCL hits remain. Build + all three suites green (the new ChatTurnTests especially).
- [ ] **Step 4:** Commit: `"Delete the Execution module; a chat turn is an awaited worker call"`.

---

### Task 3: Corpus → Memory (story facts)

**Files:**
- Delete: `src/Kernel/DigitalBrain.Core/Corpus/` (134 LOC) + `src/Kernel/DigitalBrain.Abstractions/Corpus/` (69 LOC).
- Modify: `src/Modules/Memory/Contracts/` — add fact synapses: `StoreFact(CommandId, string Kind, string Text, string? Correlation, DateTimeOffset? At) : RequestSynapse<FactStored>` and `ReadFacts(CommandId, string? Kind, string? Correlation, int Limit) : RequestSynapse<FactsRead>` (mirror the module's existing synapse record style + aliases `memory.store-fact` etc.).
- Modify: `src/Modules/Memory/Memory/` — a `FactMemoryNeuron` (or extend `VectorMemoryNeuron` — read it and choose the smaller change; facts are text-first, embedded only when the generator exists) with the same bounded retention CorpusNeuron had (4096, watermark sequence).
- Modify call sites: `src/Modules/UI/DigitalBrain.Modules.UI/Chat/Chat.cs` (the `chat.responded` append from phase-3 T5 → `StoreFact`), `src/Modules/Time/Time/ScheduleNeuron.cs:254` (→ `StoreFact`), `src/Kernel/DigitalBrain.Mcp/TimeTools.cs` (`read_corpus` tool → `read_memory_facts` over `ReadFacts`; update `McpSurface` const).
- Modify: `docs/JOURNALS.md` (the Corpus row/rules become Memory-facts), `tests/DigitalBrain.Simulation.Tests` (the T5 corpus test asserts via `ReadFacts` now; ChatTurnTests' corpus assertion likewise).

**Interfaces:** Produces the single long-term-memory concept. `ICorpus`/`AppendCorpusEntry`/`ReadCorpus`/`ReadEpisode` cease to exist.

- [ ] **Step 1 (TDD):** Update the sim tests to the `ReadFacts` shape first (RED), implement, GREEN.
- [ ] **Step 2:** Straggler grep `Corpus` → zero product hits. Build + three suites green.
- [ ] **Step 3:** Commit: `"Merge story facts into the Memory module; Corpus dies"`.

---

### Task 4: Delete the MCP authorization rail, the Google/Salesforce stubs, and OAuth hosting

**Files:**
- Delete: `src/Kernel/DigitalBrain.Sdk/Mcp/` (3,157 LOC; KEEP `Sdk/Webhook` + `Sdk/Protection` — verify their consumers first: `DurablePayloadProtectionHosting` is used by AIModule, keep; Webhook — grep consumers, delete if zero).
- Delete: `src/Modules/Google/` and `src/Modules/SalesForce/` (whole modules incl. Aspire.Hosting projects) + slnx entries + `ProductModules.cs` assemblies + `AppHost.cs` module registrations + Kernel/Mcp csproj references.
- Delete: `src/Aspire/DigitalBrain.Aspire.Hosting/OAuth/` (both files) + `src/Kernel/DigitalBrain.Kernel/MapOAuthCallback.cs` + the `WithLocalDevelopmentOAuthCallback` call in `AppHost.cs` (and its supporting members in `DigitalBrainBuilder`/`DigitalBrainHostingExtensions` — the `LocalDevelopmentOAuthCallbackUri` plumbing) + `ProductSurfaceResources.LocalDevelopmentOAuthCallbackUri` + `src/Kernel/DigitalBrain.Abstractions/OAuth/` (12 LOC).
- Modify: `src/Kernel/DigitalBrain.Kernel/Program.cs` (drop `app.MapOAuthCallback()`), `ProductModules.cs`, MCP `ChatTools.cs`/`OwnerSessionJournal.cs` rail references (read them — patch minimally), Tier 1 conformance facts that count module resources.

**Interfaces:** Produces an AppHost of: brain fabric + AI + Memory + UI + Time modules + kernel + mcp. Google/Salesforce return when real.

- [ ] **Step 1:** Consumer greps (`McpAuthorization|IMcp\b|OAuth`) → working list; delete + patch.
- [ ] **Step 2:** Build + three suites green (Tier 1 facts updated for the removed resources — name each changed pin).
- [ ] **Step 3:** Commit: `"Delete the MCP authorization rail, stub modules, and OAuth hosting"`.

---

### Task 5: Auth slim-down — minimal cookie dev-auth

**Files:**
- Delete: `src/Kernel/DigitalBrain.Kernel/Auth/` (1,246 LOC) EXCEPT what the replacement needs.
- Create: `src/Kernel/DigitalBrain.Kernel/Auth/DevCookieAuth.cs` (~150 LOC target): cookie scheme + exactly three endpoints with the SAME request/response shapes the Flutter shell uses — READ `src/Modules/UI/Flutter/core/lib/src/ui_client.dart` (`/auth/me` :106, `/auth/bootstrap` :123, `/auth/login` :147, the `CookieHttpClient` + 401-retry) and the current `AuthHttpMaps`/`AuthMeResponse` DTOs FIRST; the wire contract is the spec (byte-compatible JSON).
- Modify: `Program.cs` (`MapAuth` swap), any `ActorContext`/principal plumbing the kernel maps require (read `HttpActor`/`PrincipalSurface` — keep the minimal actor-resolution the SSE maps and owner-commands path use; the goal is deleting Identity-store machinery, not the actor concept).

**Interfaces:** Produces: same wire contract, ~1,000 fewer LOC. Multi-user auth returns later via ASP.NET Identity when wanted.

- [ ] **Step 1:** Read the shell client + current auth maps; write the replacement; delete the rest.
- [ ] **Step 2:** Build + three suites green. E2E's kernel `/health` is anonymous (unchanged); if any e2e/BDD step logs in, verify it still passes.
- [ ] **Step 3:** Commit: `"Collapse auth to a minimal cookie dev-auth"`.

---

### Task 6: Delete the Scripting project

**Files:**
- Delete: `src/Kernel/DigitalBrain.Scripting/` (whole project) + slnx entry + AppHost registration (the whole `AddProject<Projects.DigitalBrain_Scripting>` chain incl. its explicit-start comment) + `ProductSurfaceResources.Scripting` + the AppHost csproj ProjectReference.
- Modify: `tests/DigitalBrain.Aspire.Tests` — the scripting-resource conformance facts (existence + explicit-start pin) are REMOVED (name them in the report; the explicit-start pin dies with the resource).

- [ ] **Step 1:** Delete + patch; grep `Scripting` → zero product hits (test-SDK mentions of `DigitalBrainScriptHost` are a DIFFERENT symbol — untouched).
- [ ] **Step 2:** Build + three suites green.
- [ ] **Step 3:** Commit: `"Delete the Scripting project and its dev probes"`.

---

### Task 7: Full gates + docs/ledger sync

- [ ] **Step 1:** All three suites + build, recorded with counts/durations. Report the new LOC total (`find src -name "*.cs" -not -path "*/obj/*" -not -path "*/bin/*" | xargs cat | wc -l`) vs the 32,127 baseline.
- [ ] **Step 2:** `docs/JOURNALS.md` final consistency pass; spec cross-reference check (census §3 vs what actually died); update the CoreV3 spec §3 if reality diverged (name divergences).
- [ ] **Step 3:** Commit: `"C1 cleanup complete: docs sync"` (only if files changed).
