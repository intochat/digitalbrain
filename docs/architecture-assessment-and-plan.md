# Architecture Assessment & Improvement Plan — 2026-07-12

Full-repo assessment (backend, Flutter client, integrations, tests, product shaping), run through the 5 steps. Every claim below was verified against the tree on branch `v2`. This doc replaces itself: execute, then delete or trim to a short retro.

---

## 1. Verdict

**The product is one thing done extremely well, dressed as ten things.**

The durable INO conversation loop (accept → durable operation → worker → LLM → outbox → feed; ADR 0001) is production-grade: idempotent, lease-fenced, journal-rebuilt, heavily tested. The self-evolution rail (proposal → decision → risk-checked apply → rollback) is coherent. The MCP surface is a single disciplined `ino_interact` tool. Core is pure and acyclic. Debt markers are near zero (1 TODO in all of src, no `async void`, no blocking waits in real paths).

Around that crown jewel sits scaffolding that is half-wired, gated off, or dead — and two of the "dead" pieces are the *security* pieces. The gap between the north-star narrative ("self-evolving OS with pack marketplace") and what runs ("crash-proof AI chat that reads Gmail/Salesforce but cannot act") is the main product problem. The dead safety paths are the main engineering problem.

---

## 2. What's wrong — findings by severity

### HIGH (security-relevant, fix before anything else)

**H1. Pack/foundry code executes in-process behind a self-admittedly bypassable gate; the real sandbox is dead code.**
`PackAlcEmbodier` compiles arbitrary pack C# into a collectible ALC **inside the kernel silo**. The only guard, `Foundry/CapabilityGate.cs`, documents itself as "a guardrail against accidental misuse, not a security boundary" and lists a **confirmed bypass** (`Type.GetType` + `Activator.CreateInstance`). `ScriptRunner.cs:65` swallows gate exceptions in `catch { /* gate is best effort */ }` and runs anyway. Meanwhile `ISandboxedExecutor`/`OutOfProcessSandbox` is registered in DI (`Foundry/FoundryServices.cs:13`) and **has zero consumers** — verified by grep. The safer path exists and nothing uses it.

**H2. Pack signing/publisher trust is implemented but never invoked.**
`Pack.Contracts/Trust/PackSignatureVerifier.cs` (ECDSA-P256) and `PublisherTrust.IsTrusted` have **zero callers** in src/hosts/integrations (verified). `GeneratedPackRuntime.Install` → `Embody(pack.Name, pack.Code)` compiles and loads with no signature or allowlist check. The trust rail is decorative. (Mitigated only because Install itself is unreachable today — see M3 — which is its own bug.)

### MEDIUM

**M1. Kernel silo references the MCP server *Exe* project (inverted layering).**
`src/DigitalBrain.Kernel/DigitalBrain.Kernel.csproj:62` references `DigitalBrain.Mcp.csproj` solely for `McpInoCommandHandler` and `AddUiTransport`/`MapUiTransport` (`Kernel/Program.cs:3,24,26,48`). The whole MCP host surface (JWT, Orleans client config, ui.proto gRPC) is pulled into the silo build, and `UiGrpcService` lives in the Mcp project while actually being hosted in the kernel. Shared transport/handler code belongs in a neutral library.

**M2. `TrustedAutoApply` config flag bypasses the human-approval rail.**
`Foundry/CodeFoundryClosedLoopNeuron.cs:31-42`: with `DigitalBrain:Foundry:TrustedAutoApply=true`, foundry results apply immediately, skipping proposal/decision. It's audited (`AuditBypass` synapse) and off by default, but it turns the sacred invariant into a one-flag toggle. Also, the rail is a *convention at the edges*: direct `FireAsync` on grains mutates without proposals (`AutomationNeuron.cs:281` comment acknowledges this).

**M3. Pack embodiment pipeline is unreachable — a half-wired flagship feature.**
`GeneratedPackRuntime.Install(NeuroPack)` is the only setter of the embodied pack and **has no caller** (verified). `GeneratedNeuron` always falls through to the LLM path. Roslyn embodiment + CapabilityGate-for-packs is instantiated but effectively dead in production.

