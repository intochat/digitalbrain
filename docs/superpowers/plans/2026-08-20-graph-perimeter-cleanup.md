# Graph perimeter cleanup implementation plan

> **For Codex:** Execute this plan inline in the current `finalv2` working tree. The user has approved this cleanup and requires a commit plus runtime verification.

**Goal:** Remove only CodeGraph-proven unreachable graph-era kernel declarations while preserving Client, MCP, Qdrant, Aspire, tests, all modules, and the complete Flutter prototype.

**Architecture:** This is a subtractive cleanup. The retained product path remains `Flutter -> Kernel chat surface -> modules -> Assistant`, with MCP using Client. Shell and chat stream endpoints remain because Flutter's prototype (including windowing) depends on them. Four isolated legacy records have no indexed incoming edges and have no direct textual use.

**Tech Stack:** .NET 10, Aspire, Model Context Protocol, Flutter.

---

### Task 1: Remove the isolated graph declarations

**Files:**
- Delete: `src/Kernel/DigitalBrain.Kernel/BrainBroadcastRoute.cs`
- Delete: `src/Kernel/DigitalBrain.Kernel/BrainModule.cs`
- Delete: `src/Kernel/DigitalBrain.Kernel/BrainNeuron.cs`
- Delete: `src/Kernel/DigitalBrain.Kernel/GraphEvent.cs`

**Step 1: Verify the deletion boundary before editing.**

Run:

```powershell
codegraph callers BrainBroadcastRoute --json
codegraph callers BrainModule --json
codegraph callers BrainNeuron --json
codegraph callers GraphEvent --json
```

Expected: each result has an empty `callers` array.

**Step 2: Delete only those four files.**

Do not delete `Unrouted`, `JournalProjection`, `MapShellStreams`, or any `HttpSurfacePaths` members: they are still part of retained Client or Flutter shell behavior.

**Step 3: Verify no source reference remains.**

Run:

```powershell
rg -n "\b(BrainBroadcastRoute|BrainModule|BrainNeuron|GraphEvent)\b" src tests --glob '!**/bin/**' --glob '!**/obj/**'
```

Expected: no output.

### Task 2: Verify the retained product surface and commit

**Files:**
- Verify: `E:\intochat\digitalbrain\DigitalBrain.slnx`
- Verify retained: `src/Kernel/DigitalBrain.Client/**`, `src/Kernel/DigitalBrain.Mcp/**`, `src/Aspire/**`, `src/Testing/**`, `tests/**`, and `src/Flutter/**`

**Step 1: Build and test.**

Run:

```powershell
dotnet build DigitalBrain.slnx --no-restore
dotnet test DigitalBrain.slnx --no-build
```

Expected: build succeeds and all non-UI-gated tests pass.

**Step 2: Inspect the patch and preserve user-owned research files.**

Run:

```powershell
git status --short
git diff --check
git diff -- src/Kernel/DigitalBrain.Kernel
```

Expected: only the four deletions and this plan are staged; do not add `docs/research/`.

**Step 3: Commit the cleanup.**

Run:

```powershell
git add docs/superpowers/plans/2026-08-20-graph-perimeter-cleanup.md src/Kernel/DigitalBrain.Kernel/BrainBroadcastRoute.cs src/Kernel/DigitalBrain.Kernel/BrainModule.cs src/Kernel/DigitalBrain.Kernel/BrainNeuron.cs src/Kernel/DigitalBrain.Kernel/GraphEvent.cs
git commit -m "refactor: remove dead graph perimeter"
```

### Task 3: Runtime proof after the commit

**Files:**
- Verify: `src/Aspire/DigitalBrain.AppHost/Program.cs`
- Verify: `src/Kernel/DigitalBrain.Mcp/**`
- Verify: `src/Flutter/**`

**Step 1: Start the distributed app through Aspire.**

Run `aspire start --non-interactive` from `E:\intochat\digitalbrain`, then use `aspire ps`, `aspire describe`, and `aspire wait <resource>` to identify and wait for the Kernel, MCP, and Flutter resources.

**Step 2: Use the running DigitalBrain MCP service to invoke the Assistant chat tool.**

Initialize an MCP HTTP session, list tools, invoke the chat tool with a short prompt, and verify a non-empty assistant response. Do not rely only on a unit test.

**Step 3: Use Computer Use to operate the retained Flutter UI.**

Select the actual Flutter window returned by `sky.list_apps()`, open the chat experience, send a short prompt to Gemma4, and verify a visible non-empty assistant response. Do not alter Flutter source or use the windowing tab as part of cleanup.

**Step 4: Stop Aspire after evidence is captured.**

Run `aspire stop` for the session and report any service unavailable outside source control as a runtime-environment blocker rather than changing configuration.
