# DigitalBrain — Execution Plan: Product Shaping, 40% Reduction, Fixes

Date: 2026-07-12. Companion to `docs/architecture-assessment-and-plan.md` (findings + evidence live there). This file is the *executable* plan: an agent iterates through it top-to-bottom, task by task. Every task has verification. Baselines below were measured against branch `v2` on 2026-07-12.

---

## 0. Product definition (decided — do not re-litigate during execution)

**DigitalBrain is an AI-native, multi-agent operating system.** Neurons (Orleans grains) are the agents; Synapses are the messages; Ino is the user-facing orchestrator agent.

Four pillars — everything in the repo must serve one of them, or it gets deleted:

1. **Durable agent kernel.** The INO loop (accept → durable operation → worker → LLM → outbox → feed) and the journaled self-evolution rail. Already production-grade. Protect it.
2. **Ino acts on the world through 2 out-of-box integrations: Gmail and Salesforce.** Read AND write. When a task needs an integration the user has never authenticated, **Ino asks for auth mid-conversation** (OAuth suspend → user authorizes → resume), then continues the task.
3. **Self-evolution: the system creates new behaviors via prompts and via code.** Prompt-behaviors = automation definitions (triggers + reactions). Code-behaviors = foundry-generated / pack C# embodied at runtime. Both flow ONLY through the proposal → human approval → journaled apply rail, signed and sandboxed.
4. **One surface: the server-driven chat shell** (`/chat`, RuntimeShell, gRPC surface feed). No second surface until the pillars above are complete.

**Explicit non-goals (kill on sight):** pack marketplace / Stripe / economics fields, Telegram, vector store / Qdrant (future bet, not now), data-visualization & DB-workbench neurons, demo screens and galleries, any UI not reachable from `/chat`.

**One-line promise (goes into README):** *DigitalBrain is a self-hosted, crash-proof AI operating system where agents durably act on your Gmail and Salesforce and safely evolve their own behaviors — every change proposed, human-approved, journaled, and reversible.*

---

## 1. Ground rules for the executing agent

- Work on a new branch `shape-v3` cut from `v2`. One commit per task ID, message format: `[P<phase>.<task>] <summary>`.
- After EVERY task: `dotnet build` (root) then `dotnet test --logger "console;verbosity=minimal"` from repo root. **Never use `--filter`.** For Flutter tasks additionally `flutter analyze && flutter test` in `app/`.
- A task is done only when: build green, full tests green, acceptance criteria below met, verification command output matches.
- When deleting: delete tests of deleted code in the same commit. Never leave orphaned references.
- **Never delete** anything in the protected list (§6) without stopping and reporting.
- If a verification fails or reality contradicts this plan (file moved, already fixed), do NOT improvise a workaround — record the discrepancy in `docs/execution-log.md` and continue to the next task; batch questions.
- Maintain `docs/execution-log.md`: one line per task — task ID, commit hash, LOC delta, notes.

### LOC accounting (run before Phase 1, after each phase)

```bash
echo "C# src:  $(find src hosts integrations -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*' | xargs cat | wc -l)"
echo "C# test: $(find tests -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*' | xargs cat | wc -l)"
echo "Dart lib: $(find app/lib -name '*.dart' | xargs cat | wc -l)"
echo "Dart test: $(find app/test -name '*.dart' | xargs cat | wc -l)"
```

**Baseline (2026-07-12):** C# src 28,508 · C# test 13,644 · Dart lib 22,467 · Dart test 5,439 · **Total 70,058.**
**Target: ≤ 42,035 total (−40%, −28,023 LOC).** Hard floor: −25% via Tier A+B below (verified-safe + audited). The remaining gap comes from Tier C audits. **Rule: hitting 40% never justifies deleting the live chat path or pillar code. If the audits exhaust and the gap remains, stop and report the true achievable number with evidence.**

---

## 2. Phase 0 — Baseline & safety net

**P0.1** Create branch `shape-v3`; run LOC accounting; confirm build+tests green on untouched tree; write first line of `docs/execution-log.md` with baseline numbers.
*Accept:* log file exists with baseline; CI-green starting point proven.

---

## 3. Phase 1 — DELETE (Tier A: verified safe, ~7–8k LOC)

Each item was verified against the tree on 2026-07-12 (zero callers / zero tracked files / dead-in-code). Still re-verify with the given command before deleting — the tree may have moved.

