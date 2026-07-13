# Subsystem Audit: Foundry / LLM / Sandbox

- **Subsystem**: `foundry` — code generation & execution ("Foundry"), LLM chat-client wiring, and the sandbox.
- **Scope**: `src/DigitalBrain.Kernel/Foundry/` (16 files), `src/DigitalBrain.Kernel/Llm/` (7 files), `src/DigitalBrain.Kernel/Sandbox/` (3 files) = 26 files. (`ProcessRunner.cs` and `OutOfProcessSandbox.cs` counted under Sandbox; `IBuildRunner.cs` contains `ProcessBuildRunner`.)
- **Commit**: `72400e3ebbec27e17af4ae6b5b2c4158c2797fa4` (branch `docs/refinement-audit`)
- **Date**: 2026-07-13

## Subsystem overview

This is the self-evolution execution engine — the machinery that turns a natural-language spec into running C# inside (or beside) the kernel process. It is the highest-trust-consequence subsystem in the repo.

Data flow (Run tier): `FoundryRequest` → `CodeFoundryClosedLoopNeuron` → `CodeGenNeuron` (LLM → C# text) → either stage a `SelfEvolutionProposal` (rail) or, if `AutoApply`+`TrustedAutoApply`, apply immediately → `CodeRunNeuron` → `ICodeExecutor.Execute` → **`InProcessAlcExecutor`** (compile in-process, `CapabilityGate` static screen, load into a collectible `AssemblyLoadContext`, reflectively invoke `Run`). Deploy tier: `CodeDeployNeuron` → `ProcessBuildRunner.VerifyBuildAsync` (shell `dotnet build`) → write source to `Generated/*.cs` → `IResourceController.RestartKernelAsync` (kernel recompiles the file into itself on restart).

Parallel mechanisms exist but are **not** on the primary generated-code path: `PackAlcEmbodier` (in-process collectible-ALC pack embodiment, used by `GeneratedPackRuntime`), `ScriptRunner` (CSharpScript automations, used by `AutomationNeuron`), and `OutOfProcessSandbox` (a child-process executor that is **registered but never invoked anywhere in production code**).

The LLM layer (`/Llm/`) is provider-agnostic `IChatClient` construction (Ollama/AzureOpenAI/OpenAI/Anthropic/xAI/GitHub Models) wrapped with a bounded-concurrency + timeout delegating client and OpenTelemetry.

**Headline conclusion**: The "sandbox" that the code and README name as the real isolation boundary is dead code. Generated and pack code actually executes **in-process at full trust**, guarded only by `CapabilityGate` — a compile-time static-analysis guardrail that is bypassable by reflection and is a complete no-op for the `ScriptRunner` automation path. The Deploy tier applies no gate at all. Human approval is real for the default path but is toggled off by a single config flag, and the executor grains are directly fireable, so the rail is a convention at the edge rather than an enforced boundary.

---

## Per-file review

### Foundry/

**`CapabilityGate.cs`** — Purpose: static-analysis screen of a Roslyn `CSharpCompilation` for banned API usage. Layer: Foundry security. Callers: `InProcessAlcExecutor`, `PackAlcEmbodier`, `OutOfProcessSandbox`, `ScriptRunner`. Correctness: allows the entire `System.*` namespace minus 6 prefix exclusions + 2 member exclusions — an allow-broad/deny-narrow posture, the opposite of least-privilege. Its own header comment admits it is "a guardrail against accidental misuse, not a security boundary" and references a *deleted* follow-up doc whose fix "no longer exists". The exclusion list now *does* contain `System.Type.GetType` and `System.Activator.` (contradicting the stale header claim they are unexcluded), but instance-reflection members (`object.GetType()` → `Type.GetMethod(...)` → `MethodInfo.Invoke(...)`) are **not** excluded, so reflection still reaches any banned API. Framework use of `SymbolDisplayFormat` and semantic model is correct. Verdict: **retain but rename to `CapabilityScreen`/`StaticApiGuard` and stop treating as a boundary** (SEC-300, SEC-305). Weakens the OS trust model by being presented as enforcement.

**`InProcessAlcExecutor.cs`** — Purpose: the actual `ICodeExecutor` — compiles generated C# and runs it **in the kernel process**. Correctness: `CapabilityGate` screen (bypassable), collectible ALC (memory reclaim, not a security boundary), reflective `Invoke` of a static entrypoint. **No timeout, no `CancellationToken`, no resource cap** — a generated infinite loop or `Thread.Sleep`/`while(true)` hangs the grain turn forever (REL-300). `Console.SetOut` is swapped process-globally and is not thread-safe — concurrent executions and the host's own console output interleave/corrupt (REL-301). Verdict: **replace** — must not be the execution engine for untrusted output; route through the out-of-process sandbox with limits. Weakens OS model (untrusted code at full trust in-process).

**`ScriptRunner.cs`** — Purpose: CSharpScript executor for `AutomationNeuron` reactions. **Critical defect**: gates the script by compiling it with `Enumerable.Empty<MetadataReference>()` (line 57) — with no references, symbols do not bind, `GetSymbolInfo(...).Symbol` is null for every node, `FindViolations` returns empty, and the gate is a **no-op**; the script then runs via `CSharpScript` with the *full* reference set (SEC-301). The `try/catch { }` around the gate (line 65) further swallows any gate error. Dead comment blocks at lines 92–98. Verdict: **fix gate references then simplify**. Directly weakens the automation trust boundary.

**`CodeFoundryClosedLoopNeuron.cs`** — Purpose: orchestrates checkpoint → gen → stage-or-apply. Correctness: default path stages a `SelfEvolutionProposal` with `RequiresHumanApproval: true` (good). But `AutoApply` + `TrustedAutoApply` config (`ApplyImmediatelyAsync`) **executes/deploys with zero proposal or decision**, emitting only an `AuditBypass` synapse (SEC-303). Cross-grain reads via `GetOutgoingTimelineAsync` right after `FireAsync` assume synchronous same-turn ordering (REL-302). `StableModuleName` (SHA-256) is deterministic and safe. Verdict: **simplify** — replace the bypass branch with an auto-*decision* on the rail (already prescribed in `docs/execution-plan.md` P2.3).

**`CodeRunNeuron.cs`** — Purpose: thin grain that calls `ICodeExecutor.Execute`. It is a public Orleans grain implementing `IHandle<RunGeneratedCode>`; **any caller with a grain reference can `FireAsync(new RunGeneratedCode(source,...))` and execute arbitrary C# with no proposal/approval** — only the (bypassable) gate inside the executor applies (SEC-304). A test does exactly this. Verdict: **replace/guard** — execution entrypoints must not be directly fireable outside the rail.

**`CodeDeployNeuron.cs`** — Purpose: build-verify + commit source + request kernel restart. **No `CapabilityGate` anywhere on this path** — `ProcessBuildRunner` only checks that the source *compiles* against the full kernel project, then `CommitSource` writes it to `Generated/*.cs`, and on restart the kernel compiles that file into itself at full trust (SEC-308). `RestartPending` guards re-entry. Directly fireable like `CodeRunNeuron` (SEC-304). Verdict: **replace/guard** — the Deploy tier is the widest hole (arbitrary code into the kernel, ungated).

**`CodeGenNeuron.cs`** — Purpose: LLM → C# text. Correctness: system prompt asks for a fenced `csharp` block; `ExtractCode` pulls the first fence or falls back to the raw text; empty → deterministic `FallbackSource`. Model output is treated as code with **no validation beyond later compile+gate**; `Spec`/`Hints` flow straight into the prompt (prompt-injection surface, SEC-308). No structured-output enforcement (relies on markdown fence heuristic). Verdict: **retain, harden** (schema/structured output, treat output as untrusted).

**`FoundryApplyHandlers.cs`** — Purpose: rail apply handlers (`FoundryRun`/`FoundryDeploy`) invoked after human approval. Correctness looks right: find staged record by proposal id, run/deploy, fire completed/rolled-back. `FindStagedAsync` and `Failed` are duplicated verbatim across both handlers (CLEAN-301). These are the *correct* entrypoints; the problem is that the same executor grains are reachable *without* going through here. Verdict: **retain, dedupe**.

**`FoundryCompilation.cs`** — Purpose: build `CSharpCompilation` and reference sets. Correctness: prelude injects `global using System.Net.Http;`/`System.IO;` into every snippet (line 17–18) — convenient but it puts banned namespaces in scope by default, so the gate is doing subtractive work against an intentionally-permissive baseline. `DefaultReferences()` enumerates every DLL in the runtime dir and creates a `MetadataReference` **per call, per execution** with no caching (PERF-300). `TpaReferences` is cleaner. Verdict: **simplify + cache references**.

**`IBuildRunner.cs` (`ProcessBuildRunner`)** — Purpose: verify generated module compiles by shelling `dotnet build` on a temp csproj referencing the kernel. Correctness: temp dir per verify, streams captured, best-effort cleanup. Slow (full build per verify), path resolution via `GetCurrentDirectory()`/`AppContext.BaseDirectory` climbing is fragile (REL-303). **Compile success ≠ safe** — no gate here (feeds SEC-308). Verdict: **retain for CI-style verify, but it must not be the only Deploy check**.

**`CapabilityBroker.cs`** — Purpose: "narrow approved capability facade" for scripts. **Reality: placeholder.** `HttpGetAsync` builds a raw `HttpClient` and fetches **any** URL despite the interface doc claiming "allowlisted domains" — an SSRF surface with no allowlist (SEC-307). `NotifyAsync` is a no-op, `LlmExtractAsync` returns fabricated JSON, `WriteWorkbookAsync` returns a fake artifact string (PROD-300). Verdict: **implement or delete** — aspirational-naming-only today.

**`FoundryServices.cs`** — Purpose: DI registration. Registers `InProcessAlcExecutor` as `ICodeExecutor` (the full-trust in-process path) **and** `OutOfProcessSandbox` as `ISandboxedExecutor` — but nothing resolves `ISandboxedExecutor` (SEC-302). Env-var branch selects Azure vs Aspire resource controller. Verdict: **retain, but the executor wiring is the core defect** — the safe component is registered and never used.

**`IResourceController.cs` (`AspireResourceController`)** — Purpose: kernel-restart intent. Log-only; real restart is out-of-band via Aspire MCP. Honest about it in comments. Verdict: **retain (Note)** — restart is not actually performed by the process.

**`AzureResourceController.cs`** — Purpose: cloud restart. `RestartKernelAsync` is a **TODO no-op** (returns `Task.CompletedTask`) — cloud self-update restart never happens (PROD-301). `dryRun` flag exists but both branches do nothing. Verdict: **implement or mark clearly unimplemented**.

**`PackAlcEmbodier.cs`** — Purpose: compile a typed-C# pack → gate → collectible ALC → instantiate `IPackBehavior`. Correctness: `ExecutionContext.SuppressFlow()` for collectibility, `Resolving += ResolveFromHost` unifies host assemblies **by simple name** so pack casts work. That host-unification means a pack can bind to any host-loaded assembly by name (broadens surface; gate is the only screen, and it is bypassable). In-process = guardrail, not sandbox (the file's own comment says so). Verdict: **retain but route through real isolation** for untrusted packs.

**`ICodeExecutor.cs`** — Interface: synchronous `Execute(string,string)` with **no `CancellationToken`** — bakes the no-timeout defect into the contract (REL-300). Verdict: **replace signature** (async + token + limits).

**`IResourceController.cs`/`ICodeExecutor.cs`/`IBuildRunner.cs`** interfaces are otherwise thin and reasonable.

### Sandbox/

**`ISandboxedExecutor.cs`** — Defines `SandboxTier { InProcessGated, OutOfProcess, Wasm }`. Honest tier comments. `Wasm` is aspirational (no impl, no toolchain) (CLEAN-302). Verdict: **retain**.

**`OutOfProcessSandbox.cs`** — Purpose: the *real* isolation tier — compile to a temp exe, gate, run as a child `dotnet` process with a 30s timeout. **Registered but never invoked** (SEC-302). Even if wired: process isolation only — **no resource caps (memory/CPU/disk), no filesystem jail, no network restriction, same OS user/privileges**; it copies the host `runtimeconfig.json` so the child binds the full shared framework (SEC-306). Verdict: **wire it in as the actual executor and add OS-level limits (job object / cgroup, network off, temp-only FS)**.

**`ProcessRunner.cs`** — Purpose: shared process-exec core (timeout + kill-tree + block-list + output truncation). Correctness: block-list is a *denylist of ~7 command names + 3 argument substrings* — trivially evadable (`Format` blocked, `format.com`/`fmt` not; encoded PowerShell base64 bypasses substring checks entirely). Timeout + kill-tree are solid. Verdict: **retain the mechanics; do not treat the denylist as security** (it is used by SDK integration neurons, out of this subsystem's blast radius but same weakness).

### Llm/

**`DigitalBrainChat.cs`** — Provider fan-out registration; Azure managed-identity fallback via `DefaultAzureCredential`; embedding fallback to `NoOpEmbeddingGenerator`. Correct and readable. Verdict: **retain**.

**`DigitalBrainChatClientRegistration.cs`** — Keyed `IChatClient` per registered model. Mirrors the above per-key. Anthropic `AsIChatClient()` is `[Experimental("MEAI001")]`, suppressed repo-wide (FRAME-301, acceptable). Verdict: **retain**.

**`DigitalBrainChatPolicy.cs` (`BoundedNoRetryChatClient`)** — `DelegatingChatClient` with a `SemaphoreSlim` concurrency bound + per-request timeout via linked CTS. Correct use of MEAI delegating pattern; streaming and non-streaming both covered. Verdict: **retain** (a genuinely solid file).

**`ScopedChatClientFactory.cs` / `DigitalBrainChatClients` / `DigitalBrainChatTelemetry`** — Per-scope client construction; key never logged; OTel with `EnableSensitiveData=false`. Verdict: **retain**.

**`DigitalBrainLlmRuntimeOptions.cs`** — Config binding, registry-then-legacy precedence. Has a vacuous `/// <summary>` block violating the repo's no-summary rule (CLEAN-303). Verdict: **retain, strip summaries**.

**`DigitalBrainEmbeddingRuntimeOptions.cs`** — Config binding for embeddings. Fine. Verdict: **retain**.

**`NoOpEmbeddingGenerator.cs`** — 384-dim zero-vector fail-soft generator; downstream hybrid scorer detects zeros and falls back to keyword recall. Reasonable, documented. Verdict: **retain**.

---

## Subsystem-specific answers

**What actually executes generated C#? Is there a real sandbox?**
The Run tier executes through `CodeRunNeuron` → `ICodeExecutor` → **`InProcessAlcExecutor`**: compile in-process, `CapabilityGate` static screen, load into a *collectible* `AssemblyLoadContext`, reflectively invoke a static `Run`. A collectible ALC is a memory-reclamation feature, **not** a security boundary — the code runs with the full trust and privileges of the kernel process. There is **no** process/AppDomain isolation, **no** resource limit, **no** timeout, and **no** restricted API surface beyond the gate. The Deploy tier compiles the source into the kernel itself on restart with **no gate at all**. A genuinely isolating component exists — `OutOfProcessSandbox` (child process + 30s timeout) — but it is **registered in DI and never resolved/called anywhere**, so it contributes zero protection today. (Context7 verification of `Microsoft.CodeAnalysis.CSharp.Scripting` 5.6.0 was blocked by a Context7 monthly-quota error; recorded as a documentation gap. The Roslyn team's own guidance is well-established: the scripting/compilation APIs are explicitly **not** a security sandbox and run with host trust — this matches the code's own header comments admitting the gate is "not a security boundary".)

**Does execution ALWAYS flow through the approval rail?**
No. Three bypasses: (1) `TrustedAutoApply` config flag makes `CodeFoundryClosedLoopNeuron` apply immediately with no proposal/decision (SEC-303); (2) `CodeRunNeuron` and `CodeDeployNeuron` are public Orleans grains — any code holding a grain reference can `FireAsync(RunGeneratedCode/DeployGeneratedCode)` and execute/deploy directly, no rail (SEC-304); (3) `ScriptRunner` (automation path) runs C# with its gate reduced to a no-op (SEC-301). The default `FoundryRequest` path *does* correctly stage a `SelfEvolutionProposal` with `RequiresHumanApproval:true` and apply only via `FoundryApplyHandlers` after a decision — but it is a convention, not an enforced choke point.

**LLM wiring / prompt-injection / trusted output?**
`IChatClient` is built per provider via MEAI (`AddChatClient`, `AsIChatClient`) and wrapped with `BoundedNoRetryChatClient` (concurrency + timeout) and OTel. This layer is sound. However, model output is treated as **near-trusted**: `CodeGenNeuron` extracts a markdown code fence with no structured-output contract, and `Spec`/`Hints` (which can originate from user/email/CRM content upstream) flow verbatim into the prompt, so a prompt-injection that emits malicious C# reaches the executor and — on the Deploy path or via the gate bypasses — runs. Structured-output reliability rests on a fragile ```` ```csharp ```` heuristic with a deterministic fallback.

**What does the sandbox actually restrict? Escape paths?**
`OutOfProcessSandbox` restricts nothing today because it is unused. If wired, it provides only *process* isolation (memory separation + a 30s wall-clock timeout via `ProcessRunner`); it does **not** restrict filesystem, network, CPU, memory, or privileges, and it copies the host runtimeconfig granting the full framework. `CapabilityGate` "restricts" API surface only by static symbol matching and is escapable via instance reflection (`object.GetType().GetMethod(...).Invoke(...)`), via the `ScriptRunner` empty-reference no-op, and entirely absent on the Deploy path.

**Determinism/validation before apply; timeouts/cancellation/caps?**
Deploy verifies compilation (`dotnet build`) before commit — that is the only pre-apply validation, and it proves compilability, not safety. Run has no pre-apply validation beyond the bypassable gate. Timeouts: LLM calls are bounded (`BoundedNoRetryChatClient`) and the out-of-process sandbox has 30s; but the *actual* in-process executor has **no** timeout or cancellation, and `ICodeExecutor.Execute` is synchronous with no token. No memory/CPU caps anywhere on execution.

**Any path where email/CRM/model output becomes executable code without validation?**
Yes. `Spec`/`Hints` → `CodeGenNeuron` → generated C# → (a) `ScriptRunner` with a no-op gate, or (b) `CodeDeployNeuron` with no gate compiling into the kernel, or (c) `CodeRunNeuron` fired directly. Combined with the reflection bypass of the gate, there is no path on which untrusted-influenced generated code is provably validated before full-trust execution.

**Security-control status summary:**
- Human-approval rail (default `FoundryRequest` path): **implemented** but not enforced (bypassable, SEC-303/304).
- `CapabilityGate` static screen: **partial/guardrail** — reflection-bypassable (SEC-300), no-op for scripts (SEC-301), absent on Deploy (SEC-308).
- Out-of-process sandbox: **placeholder** (implemented but never invoked, SEC-302; no resource limits even if used, SEC-306).
- WASM tier: **aspirational-naming-only** (enum member, no impl).
- HTTP capability allowlist: **aspirational-naming-only** (interface claims allowlist; impl allows any host, SEC-307).
- Execution timeout/resource caps (Run tier): **absent** (REL-300).

---

## Findings

### SEC-302: The only real isolation tier (`OutOfProcessSandbox`) is registered but never invoked; generated code runs in-process at full trust
- **Severity**: Critical
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Kernel/Foundry/FoundryServices.cs:11-13` registers `ICodeExecutor→InProcessAlcExecutor` and `ISandboxedExecutor→OutOfProcessSandbox`. Grep across `src/**` shows `ISandboxedExecutor` resolved nowhere except its own file; `CodeRunNeuron.cs:10` resolves `ICodeExecutor` (the in-process one). `OutOfProcessSandbox.cs:8-12` and `README`/comments call it "the realistic security sandbox".
- **Current behavior**: All generated-code Run execution goes through `InProcessAlcExecutor` (in the kernel process). The out-of-process sandbox is dead code.
- **Why it matters** (INFERENCE): The subsystem *documents* a process-isolation boundary that does not exist at runtime, giving false assurance while untrusted, LLM-generated code executes with the kernel's full trust and privileges.
- **OS/product consequence**: Breaks the self-evolution trust boundary — the "apply" primitive runs untrusted code inside the OS kernel with no isolation.
- **Recommendation** (PROPOSAL): Make `CodeRunNeuron` resolve `ISandboxedExecutor` (out-of-process) as the execution path; keep `InProcessAlcExecutor` only for trusted, signed, human-approved packs, if at all.
- **Deletion/simplification opportunity**: yes — collapse the two executor abstractions into one sandboxed path.
- **Dependencies**: SEC-304, SEC-306, REL-300.
- **Tests/measurements required**: A test asserting the production execution path spawns a child process; a test that a filesystem/network operation from generated code is denied.
- **Effort**: L
- **Migration/rollback concern**: Behavior change for any current in-process consumers; gate behind config with sandbox as default.

### SEC-308: Deploy tier compiles generated code into the kernel with NO capability gate
- **Severity**: Critical
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Kernel/Foundry/CodeDeployNeuron.cs:16-31` calls `IBuildRunner.VerifyBuildAsync` then `CommitSource` then `RestartKernelAsync` — no `CapabilityGate.FindViolations` call. `IBuildRunner.cs:14-46` only shells `dotnet build` (compilability). On restart the kernel compiles `Generated/*.cs` into itself.
- **Current behavior**: Deploy-tier generated code is written to disk and compiled into the kernel at full trust with only a "does it compile" check.
- **Why it matters** (INFERENCE): The widest hole — arbitrary C# (including banned APIs the gate would reject on the Run path) enters the kernel process ungated.
- **OS/product consequence**: Destroys the mutation trust boundary; a Deploy apply can embed anything (`System.IO`, `Process.Start`, network) into the OS kernel.
- **Recommendation** (PROPOSAL): Run `CapabilityGate` (and ideally the sandbox) on the Deploy source before commit; require the generated module to be signed/approved; consider loading deployed modules into an isolated ALC rather than the kernel assembly.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: SEC-300, SEC-304.
- **Tests/measurements required**: Test that a Deploy source containing `System.IO.File.Delete` is rejected before commit.
- **Effort**: M
- **Migration/rollback concern**: none beyond stricter rejection.

### SEC-301: `ScriptRunner` gates against a zero-reference compilation, making the capability gate a no-op for automations
- **Severity**: Critical
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Kernel/Foundry/ScriptRunner.cs:57` `FoundryCompilation.Create("script-gate", scriptBody, Enumerable.Empty<MetadataReference>())` then `FindViolations` at line 59; the real run at lines 75-79 uses `_options` with the full reference set. With no references, `GetSymbolInfo` returns null symbols so `FindViolations` finds nothing. The `catch { }` at line 65 also swallows gate errors.
- **Current behavior**: Automation scripts are effectively ungated, then executed with full references via `CSharpScript`.
- **Why it matters** (INFERENCE): The one gate applied to the `AutomationNeuron` path never fires; any C# an automation contains runs.
- **OS/product consequence**: Automations (a user-facing self-evolution surface) can execute unrestricted code.
- **Recommendation** (PROPOSAL): Gate against the same reference set used to run (`_options` references), or reuse `FoundryCompilation.DefaultReferences()`; remove the silent `catch`.
- **Deletion/simplification opportunity**: yes — delete dead comment block lines 92-98.
- **Dependencies**: SEC-300.
- **Tests/measurements required**: Test that a script calling `System.IO.File.ReadAllText` is rejected by `ScriptRunner.ExecuteAsync`.
- **Effort**: S
- **Migration/rollback concern**: Existing automations relying on the leak would start failing (desired).

### SEC-304: Executor grains (`CodeRunNeuron`, `CodeDeployNeuron`) are directly fireable, bypassing the approval rail
- **Severity**: Critical
- **Confidence**: High
- **Evidence**: `CodeFoundrySynapses.cs:101-105` — `ICodeRunNeuron : IHandle<RunGeneratedCode>`, `ICodeDeployNeuron : IHandle<DeployGeneratedCode>`. `tests/DigitalBrain.Tests/Foundry/CodeRunNeuronWiringTests.cs:20` fires `RunGeneratedCode` directly and code executes. No caller-identity/approval check in `CodeRunNeuron.cs` or `CodeDeployNeuron.cs`.
- **Current behavior**: Any grain/MCP caller can execute or deploy code by firing a synapse, without a `SelfEvolutionProposal`/`Decision`.
- **Why it matters** (INFERENCE): The rail is a convention observed only by `CodeFoundryClosedLoopNeuron`/`FoundryApplyHandlers`; the execution primitives themselves are unguarded.
- **OS/product consequence**: The "one governed rail" invariant is unenforceable at the boundary that matters (execution).
- **Recommendation** (PROPOSAL): Have the executor grains accept only an approved-apply token/correlation proven to originate from a `SelfEvolutionDecision(Approved)`, or make them internal to the apply handlers.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: SEC-302, SEC-303.
- **Tests/measurements required**: Test that firing `RunGeneratedCode` without a matching approved proposal is refused.
- **Effort**: M
- **Migration/rollback concern**: Test/harness code that fires directly must move to the rail.

### SEC-300: `CapabilityGate` is a reflection-bypassable, allow-broad static guardrail presented as a boundary
- **Severity**: High
- **Confidence**: High
- **Evidence**: `CapabilityGate.cs:15-40` allows all of `System.*` minus 6 prefixes + `System.Type.GetType`/`System.Activator.`. Instance-reflection members are not excluded: `object.GetType()` → `System.Object.GetType` (allowed), `Type.GetMethod` → `System.Type.GetMethod` (allowed, only `.GetType` is excluded), `MethodInfo.Invoke` → `System.Reflection.MethodInfo.Invoke` (allowed, only `System.Reflection.Assembly.` is excluded). Header comment admits it is "a guardrail... not a security boundary".
- **Current behavior**: Reflection chains reach nominally-banned APIs with no statically-resolvable banned symbol.
- **Why it matters** (INFERENCE): The primary API-surface control is bypassable, so it cannot be relied on as the isolation mechanism it is used as.
- **OS/product consequence**: Undermines the capability model for all in-process execution paths.
- **Recommendation** (PROPOSAL): Move enforcement to a real boundary (out-of-process sandbox with OS limits). If keeping the gate, invert to an allowlist of specific members and ban `System.Reflection.*`, `System.Type.Get*`, `System.Object.GetType` reachability, or forbid reflection syntactically.
- **Deletion/simplification opportunity**: yes — rename to reflect it is not a boundary.
- **Dependencies**: SEC-302, SEC-301, SEC-308.
- **Tests/measurements required**: Test that a reflection-based `Process.Start` is rejected (currently would pass).
- **Effort**: M
- **Migration/rollback concern**: Tighter gate may reject currently-passing packs.

### SEC-303: `TrustedAutoApply` config flag fully bypasses the human-approval rail
- **Severity**: High
- **Confidence**: High
- **Evidence**: `CodeFoundryClosedLoopNeuron.cs:29-40,119-120` — when `request.AutoApply` and `DigitalBrain:Foundry:TrustedAutoApply=true`, calls `ApplyImmediatelyAsync` (executes/deploys) after firing only an `AuditBypass` synapse; no `SelfEvolutionProposal`/`Decision` is created. Corroborated by `docs/architecture-assessment-and-plan.md:32-33` and `docs/execution-plan.md:134-135`.
- **Current behavior**: One config flag turns the "human-approved is the only path" invariant into a toggle.
- **Why it matters** (INFERENCE): Even audited, it produces a different journal shape (no proposal/decision), so replay/rollback governance differs from the sacred path.
- **OS/product consequence**: Weakens the self-evolution invariant to a config switch.
- **Recommendation** (PROPOSAL): Per the existing plan, replace the bypass with an auto-*decision*: still create the proposal, then record `SelfEvolutionDecision(Approved, DecidedBy:"trusted-auto-config")` — same journal shape, one code path.
- **Deletion/simplification opportunity**: yes — deletes the `ApplyImmediatelyAsync` second path.
- **Dependencies**: SEC-304, FoundryApplyHandlers.
- **Tests/measurements required**: Test that with the flag set the journal still contains proposal + decision + apply-result.
- **Effort**: M
- **Migration/rollback concern**: none.

### SEC-307: `CapabilityBroker.HttpGetAsync` fetches any host despite documented allowlist (SSRF)
- **Severity**: High
- **Confidence**: High
- **Evidence**: `CapabilityBroker.cs:8-28` — interface comment says "Http (allowlisted domains)"; impl builds `new HttpClient()` and `GetStringAsync(url)` for any URL, with a comment admitting "currently allow any host".
- **Current behavior**: Script-reachable HTTP capability can hit internal/metadata endpoints (e.g. cloud IMDS) with no allowlist.
- **Why it matters** (INFERENCE): SSRF from automation/pack code; the "narrow approved capability facade" is not narrow.
- **OS/product consequence**: Breaks the least-privilege capability boundary the broker claims to enforce.
- **Recommendation** (PROPOSAL): Enforce a per-proposal domain allowlist; block private/link-local ranges; share one `HttpClient`.
- **Deletion/simplification opportunity**: no (unless broker is deleted per PROD-300).
- **Dependencies**: PROD-300.
- **Tests/measurements required**: Test that a non-allowlisted/internal URL is refused.
- **Effort**: S
- **Migration/rollback concern**: none.

### SEC-306: `OutOfProcessSandbox` has process isolation but no resource/filesystem/network limits
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `OutOfProcessSandbox.cs:57` launches `dotnet <dll>` via `ProcessRunner.RunAsync(..., timeoutMs: 30_000)`; `CopyHostRuntimeConfig` (67-74) grants the full shared framework. No job-object/cgroup, no filesystem sandbox, no network disable; child runs as same OS user.
- **Current behavior**: If wired, the child could read/write the filesystem and open sockets freely; only wall-clock is bounded.
- **Why it matters** (INFERENCE): Process isolation alone stops memory corruption of the host but not exfiltration or destructive I/O.
- **OS/product consequence**: Even the "real sandbox" would be a weak boundary.
- **Recommendation** (PROPOSAL): Add OS-level confinement (Windows Job Object memory/CPU caps + restricted token; Linux cgroup+seccomp+netns), run in a temp-only working dir, disable network by default.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: SEC-302.
- **Tests/measurements required**: Test that a sandboxed program cannot write outside its temp dir or open a socket.
- **Effort**: L
- **Migration/rollback concern**: Platform-specific; needs per-OS implementation.

### SEC-305: `CapabilityGate` header comment is stale/misleading about the reflection bypass and a deleted doc
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `CapabilityGate.cs:7-12` states "CONFIRMED BYPASS: Type.GetType + Activator.CreateInstance sit inside that broad System. allowance and aren't excluded", but lines 38-39 now list `System.Type.GetType` and `System.Activator.` as excluded; it also links a doc "deleted in commit 6dfc0a7... the tracked fix it described no longer exists".
- **Current behavior**: Comment contradicts code and points at a removed doc.
- **Why it matters** (INFERENCE): Misleads maintainers about which bypasses are open (the *real* open bypass is instance reflection, SEC-300, which the comment does not name).
- **OS/product consequence**: Documentation-integrity risk on the most security-critical file.
- **Recommendation** (PROPOSAL): Rewrite the comment to describe the actual residual bypass (instance reflection) and drop the dead doc reference.
- **Deletion/simplification opportunity**: yes.
- **Dependencies**: SEC-300.
- **Tests/measurements required**: n/a.
- **Effort**: S
- **Migration/rollback concern**: none.

### REL-300: In-process executor has no timeout, cancellation, or resource cap
- **Severity**: High
- **Confidence**: High
- **Evidence**: `ICodeExecutor.cs:7` `CodeExecutionResult Execute(string, string)` — synchronous, no `CancellationToken`. `InProcessAlcExecutor.cs:47` `method.Invoke(null, ...)` runs with no timeout.
- **Current behavior**: Generated code with an infinite loop or blocking call hangs the Orleans grain turn indefinitely.
- **Why it matters** (INFERENCE): One bad generation can wedge a grain/thread; no way to cancel or bound it.
- **OS/product consequence**: Availability/liveness of the self-evolution engine and its host silo.
- **Recommendation** (PROPOSAL): Move execution out-of-process (SEC-302) where `ProcessRunner`'s timeout applies; make `ICodeExecutor` async with a token and a hard deadline.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: SEC-302.
- **Tests/measurements required**: Test that an infinite-loop generation is terminated within a deadline.
- **Effort**: M
- **Migration/rollback concern**: Interface signature change.

### REL-301: `Console.SetOut` swap in the executor is process-global and not thread-safe
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `InProcessAlcExecutor.cs:42-63` swaps `Console.Out` for a `StringWriter` around the invoke and restores it in `finally`.
- **Current behavior**: Concurrent executions (or any concurrent host console writer) interleave/steal each other's output; a crash before `finally` leaks the redirect.
- **Why it matters** (INFERENCE): Output capture is unreliable under concurrency and pollutes host logging.
- **OS/product consequence**: Non-deterministic execution results undermine replay/verification.
- **Recommendation** (PROPOSAL): Capture stdout in the out-of-process sandbox (already does), or pass an explicit `TextWriter` to generated code instead of hijacking the global console.
- **Deletion/simplification opportunity**: yes (removed once out-of-process).
- **Dependencies**: SEC-302.
- **Tests/measurements required**: Concurrent-execution test asserting no output cross-talk.
- **Effort**: S
- **Migration/rollback concern**: none.

### REL-302: Orchestrator reads grain timelines immediately after firing, assuming synchronous same-turn ordering
- **Severity**: Medium
- **Confidence**: Medium
- **Evidence**: `CodeFoundryClosedLoopNeuron.cs:17-22,89-90,105-107` fire to another grain then `GetOutgoingTimelineAsync(...).LastOrDefault(...)`; `FoundryApplyHandlers.cs:20-22,67-69` do the same.
- **Current behavior**: Relies on the callee having journaled its result by the time the caller reads. Works if `FireAsync` completes the handler synchronously, but is fragile against async streaming/reactivation.
- **Why it matters** (INFERENCE): Missed results become false `no-source`/`run-failed` rollbacks; race under load or after restart.
- **OS/product consequence**: Unreliable self-evolution loop outcomes and misleading rollback journaling.
- **Recommendation** (PROPOSAL): Use request/response return values from the executor grain rather than post-hoc timeline scans, or correlate on `CorrelationId` with a wait.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: none.
- **Tests/measurements required**: Test the loop across a simulated reactivation between fire and read.
- **Effort**: M
- **Migration/rollback concern**: none.

### REL-303: `ProcessBuildRunner` project/path resolution is fragile
- **Severity**: Low
- **Confidence**: Medium
- **Evidence**: `IBuildRunner.cs:21-25` resolves the kernel csproj from `Directory.GetCurrentDirectory()` then a `AppContext.BaseDirectory/../../..` fallback.
- **Current behavior**: Deploy verify depends on CWD/layout; breaks when hosted from a different working directory (e.g. published/containerized).
- **Why it matters** (INFERENCE): Deploy verify silently fails to find the project and reports build failure.
- **OS/product consequence**: Deploy tier unreliable outside dev layout.
- **Recommendation** (PROPOSAL): Resolve the reference set from loaded assemblies/TPA rather than a source csproj, matching `FoundryCompilation.TpaReferences`.
- **Deletion/simplification opportunity**: yes.
- **Dependencies**: none.
- **Tests/measurements required**: Verify from a published output dir.
- **Effort**: M
- **Migration/rollback concern**: none.

### PROD-300: `CapabilityBroker` capabilities are placeholders presented as real
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `CapabilityBroker.cs:30-47` — `NotifyAsync` returns `Task.CompletedTask` (no delivery), `LlmExtractAsync` returns fabricated JSON string, `WriteWorkbookAsync` returns a fake `artifact:workbook.xlsx:...` string.
- **Current behavior**: Scripts/triggers calling these "capabilities" get stubbed responses, not real notify/LLM/workbook effects.
- **Why it matters** (INFERENCE): Automations appear to have capabilities they lack; silent no-ops.
- **OS/product consequence**: Capability model is aspirational-naming-only for 3 of 4 methods.
- **Recommendation** (PROPOSAL): Implement against real channel/LLM/artifact grains or delete the methods until implemented.
- **Deletion/simplification opportunity**: yes.
- **Dependencies**: SEC-307.
- **Tests/measurements required**: Test that `NotifyAsync` produces an observable delivery.
- **Effort**: M
- **Migration/rollback concern**: none.

### PROD-301: `AzureResourceController.RestartKernelAsync` is a TODO no-op
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `AzureResourceController.cs:20-23` — comment `// TODO Task 10` and returns `Task.CompletedTask`; both `dryRun` and non-`dryRun` branches do nothing.
- **Current behavior**: In cloud mode, a Deploy-tier kernel restart is logged but never performed.
- **Why it matters** (INFERENCE): Deploy-tier self-evolution never actually activates in the cloud; the new module is committed to disk but the kernel is not restarted to load it.
- **OS/product consequence**: Deploy tier is non-functional in production (cloud).
- **Recommendation** (PROPOSAL): Implement ACA revision restart via managed identity, or mark the Deploy tier explicitly unsupported in cloud until implemented.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: SEC-308, CodeDeployNeuron.
- **Tests/measurements required**: Integration test that a restart command is issued.
- **Effort**: M
- **Migration/rollback concern**: none.

### ARCH-300: Four overlapping code-execution mechanisms with inconsistent gating and duplicated authority
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `InProcessAlcExecutor` (Run), `ProcessBuildRunner`+kernel restart (Deploy, ungated), `PackAlcEmbodier` (packs, in-process), `OutOfProcessSandbox` (unused). Each applies (or omits) `CapabilityGate` differently.
- **Current behavior**: "What executes untrusted code" is spread across four components with divergent trust handling.
- **Why it matters** (INFERENCE): No single enforcement point; every new path risks a new gap (as Deploy and ScriptRunner already show).
- **OS/product consequence**: Diffuse trust boundary; hard to reason about or audit.
- **Recommendation** (PROPOSAL): Consolidate on one sandboxed execution service that every path (Run, Deploy, packs, scripts) routes through, with the gate + sandbox applied uniformly.
- **Deletion/simplification opportunity**: yes — net reduction of execution surfaces.
- **Dependencies**: SEC-300/301/302/308.
- **Tests/measurements required**: A single conformance test suite exercised by all execution entrypoints.
- **Effort**: L
- **Migration/rollback concern**: Significant refactor; stage behind config.

### PERF-300: `DefaultReferences()` rebuilds the full runtime metadata-reference set per execution
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `FoundryCompilation.cs:66-81` enumerates every `*.dll` in the runtime dir and calls `AssemblyName.GetAssemblyName` + `MetadataReference.CreateFromFile` on each; called from `InProcessAlcExecutor.cs:11` on every `Execute`.
- **Current behavior**: Hundreds of file probes + metadata reads per code run.
- **Why it matters** (INFERENCE): Avoidable latency/allocation on the hot execution path.
- **OS/product consequence**: Slower self-evolution cycles.
- **Recommendation** (PROPOSAL): Cache the reference set once (static lazy); prefer `TpaReferences`.
- **Deletion/simplification opportunity**: yes.
- **Dependencies**: none.
- **Tests/measurements required**: Micro-benchmark before/after.
- **Effort**: S
- **Migration/rollback concern**: none.

### FRAME-300: Roslyn compilation/scripting used as an isolation mechanism it is not designed to provide
- **Severity**: Medium
- **Confidence**: Medium (Context7 verification blocked by quota — documentation gap)
- **Evidence**: `InProcessAlcExecutor.cs` and `ScriptRunner.cs` rely on `Microsoft.CodeAnalysis.CSharp(.Scripting)` + a collectible ALC as the trust boundary; the code's own `CapabilityGate.cs:9-11` concedes it is "not a security boundary".
- **Current behavior**: Framework used at its trust default (host trust) while being treated as a sandbox.
- **Why it matters** (INFERENCE): Roslyn scripting/compilation is explicitly not a security sandbox; expecting isolation from it is a category error.
- **OS/product consequence**: Framework-level misuse underpinning SEC-300/302.
- **Recommendation** (PROPOSAL): Confirm via Context7/Roslyn docs when quota resets; treat compiled output as fully trusted and isolate at the process/OS level.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: SEC-302.
- **Tests/measurements required**: n/a (design).
- **Effort**: S (doc/decision)
- **Migration/rollback concern**: none.

### FRAME-301: Anthropic/MEAI experimental API suppressed repo-wide
- **Severity**: Note
- **Confidence**: High
- **Evidence**: `DigitalBrainChatClientRegistration.cs:75-87` uses `AnthropicClient.AsIChatClient()` documented as `[Experimental("MEAI001")]`, suppressed via csproj `<NoWarn>`.
- **Current behavior**: Experimental MEAI surface used with warnings suppressed.
- **Why it matters** (INFERENCE): API may change across the pinned `Anthropic 12.35.1` / `Microsoft.Extensions.AI 10.7.0` upgrades.
- **OS/product consequence**: Maintenance risk on the LLM layer.
- **Recommendation** (PROPOSAL): Track the experimental attribute; re-verify on package bumps.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: none.
- **Tests/measurements required**: build after upgrades.
- **Effort**: S
- **Migration/rollback concern**: none.

### CLEAN-300: `ScriptRunner` and `CapabilityGate` carry dead/misleading comment blocks
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `ScriptRunner.cs:92-98` two comment-only blocks describing non-existent behavior; `CapabilityGate.cs:7-12` stale (see SEC-305).
- **Current behavior**: Noise/contradiction in security-critical files.
- **Why it matters** (INFERENCE): Comment rot on files where clarity is safety-relevant.
- **Recommendation** (PROPOSAL): Delete/rewrite.
- **Deletion/simplification opportunity**: yes.
- **Dependencies**: SEC-305.
- **Effort**: S
- **Migration/rollback concern**: none.

### CLEAN-301: Duplicated `FindStagedAsync`/`Failed` across both apply handlers
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `FoundryApplyHandlers.cs:36-50` and `83-97` are near-identical.
- **Recommendation** (PROPOSAL): Extract a shared base/helper.
- **Deletion/simplification opportunity**: yes.
- **Effort**: S
- **Migration/rollback concern**: none.

### CLEAN-302: `SandboxTier.Wasm` is an aspirational enum member with no implementation
- **Severity**: Note
- **Confidence**: High
- **Evidence**: `ISandboxedExecutor.cs:8-13` documents Wasm as "NET-NEW with zero prior art... not implemented".
- **Recommendation** (PROPOSAL): Keep only if on the near roadmap; otherwise drop until built.
- **Deletion/simplification opportunity**: yes.
- **Effort**: S
- **Migration/rollback concern**: none.

### CLEAN-303: `DigitalBrainLlmRuntimeOptions` carries vacuous `/// <summary>` blocks against the repo rule
- **Severity**: Note
- **Confidence**: High
- **Evidence**: `DigitalBrainLlmRuntimeOptions.cs:6-8,27-30` `/// <summary>` doc comments; CLAUDE.md forbids these.
- **Recommendation** (PROPOSAL): Remove; rely on self-explanatory names.
- **Deletion/simplification opportunity**: yes.
- **Effort**: S
- **Migration/rollback concern**: none.

### TEST-300: No test proves the capability gate blocks reflection, the Deploy path, or the ScriptRunner path
- **Severity**: High
- **Confidence**: High
- **Evidence**: `tests/DigitalBrain.Tests/Foundry/CapabilityGateTests.cs` covers only literal namespace bans (`Process.Start`, `System.Net`, `System.IO`); no reflection-bypass test, no `ScriptRunner` gate test, no `CodeDeployNeuron` gate test. `InProcessAlcExecutorTests.cs` has no timeout/concurrency test and no test that the sandbox is the production path.
- **Current behavior**: The exact bypasses in SEC-300/301/308 are untested, so they pass silently.
- **Why it matters** (INFERENCE): The security posture is asserted by comments, not by tests.
- **OS/product consequence**: Regressions/holes in the trust boundary go undetected.
- **Recommendation** (PROPOSAL): Add red tests for each bypass (reflection `Process.Start`, script `File.ReadAllText`, Deploy `System.IO`), an infinite-loop timeout test, and a test that the production executor is out-of-process.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: SEC-300/301/302/308, REL-300.
- **Tests/measurements required**: the tests themselves.
- **Effort**: M
- **Migration/rollback concern**: none.

### TEST-301: No test covers the `TrustedAutoApply` bypass or the foundry→rail→apply integration
- **Severity**: Medium
- **Confidence**: Medium
- **Evidence**: Grep shows no test asserting proposal/decision journal shape for `CodeFoundryClosedLoopNeuron`; only `CodeRunNeuronWiringTests` (direct fire) and gate/executor unit tests exist.
- **Current behavior**: The rail integration and its bypass are unverified.
- **Recommendation** (PROPOSAL): Add tests asserting the default path stages a proposal and applies only after an approved decision, and (after the SEC-303 fix) that the trusted path records an auto-decision.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: SEC-303, SEC-304.
- **Effort**: M
- **Migration/rollback concern**: none.

---

## Second-pass corroborating audit (merged from redundant parallel audit `kernel-foundry.md`)

A redundant parallel audit independently reviewed the same files and is folded in here so all findings live in one subsystem document. Its findings use a different ID block; they are reconciled into the canonical findings register. Where it agrees with the primary audit above, treat as corroboration; where it adds new findings, they are additive.

## Findings

### SEC-150: CapabilityGate bypassable via `typeof(bannedType)` + reflection Invoke
- **Severity**: Critical
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Kernel/Foundry/CapabilityGate.cs:29-40, 92-105` — exclusions are prefix strings with trailing dots (`"System.Diagnostics.Process."`); `Type.GetMethod` (instance) and `MethodInfo.Invoke` are not excluded (`System.Reflection.Assembly.`/`.Emit.` are).
- **Current behavior**: `typeof(System.Diagnostics.Process)` renders `"System.Diagnostics.Process"` (no trailing dot) → does not match the `"System.Diagnostics.Process."` exclusion → passes. `t.GetMethod("Start", …)` (`System.Type.GetMethod`, not the excluded `System.Type.GetType`) + `MethodInfo.Invoke` then launch an arbitrary process with the gate reporting zero violations.
- **Why it matters** (INFERENCE): The single static control guarding in-process, full-trust code execution is defeated by a 3-line reflection idiom that any capable LLM (or attacker steering it) can emit.
- **OS/product consequence**: Breaks the self-evolution trust boundary — generated/pack/automation code can execute arbitrary OS operations (process spawn, and via other reflection, more) despite the gate.
- **Recommendation** (PROPOSAL): Stop treating the gate as a security control; run all untrusted/LLM-authored code out-of-process with OS-level privilege reduction. If keeping the gate, ban reflection wholesale (`System.Reflection.*`, `System.Type` member access, `typeof` of non-allowlisted types) and switch to an allowlist of *types*, not a `System.`-minus-exclusions denylist.
- **Deletion/simplification opportunity**: yes — a type-allowlist is smaller and safer than the denylist.
- **Dependencies**: SEC-151, SEC-152, SEC-153.
- **Tests/measurements required**: red-team tests proving `typeof`+reflection, `Environment`, `Unsafe`, `Environment.Exit` are all rejected (or contained by OS sandbox).
- **Effort**: L
- **Migration/rollback concern**: tightening the gate may reject legitimate pack code; needs a pack-compat pass.

### SEC-151: Generated code can read the credential store / env vars and kill the host (gate gaps)
- **Severity**: Critical
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Kernel/Foundry/CapabilityGate.cs:29-40` — `System.Environment`, `System.Runtime.CompilerServices` (Unsafe/RuntimeHelpers), `System.GC` are NOT excluded; only `System.Runtime.InteropServices.`/`.Loader.` are.
- **Current behavior**: `System.Environment.GetEnvironmentVariables()` passes the gate; run-tier `Run()` can return the dictionary as its output (surfaced to caller/logs) → API keys (`DigitalBrain:Llm:AnthropicApiKey`, etc., when provided via env) exfiltrate through the result channel even though `System.Net` is blocked. `System.Environment.Exit(0)` terminates the silo. `System.Runtime.CompilerServices.Unsafe.*` enables memory corruption.
- **Why it matters** (INFERENCE): Network egress being blocked gives false comfort — the return value and env access are sufficient to leak secrets and to DoS the host.
- **OS/product consequence**: Credential compromise + availability loss from within the self-evolution sandbox tier.
- **Recommendation**: Add `System.Environment`, `System.Runtime.CompilerServices` (except safe subset), `System.GC`, `System.AppContext` to exclusions; better, allowlist types. Run out-of-process with env scrubbed and no secrets in the child's environment.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: SEC-150, credential/auth subsystem.
- **Tests/measurements required**: test that env access is rejected/empty and that output cannot carry secret material.
- **Effort**: M
- **Migration/rollback concern**: minimal.

### SEC-152: The only real isolation boundary (OutOfProcessSandbox) has zero production consumers
- **Severity**: High
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Kernel/Foundry/FoundryServices.cs:13` registers `ISandboxedExecutor→OutOfProcessSandbox`; grep shows consumers only in `OutOfProcessSandbox.cs`, `FoundryServices.cs`, `ISandboxedExecutor.cs`, and tests. `CodeRunNeuron.cs:10-11` resolves `ICodeExecutor` (→ `InProcessAlcExecutor`), never `ISandboxedExecutor`.
- **Current behavior**: Every reachable execution runs in-process; the out-of-process sandbox is dead on all product paths.
- **Why it matters** (INFERENCE): The documented "hardening tier" provides zero actual protection because nothing routes to it.
- **OS/product consequence**: The self-evolution execution surface has no isolation in practice.
- **Recommendation**: Route `CodeRunNeuron` and `ScriptRunner`/pack execution through `ISandboxedExecutor`; or delete the registration and stop implying isolation exists.
- **Deletion/simplification opportunity**: yes (delete dead registration) — but preferable to *use* it.
- **Dependencies**: SEC-150, SEC-153, REL-166.
- **Tests/measurements required**: integration test proving run-tier executes out-of-process.
- **Effort**: M
- **Migration/rollback concern**: out-of-process adds latency/`dotnet` startup cost per run.

### SEC-153: AssemblyLoadContext / CSharpScript run generated code fully trusted in-process
- **Severity**: High
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Kernel/Foundry/InProcessAlcExecutor.cs:31-47`, `PackAlcEmbodier.cs:71-81`, `ScriptRunner.cs:75-79`. Microsoft Learn (net-10): *"AssemblyLoadContext does not provide any security features. All code has full permissions of the process."*
- **Current behavior**: Collectible ALC and `CSharpScript.RunAsync` isolate assembly *identity/unloadability*, not privileges; loaded code shares the silo's identity, memory, filesystem, network, and Orleans grain state.
- **Why it matters** (INFERENCE): Naming ("ALC executor", "Sandbox/") implies containment that the runtime does not provide.
- **OS/product consequence**: Any gate bypass (SEC-150/151) executes at full silo trust.
- **Recommendation**: Treat in-process execution as trusted-only; move untrusted code to OS-isolated processes/containers/Wasm.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: SEC-150, SEC-152.
- **Tests/measurements required**: documented threat model + isolation integration test.
- **Effort**: L
- **Migration/rollback concern**: architecture-level.

### SEC-154: CapabilityBroker allows HTTP to any host despite "allowlisted domains" contract
- **Severity**: High
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Kernel/Foundry/CapabilityBroker.cs:5-7` (interface doc: "Http (allowlisted domains)") vs `22-28` (`HttpGetAsync` GETs any URL with a raw `HttpClient`). Live via `AutomationNeuron.cs:118`.
- **Current behavior**: An approved automation script can fetch/exfiltrate to arbitrary external or internal (SSRF) URLs; the promised allowlist does not exist.
- **Why it matters** (INFERENCE): This is the sanctioned egress for the *reachable* execution path — the exfiltration channel that the blocked `System.Net` in generated code was supposed to prevent.
- **OS/product consequence**: Data exfiltration + SSRF against internal services from within an approved automation.
- **Recommendation**: Enforce a real per-proposal domain allowlist; use `IHttpClientFactory`; block private/link-local ranges by default.
- **Deletion/simplification opportunity**: yes — delete the stub methods (`LlmExtract`/`WriteWorkbook`/`Notify`) until real.
- **Dependencies**: SEC-150, automation/rail subsystem.
- **Tests/measurements required**: allowlist enforcement + SSRF tests.
- **Effort**: M
- **Migration/rollback concern**: may break existing automations relying on open egress.

### SEC-155: TrustedAutoApply config bypasses human approval for the highest-risk surface
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Kernel/Foundry/CodeFoundryClosedLoopNeuron.cs:29-40, 119-120` — `AutoApply && TrustedAutoApply` fires `AuditBypass` then `ApplyImmediatelyAsync`, skipping the proposal/approval rail.
- **Current behavior**: A single boolean (`DigitalBrain:Foundry:TrustedAutoApply`) removes human approval for run/deploy of LLM-generated code; an `AuditBypass` is journaled.
- **Why it matters** (INFERENCE): The rail is the whole safety story; a config flag disabling it for code execution is the most dangerous possible bypass to expose.
- **OS/product consequence**: Un-reviewed self-mutation if the flag is ever set (misconfig, test bleed).
- **Recommendation**: Restrict auto-apply to `SelfEvolutionRisk.InProcessCode` at most, require signed source, and forbid it entirely for `KernelRestart`/deploy; alert on `AuditBypass`.
- **Deletion/simplification opportunity**: yes — consider deleting auto-apply for code tiers.
- **Dependencies**: SelfEvolution rail, ARCH-163.
- **Tests/measurements required**: test that deploy-tier auto-apply is refused.
- **Effort**: S
- **Migration/rollback concern**: none.

### SEC-156: CodeGen prompt built from unsanitized Spec/Hints (prompt-injection vector)
- **Severity**: Medium
- **Confidence**: Medium
- **Evidence**: `src/DigitalBrain.Kernel/Foundry/CodeGenNeuron.cs:24-31` — `prompt = system + cmd.Spec + cmd.Hints` verbatim.
- **Current behavior**: No delimiting/escaping; whoever controls Spec controls the generation prompt. No connector path wires Spec today (SEC/PROD-162), so not currently reachable.
- **Why it matters** (INFERENCE): If Gmail/Salesforce/user content ever feeds Spec, injected instructions steer generated code, and the weak gate won't catch the result.
- **OS/product consequence**: Untrusted content → attacker-influenced self-evolution.
- **Recommendation**: Treat Spec as data (structured message, delimiters, provenance tag); never let connector content become a generation instruction without human framing.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: SEC-150, connector subsystems, SEC/PROD-162.
- **Tests/measurements required**: injection tests once an entry point exists.
- **Effort**: M
- **Migration/rollback concern**: none.

### SEC-157: ProcessRunner command blocklist is incomplete security theater
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Kernel/Sandbox/ProcessRunner.cs:12-21, 89-107` — blocks 7 command names + 3 literal argument patterns.
- **Current behavior**: `ShellAsync`/`PowerShellAsync` run arbitrary command lines; the blocklist stops only exact-named commands (`format`, `shutdown`, …) and three literal strings — bypassable via paths, piping (`curl … | sh`), encoding, or any destructive command not listed.
- **Why it matters** (INFERENCE): Presenting a blocklist as a control invites reliance on it.
- **OS/product consequence**: Arbitrary command execution by SDK-integration neurons; false sense of containment.
- **Recommendation**: Drop the blocklist pretense; gate shell exec behind explicit approval + OS sandbox. Keep timeout/kill-tree/truncation (those are sound).
- **Deletion/simplification opportunity**: yes — delete the blocklist or replace with allowlist.
- **Dependencies**: SDK-integration neurons (out of this subsystem).
- **Tests/measurements required**: n/a (control removal).
- **Effort**: S
- **Migration/rollback concern**: none.

### SEC/PROD-162: Entire Foundry closed loop has no external entry point (speculative infrastructure)
- **Severity**: High
- **Confidence**: High
- **Evidence**: grep for `FoundryRequest`/`GenerateCode(` across `src/DigitalBrain.Mcp/**` → no matches; callers of `FoundryRequest` are only inside `Foundry/` + tests; codegraph: `FoundryRequest` "no covering tests found."
- **Current behavior**: `CodeGen/CodeRun/CodeDeploy/CodeFoundryClosedLoop` are wired to each other and to apply handlers but nothing user-facing fires `FoundryRequest`.
- **Why it matters** (INFERENCE): A large, maximally-dangerous subsystem exists with no product surface and no end-to-end tests — carrying all the SEC risk above with none of the value yet.
- **OS/product consequence**: Elon Step 2 candidate — either connect it through the rail with proper isolation, or delete until needed.
- **Recommendation**: Decide: wire an approved MCP/Ino entry point (with SEC-150..155 fixed and out-of-process execution) or delete the loop.
- **Deletion/simplification opportunity**: yes — potentially delete `CodeGen/CodeRun/CodeDeploy/CodeFoundryClosedLoop` + apply handlers.
- **Dependencies**: all SEC findings, ARCH-163/164, TEST-170.
- **Tests/measurements required**: end-to-end loop test if retained.
- **Effort**: L
- **Migration/rollback concern**: deleting removes an in-progress feature.

### ARCH-163: Approval not bound to generated source (human approves prose, not code)
- **Severity**: High
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Kernel/Foundry/CodeFoundryClosedLoopNeuron.cs:63-74` (proposal `ProposedChange` is prose; source only in `FoundryApplyStaged`); `FoundryApplyHandlers.cs:36-47` (source fetched by `ProposalId` journal lookup, no content hash).
- **Current behavior**: The approver sees a rationale/description; the apply handler later pulls the staged source by id. Nothing hashes the approved content or verifies the applied source matches what a human saw.
- **Why it matters** (INFERENCE): Violates the rail's "diff → human approve → apply" invariant for the case where the diff *is* executable code.
- **OS/product consequence**: Weakens the self-evolution trust chain precisely where stakes are highest.
- **Recommendation**: Include a source hash in the proposal, render the actual source in the approval surface, and re-verify hash at apply.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: SelfEvolution rail, ARCH-164.
- **Tests/measurements required**: test that apply refuses when staged source hash ≠ approved hash.
- **Effort**: M
- **Migration/rollback concern**: none.

### ARCH-164: Artifact identity keyed on spec, not source; no signing
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Kernel/Foundry/CodeFoundryClosedLoopNeuron.cs:122-126` (`StableModuleName` = SHA256 of spec); `CodeDeployNeuron.cs:39-47` (`File.WriteAllText` overwrites `<module>.cs`).
- **Current behavior**: Two different generated sources for one spec map to the same module name and overwrite the committed file; no signature on the artifact.
- **Why it matters** (INFERENCE): Non-content-addressed artifacts prevent reliable caching, dedup, rollback, and tamper detection.
- **OS/product consequence**: Replay/rollback ambiguity in the self-evolution journal.
- **Recommendation**: Name artifacts by source hash; sign; store content-addressed.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: ARCH-163, REL-165.
- **Tests/measurements required**: test dedup + collision behavior.
- **Effort**: M
- **Migration/rollback concern**: changes on-disk layout of `Generated/`.

### REL-165: Fragile CWD-relative path resolution + non-atomic overwrite in deploy
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `CodeDeployNeuron.cs:41-46` (`Directory.GetCurrentDirectory()` target heuristic, `File.WriteAllText`); `IBuildRunner.cs:21-25` (`.csproj` located via CWD then `AppContext.BaseDirectory/../../..`).
- **Current behavior**: Target directory and project path depend on process CWD; write is non-atomic and overwrites.
- **Why it matters** (INFERENCE): Behavior differs between local dev, tests, and containerized runtime; a crash mid-write leaves a partial file.
- **OS/product consequence**: Deploy-tier reliability/reproducibility gaps.
- **Recommendation**: Resolve paths from explicit configuration; write to temp + atomic rename.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: ARCH-164.
- **Tests/measurements required**: deploy test under a non-repo CWD.
- **Effort**: S
- **Migration/rollback concern**: none.

### REL-166: InProcessAlcExecutor mutates process-global Console.Out under concurrency
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Kernel/Foundry/InProcessAlcExecutor.cs:42-63` — `Console.SetOut(writer)` around `method.Invoke`, restored in `finally`.
- **Current behavior**: Two concurrent executions (multiple grains) race on the shared `Console.Out`; captured output interleaves or leaks across runs; a nested run restores the wrong writer.
- **Why it matters** (INFERENCE): Silo runs many grains concurrently; global console redirection is not reentrant.
- **OS/product consequence**: Corrupted/leaked execution output in the self-evolution result channel.
- **Recommendation**: Capture output via a per-run `TextWriter` passed to the generated contract (or out-of-process stdout), not `Console.SetOut`.
- **Deletion/simplification opportunity**: yes (moot once out-of-process, SEC-152).
- **Dependencies**: SEC-152.
- **Tests/measurements required**: concurrent-execution output-isolation test.
- **Effort**: S
- **Migration/rollback concern**: none.

### REL-167: ScriptRunner unbounded compile cache + swallowed gate errors
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Kernel/Foundry/ScriptRunner.cs:37, 73-77` (unbounded `_scriptCache`), `55-65` (`catch { }` treats a throwing gate as pass).
- **Current behavior**: Compiled scripts are retained forever keyed by body hash; if `CapabilityGate`/compilation throws during the pre-check, execution proceeds to the real run anyway.
- **Why it matters** (INFERENCE): Unbounded growth is a slow leak; swallowing gate exceptions turns a fail-closed control into fail-open.
- **OS/product consequence**: Memory growth + a gate that can be made to throw is a gate that can be skipped.
- **Recommendation**: Bound the cache (LRU/size cap); on gate exception, fail closed (reject), don't proceed.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: SEC-150.
- **Tests/measurements required**: cache-eviction test; gate-throws-→-rejected test.
- **Effort**: S
- **Migration/rollback concern**: none.

### FRAME-168: No retry, no token/cost budget on LLM calls
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Kernel/Llm/DigitalBrainChatPolicy.cs:19-59` — concurrency + timeout only; class name `BoundedNoRetryChatClient`.
- **Current behavior**: Transient provider errors surface as hard failures; no per-request/agg token or cost ceiling.
- **Why it matters** (INFERENCE): Reliability and cost-runaway exposure on every LLM path.
- **OS/product consequence**: Flaky INO/foundry generation; unbounded spend.
- **Recommendation**: Add bounded exponential-backoff retry for idempotent reads and a token/cost budget guard.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: FRAME-169.
- **Tests/measurements required**: retry + budget-exceeded tests.
- **Effort**: M
- **Migration/rollback concern**: none.

### FRAME-169: CodeGen uses string prompt + regex extraction, silent stub fallback
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Kernel/Foundry/CodeGenNeuron.cs:24-36, 38-56`.
- **Current behavior**: No structured-output schema; ```` ```csharp ```` block scraped by `IndexOf`; empty/failed extraction silently returns a canned stub.
- **Why it matters** (INFERENCE): Brittle parsing; failures masked as "success" with a stub.
- **OS/product consequence**: Unreliable generation; hidden LLM errors.
- **Recommendation**: Use Microsoft.Extensions.AI structured/response-format output; surface extraction failure explicitly.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: FRAME-168.
- **Tests/measurements required**: extraction + failure-surfacing tests.
- **Effort**: M
- **Migration/rollback concern**: none.

### CLEAN-158: CapabilityGate class comment is stale and self-contradicting
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Kernel/Foundry/CapabilityGate.cs:8-12` claims `Type.GetType`/`Activator.CreateInstance` "aren't excluded," but lines 38-39 exclude both; also references a doc deleted in commit `6dfc0a7`.
- **Current behavior**: Comment misleads readers about the real allow/deny set.
- **Why it matters** (INFERENCE): Comment rot on a security-relevant control invites wrong assumptions (in both directions — it understates coverage there while overstating it via SEC-150).
- **Recommendation**: Rewrite the comment to state the actual denylist and the reflection-bypass reality (SEC-150).
- **Deletion/simplification opportunity**: yes.
- **Dependencies**: SEC-150.
- **Tests/measurements required**: n/a.
- **Effort**: S
- **Migration/rollback concern**: none.

### CLEAN-159: CapabilityBroker methods are fabricating stubs behind a real interface
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Kernel/Foundry/CapabilityBroker.cs:37-47` — `LlmExtractAsync`/`WriteWorkbookAsync` return canned strings; `NotifyAsync` no-ops.
- **Current behavior**: Callers receive fake "extracted"/"artifact" data as if real.
- **Why it matters** (INFERENCE): Silent stubs on a capability surface produce wrong results that look valid.
- **Recommendation**: Implement or throw `NotImplementedException`/remove until real; never fabricate data.
- **Deletion/simplification opportunity**: yes.
- **Dependencies**: SEC-154.
- **Tests/measurements required**: n/a.
- **Effort**: S
- **Migration/rollback concern**: none.

### CLEAN-160: Kernel restart is a no-op/TODO in both local and cloud
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Kernel/Foundry/AzureResourceController.cs:20-23` (TODO), `IResourceController.cs:12-20` (log-only).
- **Current behavior**: Deploy tier commits `<module>.cs` but never actually reloads/restarts to activate it.
- **Why it matters** (INFERENCE): The deploy tier's terminal step is unimplemented; "restart-requested" is a label, not an effect.
- **Recommendation**: Implement ACA revision restart (cloud) and document the Aspire-MCP-driven local restart as an explicit external step; or delete the cloud controller until implemented.
- **Deletion/simplification opportunity**: yes.
- **Dependencies**: SEC/PROD-162.
- **Tests/measurements required**: cloud restart integration test if implemented.
- **Effort**: M
- **Migration/rollback concern**: none.

### CLEAN-161: Two divergent reference strategies (whole runtime dir vs TPA)
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Kernel/Foundry/FoundryCompilation.cs:66-81` (`DefaultReferences` scans every runtime `*.dll`) used by `InProcessAlcExecutor`; `42-64` (`TpaReferences`) used elsewhere.
- **Current behavior**: In-process run compiles against the entire framework surface; other paths use TPA.
- **Why it matters** (INFERENCE): Broader reference surface = larger attack/compile surface and inconsistency.
- **Recommendation**: Standardize on TPA (or a minimal curated ref set) everywhere.
- **Deletion/simplification opportunity**: yes — delete `DefaultReferences`.
- **Dependencies**: SEC-153.
- **Tests/measurements required**: compile-parity test.
- **Effort**: S
- **Migration/rollback concern**: some generated code may need refs added back.

### TEST-170: No coverage for the foundry loop or the gate bypasses
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: codegraph — `FoundryRequest` "no covering tests found"; existing tests cover `CodeRunNeuron` wiring and `OutOfProcessSandbox` (which is unused in prod). No test asserts the SEC-150/151 bypasses are blocked.
- **Current behavior**: The riskiest paths are untested end-to-end and adversarially.
- **Why it matters** (INFERENCE): Security controls without red-team tests regress silently.
- **Recommendation**: Add adversarial gate tests (reflection, `Environment`, `Unsafe`, `Exit`) and an end-to-end loop test if the loop is retained.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: SEC-150, SEC-151, SEC/PROD-162.
- **Tests/measurements required**: the tests themselves.
- **Effort**: M
- **Migration/rollback concern**: none.

### Positive notes (no finding)
- Prompt/response telemetry uses `EnableSensitiveData = false` (`ScopedChatClientFactory.cs:87-89`); API keys are never logged (`ScopedChatClientFactory.cs:9`). Good secret hygiene on the LLM path.
- `ScriptRunner` never crashes the host on script error (returns diagnostics) — correct isolation of *faults* (not privileges).
- `ISandboxedExecutor.cs` documentation is refreshingly honest about tier strengths and the not-implemented Wasm tier.