**M4. Speculative/dead dependencies contradict the delete-first ethos.**
Verified unused in code: `Qdrant.Client` (no csproj ref, no usage), `Spectre.Console(.Cli)`, `Microsoft.Orleans.Persistence.Redis`. `Stripe.net` is *referenced* by Kernel.csproj:44 yet its only trace in code is the string `"noreply@stripe.com"` in `GeneratedNeuron.cs:335`. `xai-api-key` Aspire parameter has no backing model. `EnableOrleansDashboard` defaults true with no dashboard package referenced.

**M5. OAuth/credential plumbing copy-pasted across Google and Salesforce.**
`IConnector` is a genuinely shared contract, but `GoogleClientFactory` / `SalesforceClientFactory` duplicate constants (`OAuthPendingLifetime`, `OAuthProcessingLifetime`), `GetMergedScopedValuesAsync`, `HasUsableCredential`, and mirror-image BeginAuth/CompleteAuth bodies — hundreds of duplicated lines. The `IConnector` XML-doc itself promises "one base eliminates bespoke per-provider forks" — not realized.

**M6. Test asymmetry and flake risk.**
Google's API client is tested (`tests/DigitalBrain.Tests/Integrations/GoogleGmailApiClientTests.cs`) but there are no Google neuron/OAuth-continuation tests equivalent to Salesforce's. ~19 raw `Thread.Sleep`/`Task.Delay` sit in reactive/recovery tests (Ino* suite, Broadcast/LlmResponder tests) even though `TestSupport/AsyncTestWait.cs` exists. `DigitalBrain.Mcp` has no dedicated test folder.

**M7. Doc/meta duplication and committed litter.**
`AGENTS.md` and `CLAUDE.md` are byte-near-identical (9,193 bytes, 4 lines differ) — they *will* drift. `skills-lock.json` (29KB) is referenced nowhere. `tmp/main-test-summary.txt` is a committed test-run artifact.

**M8. Flutter client inconsistencies.**
`app/lib/rfw_host/digitalbrain_rfw_library.dart` is a **5,392-line single file**. Three coexisting state approaches: `flutter_bloc` (2 files), `ChangeNotifier` (11 files), hand-rolled buses (`features/ino_editor/*_bus.dart`). Test:lib ratio ~24% by LOC.

### LOW

- **Ghost projects/dirs**: `hosts/DigitalBrain.Telegram.Transport`, `integrations/DigitalBrain.Telegram`, `tests/DigitalBrain.Telegram.Tests`, `tools/DigitalBrain.RuntimeMigration` — zero tracked files, only bin/obj residue; `Brain.slnx` `/tools/` folder empty. Flutter: `features/canvas`, `features/experience`, `features/spike` — zero dart files.
- **God-files**: `Kernel.Abstractions/ConversationNeuron.cs` 1,418 lines (contracts + state machine mega-file) confusingly twinned with `Kernel/Runtime/ConversationNeuron.cs` (the grain) under *swapped* namespaces (`Kernel.Runtime` vs `Kernel`); `InoOperationWorkerGrain.cs` 1,199; `UiSurfaceRuntime.cs` 850; `RuntimeSurfaceFeed.cs` 743.
- Fat `INeuron` (11 methods incl. test-only `GetSiloIdentityAsync`, checkpoint/branch APIs forced on every grain). Trust types declare `namespace DigitalBrain.Core.Trust` inside the Pack.Contracts assembly. `IBuildRunner.cs:40` blocking `.Result`. Silent `catch {}` in `DataVisualizationNeuron.cs:93`, `SystemStatusNeuron.cs:168,214`. Dev password-bypass first sign-in in `UserSessionNeuron.cs:41` (verify dev-gating).

---

## 3. Product shaping

**No stated user exists anywhere.** README/AGENTS/CLAUDE describe architecture and process; the ADR literally records "No personal names were supplied." The product is defined by mechanism, not by anyone who uses it.

**Surface inventory vs maturity** (from code, not docs):