**P1.1 Ghost .NET directories.** Delete `hosts/DigitalBrain.Telegram.Transport/`, `integrations/DigitalBrain.Telegram/`, `tests/DigitalBrain.Telegram.Tests/`, `tools/DigitalBrain.RuntimeMigration/` (only bin/obj residue, 0 tracked files). Remove the empty `<Folder Name="/tools/">` from `Brain.slnx`.
*Verify first:* `git ls-files hosts/DigitalBrain.Telegram.Transport integrations/DigitalBrain.Telegram tests/DigitalBrain.Telegram.Tests tools/DigitalBrain.RuntimeMigration` → empty.

**P1.2 Empty Flutter features.** Delete `app/lib/features/canvas/`, `app/lib/features/experience/`, `app/lib/features/spike/` (0 dart files each).

**P1.3 surface_demo (911 LOC + test).** `app/lib/features/surface_demo/` is a demo screen. NOTE: `app/lib/main.dart:13` imports `surface_demo_screen.dart` — rewire `main.dart` to boot via `router.dart`/`RuntimeShell` only, then delete the feature and `app/test/features/` tests that cover it (~106 LOC).
*Accept:* app builds, `/chat` renders, no `surface_demo` string anywhere in `app/`.

**P1.4 features/live (1,071 LOC).** Graph visualization painters, unreachable from `/chat`. Delete `app/lib/features/live/`.
*Verify first:* `grep -rln "features/live" app/lib --include='*.dart' | grep -v '^app/lib/features/live'` → empty (except possibly main.dart leftovers from P1.3).

**P1.5 features/brain (255 LOC).** `voice_input.dart`, not routed. Delete unless imported by runtime (`grep -rln "features/brain" app/lib | grep -v '^app/lib/features/brain'` → must be empty).

**P1.6 neuron_vector_logo (526 LOC).** Used only by deleted `features/live/graph/brain_painter.dart` and `ui_kit/ui_button.dart`. Inspect `ui_button.dart`'s usage; replace with a trivial icon/asset, then delete `app/lib/widgets/neuron_vector_logo.dart`.

**P1.7 ino_editor feature (215 LOC) — move, don't blind-delete.** The `*_bus.dart` files are imported by `app/lib/rfw_host/digitalbrain_rfw_library.dart` (live path). Move the buses that `digitalbrain_rfw_library.dart` actually imports into `app/lib/runtime/buses/`, update imports, delete the rest of `app/lib/features/ino_editor/`. After this, `app/lib/features/` must be EMPTY — delete the directory.

**P1.8 Dead packages & config.** In `Directory.Packages.props` delete: `Qdrant.Client`, `Spectre.Console`, `Spectre.Console.Cli`, `Microsoft.Orleans.Persistence.Redis`, `Stripe.net`. Delete the `Stripe.net` PackageReference at `src/DigitalBrain.Kernel/DigitalBrain.Kernel.csproj:44`. Delete the `xai-api-key` parameter in `src/DigitalBrain.Aspire/DigitalBrainBuilderExtensions.cs` (~line 65). Resolve `EnableOrleansDashboard` (defaults true, no backing package): delete the option and the `ORLEANS_DASHBOARD_PORT` wiring in AppHost.
*Verify each first:* `grep -rn "<name>" src hosts integrations --include='*.cs'` → no code usage.

**P1.9 Marketplace economics fields.** Delete `Price`/`CommissionRate` (self-labeled "Legacy economics metadata") from `src/DigitalBrain.Pack.Contracts/NeuroPack.cs` and all references/serialization tests touching them.

**P1.10 Demo/workbench neurons in Kernel (~969 LOC + synapses + tests):**
- `Grains/DataVisualizationNeuron.cs` (342) — also remove its synapse defs in `src/DigitalBrain.Core/Synapse.cs` and the reference from `Grains/GeneratedNeuron.cs`.
- `Grains/DbSupportNeuron.cs` (176) + `src/DigitalBrain.Core/Synapses/DbSynapses.cs` + `tests/.../DbSupportNeuronTests.cs`. Then check `ClosedXML` and `Microsoft.Data.Sqlite` in Kernel.csproj: if their only consumers were DbSupport/TabularData paths, delete packages + `tests/.../TabularData/`.
- `Grains/SystemStatusNeuron.cs` (281), `Grains/KernelTaskNeuron.cs` (84) — zero external references found; re-verify (grain string-keys too: `grep -rn "SystemStatus\|KernelTask" src app --include='*.cs' --include='*.dart'`).
- `Grains/SoftwareEngineeringClosedLoopNeuron.cs` (86) — superseded by Foundry loop; remove its `ApplyVia`/refs in `src/DigitalBrain.Core/SelfEvolution.cs` and `SelfEvolutionContractTests.cs` entries.
*Accept:* kernel boots in AppHost, all remaining tests green.

**P1.11 Ui.Runtime demo code.** Delete `src/DigitalBrain.Ui.Runtime/UiKitGallery.cs` (zero non-test references) and `DbSchemaGraphMapper.cs` + `tests/.../Ui/DbSchemaGraphMapperTests.cs` (its only consumer was the DB workbench killed in P1.10). ⚠️ Before deleting the gallery, run P4/B1's widget-usage audit — if kernel surfaces emit widgets ONLY via the gallery, extract the used builders first.

**P1.12 Meta files.** Delete `skills-lock.json` (referenced nowhere). Delete `tmp/main-test-summary.txt`, add `tmp/` to `.gitignore`. Replace `AGENTS.md` body with a 3-line pointer to `CLAUDE.md` (single canonical WoW doc).

**P1.13 Phase checkpoint.** Run LOC accounting, log delta in `docs/execution-log.md`.

## Phase 1 — DELETE (Tier B: audit-then-delete, targets the big Flutter fat, ~5–7k LOC)

**P1.14 Prune `digitalbrain_rfw_library.dart` (5,392 LOC, single file).**
Audit which RFW widget names the kernel can actually emit: extract the widget-name string set from `src/DigitalBrain.Ui.Runtime/UiSurfaceRuntime.cs`, `src/DigitalBrain.Ui.Contracts/UiSurfaces.cs`, `RfwCard.cs`, and any surface-producing neuron. Cross-reference against widget definitions registered in `digitalbrain_rfw_library.dart`. Delete every widget definition the kernel never emits. Split the survivors into `app/lib/rfw_host/library/<category>.dart` files (≤500 LOC each).
*Accept:* `/chat` renders all real surfaces (run app against local kernel; exercise a conversation); no file in `rfw_host/` >800 LOC.

**P1.15 Prune `ui_kit/` (1,893) and `digital_brain_ui/` (1,353).** Same audit: keep only components transitively reachable from `runtime/`, `rfw_host/` survivors, and `shell/`. Delete the rest plus their tests in `app/test/ui_kit/` (1,106 — expect most to go).

**P1.16 Connector consolidation (net −400…−600).** Extract `ConnectorBase` into `src/DigitalBrain.Kernel.Abstractions/` (or a new `DigitalBrain.Connectors.Abstractions`): pending-pack OAuth lifecycle (`OAuthPendingPackName`, `OAuthPendingLifetime=10min`, `OAuthProcessingLifetime=3min`), `GetMergedScopedValuesAsync`, `HasUsableCredential`, PKCE/state-protector plumbing. Rebase `GoogleConnector`/`GoogleClientFactory` and `SalesforceConnector`/`SalesforceClientFactory` onto it. The `IConnector` XML-doc already promises this ("One base eliminates bespoke per-provider forks").
*Accept:* both connectors' existing tests green; duplicated constants exist in exactly one place (`grep -rn "OAuthPendingLifetime" integrations` → 0 hits, only in base).

**P1.17 Phase checkpoint.** LOC accounting + log. Expected cumulative: −13k…−16k (−20%…−23%).

## Phase 1 — DELETE (Tier C: audits to close the 40% gap)

Run these audits and delete what they prove dead. Do not skip; do not delete without the audit passing.