| Surface | Reality |
|---|---|
| Durable INO chat loop | Working, tested — the moat |
| Self-evolution rail | Working but narrow: only 4 apply handlers (automation define/remove, foundry run/deploy). No pack-install handler. |
| Flutter runtime shell | Working; exactly one route (`/chat`) |
| MCP server | One tool (`ino_interact`) — deliberate, good |
| Gmail / Salesforce | OAuth + **read-only**; agent cannot write anywhere |
| INO external actions | **Hard-closed**: only registered gateway is `ClosedInoToolGateway` — every mutation returns "No external action was performed" |
| Foundry (self-writing code) | Research bet gated behind `TrustedAutoApply` |
| Packs / marketplace / Stripe | Engine half-wired (M3), no install flow, no store, economics fields self-labeled "legacy" |
| Vector store / memory | **Does not exist.** Embedding models registered, nothing stores or queries them. The product is called DigitalBrain and has no memory layer. |

**The central contradiction:** the north star is "an agent that safely acts," but the tool gateway guarantees nothing external ever happens. Today's honest one-liner is: *"A locally-hostable, crash-proof AI assistant that reads your Gmail/Salesforce and can safely automate reactions with human approval."* That is a real, shippable, smaller product.

**Shaping calls:**

1. **Focus the narrative on the rail** — "the most durable, recoverable, human-governed AI agent runtime" — not "self-evolving OS marketplace."
2. **Pick ONE real write action** (e.g. send a Gmail draft) and drive it end-to-end through proposal → approval → apply → journaled effect. This single vertical validates the entire thesis, which is currently untested against any real provider.
3. **Kill the marketplace fiction**: delete `Price`/`CommissionRate`, drop Stripe.net, remove marketplace framing until one real installable pack exists.
4. **Quarantine the foundry** as an internal research spike, not a product surface. "The OS rewrites its own code" before "the agent can send one email" is inverted priority.
5. **Memory is the biggest name-vs-substance gap.** Either build a minimal retrieval layer (after the write-action vertical) or stop implying it.
6. **Write the one-line user promise** into README before adding any feature: *For [who], DigitalBrain [does what job] that [alternative] can't because [durable/recoverable/governed].*

---

## 4. The plan (5 steps, in order)

### Step 1 — Make requirements less dumb (question)

- Requirement "packs are signed C# embodied at runtime" → today unreachable AND unverified. Either it's a requirement (then H1/H2 are P0) or it isn't yet (then delete the dead half-wiring and keep only contracts). Decide explicitly; owner: Vlad.
- Requirement "marketplace economics" → traced to nobody. Delete (see shaping call 3).
- Requirement "agent acts on the world" → the actual point of the system; currently negated by `ClosedInoToolGateway`. Promote to the top requirement.
- Requirement "Telegram / runtime migration" → already deleted in history; finish the deletion (dirs remain).

### Step 2 — Delete (target well over 10%)

All verified-safe deletions; each is a small commit:

1. Remove ghost dirs: `hosts/DigitalBrain.Telegram.Transport`, `integrations/DigitalBrain.Telegram`, `tests/DigitalBrain.Telegram.Tests`, `tools/DigitalBrain.RuntimeMigration`; drop empty `/tools/` folder from `Brain.slnx`.
2. Remove empty Flutter features `canvas`, `experience`, `spike`; decide kill-or-widgetbook for `surface_demo`, `ino_editor` buses, `live/graph` (none reachable from `/chat`).
3. `Directory.Packages.props`: delete `Qdrant.Client`, `Spectre.Console(.Cli)`, `Microsoft.Orleans.Persistence.Redis`, `Stripe.net` (+ its Kernel.csproj:44 reference); delete `xai-api-key` param in `DigitalBrainBuilderExtensions.cs:65`; fix or delete `EnableOrleansDashboard` (option-on with no package).
4. Delete `NeuroPack.Price`/`CommissionRate` legacy fields.
5. Delete `skills-lock.json`, `tmp/main-test-summary.txt`; gitignore `tmp/`.
6. Collapse `AGENTS.md` into a 3-line pointer to `CLAUDE.md` (or vice versa) — one canonical WoW doc.
7. Decision fork on packs: if pack embodiment is deferred, delete `GeneratedPackRuntime`/embodier wiring from the live path and keep `Pack.Contracts` + trust code as contracts-only (removes the H1 attack surface for packs at zero feature cost, since the path is unreachable anyway).

### Step 3 — Simplify (what remains)