**P1.18** `src/DigitalBrain.Mcp/RuntimeSurfaceFeed.cs` (743) vs `Kernel.Abstractions/SurfaceFeedNeuron.cs` (593) vs `Mcp/ConversationStateClient.cs` (407): map the call graph; collapse duplicated feed/state projection logic after the P2.3 transport extraction.
**P1.19** `app/lib/digital_brain_ui/` effects (glass/glow/density): if P1.15 left effect variants with a single call site, inline and delete the framework-y indirection.
**P1.20** `src/DigitalBrain.Ui.Contracts/UiSurfaces.cs` (607) + `Core/RuntimeContracts.cs` (426): delete DTOs never constructed outside tests (`grep` each record name across src+app protos).
**P1.21** Kernel `Foundry/` : delete `AzureResourceController.cs` if it's still the lone-TODO stub with no live caller.
**P1.22** Generated protobuf: after P1.14/P1.20, if messages in `ui.proto`/`digitalbrain.proto` are no longer used, remove them and regenerate both C# and Dart stubs (shrinks `app/lib/grpc/` ~2.9k proportionally).
**P1.23 Final Phase-1 checkpoint.** LOC accounting. If total reduction <40%: write the gap analysis (what remains, why it's live) into `docs/execution-log.md` and continue — do NOT delete pillar code to hit the number.

---

## 4. Phase 2 — SECURITY (the two HIGHs — nothing ships past this phase until closed)

**P2.1 Sandbox becomes the only execution path (H1).**
- `src/DigitalBrain.Kernel/Foundry/FoundryServices.cs`: today registers `ISandboxedExecutor → OutOfProcessSandbox` which has ZERO consumers. Route ALL dynamic-code execution (`ScriptRunner`, `InProcessAlcExecutor` call sites, `CodeFoundryClosedLoopNeuron` run path) through `ISandboxedExecutor`.
- Delete `InProcessAlcExecutor` as a production default (keep only if a test explicitly needs it, behind a test-only flag).
- `Foundry/ScriptRunner.cs:65`: remove `catch { /* gate is best effort */ }` — a `CapabilityGate` failure must hard-fail the run.
- `Sandbox/OutOfProcessSandbox.cs`: fix the overstated "OS-level isolation" claim — child `dotnet` process + 30s timeout only. Add: non-inherited env (no secrets passed), constrained working dir, and document the honest boundary in the class header. `CapabilityGate` is demoted to defense-in-depth (its own header already admits the `Type.GetType`+`Activator.CreateInstance` bypass).
*Accept:* `grep -rn "InProcessAlcExecutor" src` → test-only; new test proves a script attempting `Environment.GetEnvironmentVariable` on a secret name inside the sandbox cannot see host secrets; foundry closed-loop tests green.

**P2.2 Enforce signing + rail for pack embodiment (H2 + M3).**
- `src/DigitalBrain.Kernel/Generated/GeneratedPackRuntime.cs`: `Install(NeuroPack)` currently has NO caller and does NO verification. Fix both: (a) `Install` must call `PackSignatureVerifier.VerifyPack` and `PublisherTrust.IsTrusted(pack, allowlist)` (allowlist from config `DigitalBrain:Packs:TrustedPublishers`) and refuse on failure; (b) create `PackInstallApplyHandler : SelfEvolutionApplyHandler` (`ApplyVia=PackInstall`, `MaxRisk=InProcessCode`) so installs are proposals approved on the rail, exactly like automations; register it beside `AutomationDefinitionApplyHandler`.
- Fix namespace lie: `Pack.Contracts/Trust/*` declares `namespace DigitalBrain.Core.Trust` → change to `DigitalBrain.Pack.Contracts.Trust`.
*Accept:* new tests — unsigned pack rejected; signed-untrusted-publisher rejected; signed+trusted pack flows proposal→approve→apply→embodied behavior responds; direct `Install` without decision refused.

**P2.3 Close the rail bypass (M2).** `Foundry/CodeFoundryClosedLoopNeuron.cs:31-42`: replace the `TrustedAutoApply` skip-the-rail path with an **auto-decision**: when the flag is set, still create the `SelfEvolutionProposal`, then immediately record a `SelfEvolutionDecision(Approved, DecidedBy:"trusted-auto-config")` — same journal shape, no second code path. Keep the `AuditBypass` synapse until this lands, then delete it.
*Accept:* with `TrustedAutoApply=true`, the journal contains proposal + decision + apply-result records (test asserts).

---

## 5. Phase 3 — LAYERING & STRUCTURE

**P3.1 Extract `DigitalBrain.Runtime.Transport` (M1).** New classlib. Move from `src/DigitalBrain.Mcp/`: `McpInoCommandHandler`, `AddUiTransport`/`MapUiTransport` (UiHostingExtensions), `UiGrpcService.cs`, `ui.proto` (+gen), `RuntimeSurfaceFeed.cs`, `ConversationStateClient.cs` if shared. `Kernel.csproj` drops its `DigitalBrain.Mcp` ProjectReference (currently line 62); both Kernel and Mcp reference the new lib. Mcp goes back to being a thin host: Program + guards + `McpTools.cs`.
*Accept:* `grep -n "DigitalBrain.Mcp" src/DigitalBrain.Kernel/DigitalBrain.Kernel.csproj` → empty; build+tests green; AppHost runs.

**P3.2 Split the ConversationNeuron twins.** `src/DigitalBrain.Kernel.Abstractions/ConversationNeuron.cs` (1,418) → three files: `ConversationContracts.cs` (records), `IConversationGrains.cs` (interfaces), `ConversationTransitions.cs` (pure state machine). Fix the swapped namespaces: abstractions file wrongly declares `DigitalBrain.Kernel.Runtime`, the Kernel grain file declares `DigitalBrain.Kernel` — align to their assemblies.

**P3.3 Slim `INeuron` (ISP).** Move `CreateCheckpointAsync`, `BranchAsync`, `RestoreCheckpointAsync`, `GetCausalLineageAsync`, `GetSiloIdentityAsync` out of `src/DigitalBrain.Core/INeuron.cs` into `IReplayableNeuron` / `INeuronDiagnostics`. Update `Neuron` base + tests.

**P3.4 Small fixes batch.** `Foundry/IBuildRunner.cs:40` blocking `.Result` → `await`. Add logging to silent `catch {}` at `Grains/SystemStatusNeuron.cs:168,214` (if it survived P1.10 — else skip) and `Grains/DataVisualizationNeuron.cs:93` (ditto). Verify `UserSessionNeuron.cs:41` password-bypass first-sign-in is dev/config-gated; if not, gate it.
**P3.5 Split `Runtime/InoOperationWorkerGrain.cs` (1,199)** into partial files or extracted step-executor classes along its existing workflow-step seams. No behavior change; tests green.

---

## 6. Phase 4 — PRODUCT VERTICALS (the point of everything)

**P4.1 Auth-on-demand (pillar 2 core UX).** Flow: user asks Ino something needing Gmail/Salesforce → gateway detects no usable credential (`HasUsableCredential` in ConnectorBase) → INO operation **suspends** (the ADR's OAuth suspend/resume machinery exists — reuse it) → conversation feed emits an auth-request surface (provider name, scopes, `BeginAuthAsync` URL) → user completes OAuth → `CompleteAuthAsync` (existing, state-protected) → operation **resumes** and finishes the original task.
Work: implement the credential-check + suspend trigger in the tool gateway (P4.2); add an `AuthRequestCard` surface to Ui.Contracts + RFW rendering; E2E test with fake OAuth endpoints (TestKit has `FakeTokenHandler` precedent in Salesforce tests).
*Accept:* integration test — unauthenticated ask → auth surface → simulated callback → original task completes without re-asking.

**P4.2 Open the tool gateway through the rail.** Replace `Runtime/ClosedInoToolGateway.cs` (registered at `Hosting/DigitalBrainOrleansExtensions.cs:84`) with `RailInoToolGateway`:
- Read tools (existing Gmail read, Salesforce read) execute directly — reads are not mutations.
- Write tools stage a `SelfEvolutionProposal` (or a lighter `ActionApproval` on the same journal pattern) rendered in-conversation as an approval card; on approve, execute via the connector, journal the effect receipt; on reject/timeout, report.
- First two write tools: **`gmail.send_draft`** (create draft + send on approval) in `integrations/DigitalBrain.Google/` and **`salesforce.update_record`** in `integrations/DigitalBrain.Salesforce/`.
- Config kill-switch `DigitalBrain:Tools:Enabled=false` restores closed behavior; keep `ClosedInoToolGateway` as that fallback implementation only.
*Accept:* E2E TestKit tests — propose→approve→sent (mocked HTTP asserts exact API call); propose→reject→no HTTP call; idempotent re-delivery does not double-send (reuse INO exactly-once machinery).

**P4.3 Self-evolution: prompt-behaviors.** Automations (define/remove via rail) exist. Add the missing conversational entry: Ino can draft an `AutomationDefinition` from a user request ("watch my inbox for invoices and file them") and stage it as a proposal with a human-readable diff card. Polish `PollTriggerNeuron`/`ScheduleTriggerNeuron` wiring to the two connectors (poll Gmail inbox; poll Salesforce query).
*Accept:* test — chat request → proposal card with the automation definition → approve → trigger fires (fake clock) → reaction runs through the tool gateway (which may itself require approval per P4.2 policy).

**P4.4 Self-evolution: code-behaviors.** With P2.1+P2.2 done, connect the loop: Foundry generates pack code → proposal (risk `InProcessCode` or `KernelRestart`) → approve → `PackInstallApplyHandler` verifies signature+trust → sandboxed embodiment → new behavior live, journaled, rollback-capable. Sign foundry-generated packs with a kernel signing key (config-provided) so the signature rail covers self-generated code too.
*Accept:* E2E — Ino proposes a code behavior, approval applies it, the embodied behavior answers a synapse, rollback restores prior state.

**P4.5 README rewrite.** Replace architecture-manifesto framing with: the one-line promise (§0), the four pillars, a 5-minute quickstart (aspire run → open app → chat → connect Gmail → approve one automation), and a link to this plan. Delete stale claims (marketplace, Telegram, vector store).

---

## 7. Phase 5 — FLUTTER CONSOLIDATION

**P5.1 One state approach.** Standardize on `ChangeNotifier` + buses-as-notifiers (incumbent majority: 11 files vs 2 bloc files). Migrate the 2 `flutter_bloc` usages, remove `flutter_bloc` from `pubspec.yaml`. Relocated `runtime/buses/` (P1.7) conform to the same pattern.
**P5.2 File-size rule.** After P1.14 splits: no handwritten dart file >800 LOC (`surface_protocol.dart` 1,193 and `ino_conversation_view.dart` 887 get split next if touched — don't split preemptively).
*Accept:* `flutter analyze` clean; `flutter test` green; `grep flutter_bloc app/pubspec.yaml` → empty.

---

## 8. Phase 6 — TESTS & CI GATES

**P6.1** Replace all raw `Thread.Sleep`/`Task.Delay` in tests (~19 across Ino* recovery suites, `BroadcastReactivityTests`, `NeuronBroadcastTests`, `LlmResponder*Tests`, `OAuthStateProtectorTests`, `SalesforceReadNeuronContinuationTests`) with the existing `tests/DigitalBrain.Tests/TestSupport/AsyncTestWait.cs` polling helper.
*Accept:* `grep -rn "Thread.Sleep\|Task.Delay" tests --include='*.cs' | grep -v AsyncTestWait` → 0 (or documented exceptions).
**P6.2** Google parity tests: `GoogleOAuthStartNeuronTests` + `GoogleReadNeuronContinuationTests` mirroring the Salesforce suite; plus P4 E2E tests above.
**P6.3** `tests/DigitalBrain.Tests/Mcp/`: request-guard middleware tests + `ino_interact` happy path against TestKit cluster.
**P6.4** Architecture tests (extend `Architecture/`): (a) Kernel references no `OutputType=Exe` project; (b) every `ApplyVia` enum value has a registered apply handler; (c) no `catch {}` without logging under `Foundry/`+`Sandbox/`; (d) every `PackageVersion` in `Directory.Packages.props` is referenced by ≥1 csproj (kills M4 forever).
**P6.5** Final accounting: LOC table before/after per phase in `docs/execution-log.md`; retro paragraph ("which of the 5 steps was skipped?").

---

## 9. Protected list — NEVER delete or "simplify away"

`Kernel/Runtime/` INO loop (ConversationNeuron grain, InoOperationWorkerGrain, outbox dispatcher), `SelfEvolution/` grain+registry, `Kernel.Abstractions/Neuron.cs` base, MCP single-tool surface + security middleware (`McpRequestGuard`, transport boundary, session tokens), `EncryptedPersistentState`/key handling, Core purity (no new Core dependencies), gRPC transport seam (`GrpcUiTransport`, `GrpcClientPort`), TestKit harness, ADR 0001, generated protobuf for live messages, `app/lib/runtime/` + `shell/` + `router.dart`.

---

## 10. Execution order & dependencies

P0 → P1 Tier A (P1.1–P1.13, any order, small commits) → P1 Tier B (P1.14–P1.17; P1.16 independent) → P2 (P2.1 → P2.2 → P2.3) → P3 (P3.1 first; rest any order) → P1 Tier C (P1.18 needs P3.1; P1.22 needs P1.14/P1.20) → P4 (P4.1+P4.2 together → P4.3 → P4.4 → P4.5) → P5 → P6. LOC checkpoint after every phase.