1. **Fix H1 for the surviving dynamic-code path (foundry):** route all execution through `ISandboxedExecutor`; delete `InProcessAlcExecutor` as the default; remove the `catch { }` gate-swallow in `ScriptRunner.cs:65` (gate failure = hard fail); harden `OutOfProcessSandbox` honestly (child process + timeout is not "OS-level isolation" — say so, or add real confinement) and demote `CapabilityGate` to defense-in-depth only.
2. **Fix H2 if packs survive Step 2:** `Install` must call `PackSignatureVerifier.VerifyPack` + `PublisherTrust.IsTrusted` before `Embody`, and pack-install must get a `SelfEvolutionApplyHandler` so installs flow through the rail like everything else.
3. **Fix M1:** extract `McpInoCommandHandler`, `AddUiTransport`/`MapUiTransport`, `UiGrpcService`, `ui.proto` into `DigitalBrain.Runtime.Transport` (classlib); Kernel and Mcp both reference it; Kernel drops its Exe reference.
4. **Fix M2:** replace the `TrustedAutoApply` full bypass with an auto-*decision* (a pre-authorized `SelfEvolutionDecision` recorded on the rail) so even trusted flows produce the same journal shape; keep `AuditBypass` until then.
5. **Fix M5:** extract `ConnectorBase` (pending-pack lifecycle, merged scoped values, PKCE/state protection); Google and Salesforce shrink to descriptors + API specifics.
6. Split `Kernel.Abstractions/ConversationNeuron.cs` (records / grain interfaces / `ConversationTransitions`) and fix the swapped namespaces between the two `ConversationNeuron.cs` files. Split `digitalbrain_rfw_library.dart` by widget category. Slim `INeuron`: move checkpoint/branch/lineage and `GetSiloIdentityAsync` to a separate `IReplayableNeuron`/diagnostics interface.
7. Fix namespace `DigitalBrain.Core.Trust` → `DigitalBrain.Pack.Contracts.Trust`; log the three silent `catch {}`; fix `IBuildRunner.cs:40` `.Result`; confirm dev-gating of the `UserSessionNeuron` password bypass.
8. Flutter: pick ONE state approach (ChangeNotifier is the incumbent majority — standardize on it or on bloc, but pick), migrate the 2 bloc files and the ino_editor buses.

### Step 4 — Accelerate

1. Replace all 19 raw `Thread.Sleep`/`Task.Delay` in tests with `AsyncTestWait` polling — kills the flake tax on every `dotnet test` run.
2. Add the missing Google OAuth-continuation neuron tests (mirror the Salesforce suite) and a minimal `Mcp/` test folder (guard middleware + `ino_interact` happy path) so the two thinnest areas stop requiring manual verification.
3. Keep using targeted Aspire resource restarts + background test polling per CLAUDE.md — already good.

### Step 5 — Automate (only after 1–4)

1. CI gate: fail build on package references with zero code usage (simple script over `Directory.Packages.props` vs grep) — prevents M4 recurring.
2. Architecture test (extend `Architecture/` suite): assert Kernel never references an `OutputType=Exe` project; assert no `catch { }` without logging in `Foundry/`/`Sandbox/`; assert every `ApplyVia` value has a registered handler.
3. Propose this plan's WoW changes through the self-evolution rail itself, per the meta rule.

---

## 5. Sequencing

**Week 1 — Delete + decide.** All of Step 2 (mechanical, verified-safe), plus the pack decision fork. Expected: 4 ghost projects, 3 ghost features, 5+ dead packages, 2 dead docs/artifacts gone; well past the 10% target.
**Week 2 — Security honesty.** Step 3 items 1–2 (sandbox + signing) and 4 (rail bypass). Nothing else ships until the two HIGHs are closed or the code paths that create them are deleted.
**Week 3 — Layering + duplication.** Transport extraction (M1), ConnectorBase (M5), god-file splits.
**Week 4 — Product vertical.** One real write action (Gmail send) end-to-end through the rail: `IInoToolGateway` implementation for Gmail → proposal → approval in the UI → journaled apply → test suite. This is the demo that makes the whole system's thesis true.
**Then** — test acceleration (Step 4) and automation gates (Step 5); revisit memory/retrieval as the next product bet.

---

## 6. What NOT to touch

The INO durable loop, the SelfEvolution grain/registry design, the single-tool MCP surface, the MCP security middleware, Core's purity, the gRPC transport seam in Flutter, secrets handling, and the TestKit harness. These are the assets. Everything above exists to protect them.
