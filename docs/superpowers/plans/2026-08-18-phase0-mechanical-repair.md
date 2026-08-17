# Phase 0: Mechanical Repair Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `DigitalBrain.slnx` build green with `-warnaserror` and `aspire run` boot, by finishing the half-completed `src/Kernel/Aspire` → `src/Aspire` reorganization and restoring the sources the "Delete trash" commits broke.

**Architecture:** Pure mechanical repair on branch `finalv2` — fix reference paths, restore deleted-but-still-referenced sources from `master`, unify the duplicated name-constant classes into one linked source file, retire the Core V2 seed sketch. No new production logic, no API redesign (that is phase 1).

**Tech Stack:** .NET 11 preview (SDK `11.0.100-preview.6.26359.118`), Aspire 13.5.0-preview, Orleans 10.2.2, central package management (`Directory.Packages.props`).

**Spec:** `docs/superpowers/specs/2026-08-18-digitalbrain-aspire-testing-sdk-design.md` (§4 "Repair actions", §11 phase 0)

## Global Constraints

- Working directory: `E:\intochat\digitalbrain`, branch `finalv2`. NEVER read or write any path under `C:\Users\`.
- `TargetFramework` net11.0 everywhere via `Directory.Build.props`; `TreatWarningsAsErrors=true`; `AnalysisLevel=preview-all`.
- Central package management: `<PackageReference>` entries carry **no** `Version` attribute; do not add new packages or bump versions in this phase.
- Do not modify: `AppHost.cs`, `ProductSurfaceResources.cs`, `Brain/DigitalBrainBuilder.cs`, `Brain/DigitalBrainHostingExtensions.cs` owner-plumbing deletions, `src/Kernel/DigitalBrain.Mcp/Program.cs`, `src/Modules/AI/Contracts/IAgent.cs`, `src/Modules/UI/DigitalBrain.Modules.UI.Aspire.Hosting/ShellNames.cs`, `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Chat/ChatTurnFailure.cs` — these carry **intentional** finalv2 edits (the owner-env refactor), except where a task below names an exact edit.
- Never add meaningless `/// <summary>` doc comments. Match surrounding code style.
- No test projects exist yet (they arrive in phase 2); each task's verification is restore/build-based. TDD applies from phase 2 onward.
- Commit after every task with the trailer: `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.

**Fact base (verified 2026-08-18):** `finalv2` = `master` + commits `aa9219e1`/`17a37ec2`. On `master` the Aspire layer already lives at `src/Aspire/` and the whole solution was consistent. The two finalv2 commits (a) gutted `src/Kernel/DigitalBrain.{Abstractions,Core,Client,Sdk}` to bare csprojs, (b) deleted 47 Kernel + 19 Mcp source files still referenced by the kept `Program.cs` files, (c) deleted the Aspire-layer OAuth/global-using files and the correct `Brain/DigitalBrainNames.cs` (replacing it with a stray incomplete class named `DigitalBrainResources`), (d) rewired `.csproj` paths as if the projects lived somewhere else, and (e) made *intentional* edits (owner-env refactor, MCP pragma) that must be preserved.

---

### Task 1: Repoint `aspire.config.json` and retire the Core V2 seed project

**Files:**
- Modify: `aspire.config.json`
- Modify: `DigitalBrain.slnx` (remove one line)
- Delete: `src/Kernel/DigitalBrain/` (entire directory)

**Interfaces:**
- Consumes: nothing.
- Produces: a solution file whose every `<Project>` entry exists on disk; aspire CLI able to locate the AppHost.

- [ ] **Step 1: Rewrite `aspire.config.json`**

Replace the full file content (both keys currently point at `src/Kernel/DigitalBrain.AppHost/`, which does not exist):

```json
{
  "appHostPath": "src/Aspire/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj",
  "appHost": {
    "path": "src/Aspire/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj"
  }
}
```

- [ ] **Step 2: Remove the seed project from the solution**

In `DigitalBrain.slnx`, inside `<Folder Name="/Kernel/">`, delete exactly this line:

```xml
    <Project Path="src/Kernel/DigitalBrain/DigitalBrain.csproj" Id="5770c5e8-5fe7-4ec5-852e-23655f350a2a" />
```

- [ ] **Step 3: Delete the seed directory**

The seed's concepts are ratified in the spec (§5); the sketch itself does not compile and is retired per spec §3 D12.

```powershell
Remove-Item -Recurse -Force src/Kernel/DigitalBrain
```

- [ ] **Step 4: Verify**

Run: `grep -c "src/Kernel/DigitalBrain/DigitalBrain.csproj" DigitalBrain.slnx` → expected `0`; `Test-Path src/Kernel/DigitalBrain` → expected `False`.

- [ ] **Step 5: Commit**

```powershell
git add -A && git commit -m @'
Repoint aspire.config.json and retire Core V2 seed sketch

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

### Task 2: Fix all broken `ProjectReference` paths

**Files:**
- Modify: `src/Aspire/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj` (lines 13–16)
- Modify: `src/Aspire/DigitalBrain.Aspire.Hosting/DigitalBrain.Aspire.Hosting.csproj` (line 12)
- Modify: `src/Aspire/DigitalBrain.Aspire/DigitalBrain.Aspire.csproj` (re-add 2 references)
- Modify: `src/Kernel/DigitalBrain.Kernel/DigitalBrain.Kernel.csproj` (line 21)
- Modify: `src/Kernel/DigitalBrain.Mcp/DigitalBrain.Mcp.csproj` (line 15)
- Modify: `src/Modules/AI/Aspire.Hosting/DigitalBrain.Modules.AI.Aspire.Hosting.csproj` (line 13)
- Modify: `src/Modules/Google/Aspire.Hosting/DigitalBrain.Modules.Google.Aspire.Hosting.csproj` (line 9)
- Modify: `src/Modules/Memory/Aspire.Hosting/DigitalBrain.Modules.Memory.Aspire.Hosting.csproj` (line 12)
- Modify: `src/Modules/SalesForce/Aspire.Hosting/DigitalBrain.Modules.Salesforce.Aspire.Hosting.csproj` (line 9)
- Modify: `src/Modules/UI/DigitalBrain.Modules.UI.Aspire.Hosting/DigitalBrain.Modules.UI.Aspire.Hosting.csproj` (line 9)

**Interfaces:**
- Consumes: Task 1 (seed removed from slnx, so restore evaluates only real projects).
- Produces: a solution where `dotnet restore DigitalBrain.slnx` resolves every `ProjectReference`.

- [ ] **Step 1: Fix the AppHost's four broken references**

In `src/Aspire/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj` apply exactly these replacements (attributes on the lines stay as they are):

| Old Include | New Include |
|---|---|
| `../Aspire/DigitalBrain.Aspire.Hosting/DigitalBrain.Aspire.Hosting.csproj` | `../DigitalBrain.Aspire.Hosting/DigitalBrain.Aspire.Hosting.csproj` |
| `../DigitalBrain.Kernel/DigitalBrain.Kernel.csproj` | `../../Kernel/DigitalBrain.Kernel/DigitalBrain.Kernel.csproj` |
| `../DigitalBrain.Mcp/DigitalBrain.Mcp.csproj` | `../../Kernel/DigitalBrain.Mcp/DigitalBrain.Mcp.csproj` |
| `../DigitalBrain.Scripting/DigitalBrain.Scripting.csproj` | `../../Kernel/DigitalBrain.Scripting/DigitalBrain.Scripting.csproj` |

- [ ] **Step 2: Fix the hosting-integration reference to Abstractions**

In `src/Aspire/DigitalBrain.Aspire.Hosting/DigitalBrain.Aspire.Hosting.csproj`:
`../../DigitalBrain.Abstractions/DigitalBrain.Abstractions.csproj` → `../../Kernel/DigitalBrain.Abstractions/DigitalBrain.Abstractions.csproj`

- [ ] **Step 3: Re-add the two references the delete commit dropped from the runtime package**

`src/Aspire/DigitalBrain.Aspire/DigitalBrainRuntimeHostingExtensions.cs` calls `DigitalBrainRuntime.Add(...)` and `AddDigitalBrainJournalStorage(...)` (types in `DigitalBrain.Core`) and client types from `DigitalBrain.Client`, so the references are required. In `src/Aspire/DigitalBrain.Aspire/DigitalBrain.Aspire.csproj`, inside the `<ItemGroup>` that holds the ServiceDefaults reference, add (paths corrected for the new layout — master's used `../../DigitalBrain.Client/`):

```xml
    <ProjectReference Include="../../Kernel/DigitalBrain.Client/DigitalBrain.Client.csproj" />
    <ProjectReference Include="../../Kernel/DigitalBrain.Core/DigitalBrain.Core.csproj" />
```

- [ ] **Step 4: Fix the Kernel and Mcp references to the runtime package**

In `src/Kernel/DigitalBrain.Kernel/DigitalBrain.Kernel.csproj` and `src/Kernel/DigitalBrain.Mcp/DigitalBrain.Mcp.csproj`:
`../Aspire/DigitalBrain.Aspire/DigitalBrain.Aspire.csproj` → `../../Aspire/DigitalBrain.Aspire/DigitalBrain.Aspire.csproj`

- [ ] **Step 5: Fix the five module hosting references**

In each of the five module `*.Aspire.Hosting.csproj` files listed above:
`../../../Kernel/Aspire/DigitalBrain.Aspire.Hosting/DigitalBrain.Aspire.Hosting.csproj` → `../../../Aspire/DigitalBrain.Aspire.Hosting/DigitalBrain.Aspire.Hosting.csproj`

- [ ] **Step 6: Verify every reference resolves**

Run: `dotnet restore DigitalBrain.slnx`
Expected: restore succeeds with no `MSB3202`/`NU1104` (missing project) errors. Compile errors are NOT expected at this step (restore does not compile).

- [ ] **Step 7: Commit**

```powershell
git add -A && git commit -m @'
Fix ProjectReference paths broken by the src/Aspire move

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

### Task 3: Restore the four gutted Kernel projects from `master`

**Files:**
- Restore (from `master`, csprojs are byte-identical so this only adds sources): `src/Kernel/DigitalBrain.Abstractions/` (133 files), `src/Kernel/DigitalBrain.Core/` (85), `src/Kernel/DigitalBrain.Client/` (5), `src/Kernel/DigitalBrain.Sdk/` (53)

**Interfaces:**
- Consumes: nothing (independent of Tasks 1–2).
- Produces: `IDigitalBrain`, `DigitalBrainClient`, `Neuron`, `NeuronJournal`, `DigitalBrainRuntime`, `OAuthCallbackPaths` (in `DigitalBrain.Abstractions/OAuth/`), and every type the Kernel/Mcp/module projects compile against.

- [ ] **Step 1: Restore**

```powershell
git checkout master -- src/Kernel/DigitalBrain.Abstractions src/Kernel/DigitalBrain.Core src/Kernel/DigitalBrain.Client src/Kernel/DigitalBrain.Sdk
```

- [ ] **Step 2: Verify counts**

```powershell
(git ls-files src/Kernel/DigitalBrain.Abstractions | Measure-Object -Line).Lines
```
Expected: 134 (Abstractions), and for the same command on Core/Client/Sdk: 86 / 6 / 54.

- [ ] **Step 3: Commit**

```powershell
git add -A && git commit -m @'
Restore gutted Kernel projects (Abstractions, Core, Client, Sdk) from master

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

### Task 4: Restore deleted Kernel and Mcp sources (keep finalv2's `Program.cs` edits)

**Files:**
- Restore from `master`: every file with `D` status in `git diff master finalv2 --name-status -- src/Kernel/DigitalBrain.Kernel src/Kernel/DigitalBrain.Mcp` (47 Kernel files incl. `Auth/**`, `Map*.cs`, `OwnerSessionJournal.cs`, `DigitalBrainHost.cs`, `GlobalUsings.Abstractions.cs`; 19 Mcp files incl. `ChatTools.cs`, `IntrospectionTools.cs`, `RegistryTools.cs`, `TimeTools.cs`, `LibraryBehaviorTools.cs`, `McpSurface.cs`, `GlobalUsings.Abstractions.cs`)
- Do NOT touch: `src/Kernel/DigitalBrain.Mcp/Program.cs` (finalv2's `M` edit — non-static lambda — is intentional and it *references* the restored tool classes)

**Interfaces:**
- Consumes: Task 3 (restored Kernel files compile against Abstractions/Core/Client types).
- Produces: compiling `DigitalBrain.Kernel` and `DigitalBrain.Mcp` projects; `AuthHostingExtensions` (used by Task 6's rename sweep).

- [ ] **Step 1: Restore exactly the deleted files**

```bash
git diff master finalv2 --name-status -- src/Kernel/DigitalBrain.Kernel src/Kernel/DigitalBrain.Mcp | awk '$1=="D" {print $2}' | xargs git checkout master --
```
(Bash tool. `D` in this diff direction = present on master, deleted on finalv2. Modified (`M`) files are untouched, preserving finalv2 edits.)

- [ ] **Step 2: Verify**

Run the same `git diff ... --name-status` pipe with `wc -l` on `D` lines against the **working tree** replaced check: `git status --short src/Kernel/DigitalBrain.Kernel src/Kernel/DigitalBrain.Mcp | grep -c "^A "` → expected 66. Spot-check: `Test-Path src/Kernel/DigitalBrain.Kernel/MapChatVoice.cs` → `True`; `git diff --stat src/Kernel/DigitalBrain.Mcp/Program.cs` → no output (untouched).

- [ ] **Step 3: Commit**

```powershell
git add -A && git commit -m @'
Restore Kernel and Mcp sources still referenced by kept entry points

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

### Task 5: Restore the Aspire-layer breakage files (not the intentional owner-refactor deletions)

**Files:**
- Restore from `master`: `src/Aspire/DigitalBrain.AppHost/GlobalUsings.Abstractions.cs`, `src/Aspire/DigitalBrain.Aspire/GlobalUsings.Abstractions.cs`, `src/Aspire/DigitalBrain.Aspire.Hosting/OAuth/OAuthProviderHosting.cs`, `src/Aspire/DigitalBrain.Aspire.Hosting/OAuth/OAuthProviderHostingDefinition.cs`
- Do NOT restore: `src/Aspire/DigitalBrain.Aspire.Hosting/Brain/DigitalBrainNames.cs` (superseded by Task 6), and no owner-plumbing lines in `DigitalBrainBuilder.cs` / `DigitalBrainHostingExtensions.cs` / `ProductSurfaceResources.cs` (intentional finalv2 deletions)

**Interfaces:**
- Consumes: Task 3 (`OAuthCallbackPaths` lives in restored `DigitalBrain.Abstractions/OAuth/`), Task 2 (Hosting→Abstractions reference path).
- Produces: `OAuthProviderHosting.Register(...)` and `WithLocalDevelopmentOAuthCallback` support consumed by `AppHost.cs` and the Google/Salesforce module hosting projects.

- [ ] **Step 1: Restore the four files**

```powershell
git checkout master -- src/Aspire/DigitalBrain.AppHost/GlobalUsings.Abstractions.cs src/Aspire/DigitalBrain.Aspire/GlobalUsings.Abstractions.cs src/Aspire/DigitalBrain.Aspire.Hosting/OAuth/OAuthProviderHosting.cs src/Aspire/DigitalBrain.Aspire.Hosting/OAuth/OAuthProviderHostingDefinition.cs
```

- [ ] **Step 2: Check the restored OAuth code against the intentional owner-refactor**

Run: `grep -n "\.Owner\b\|WithOwner\|UseOwner\|DefaultOwner" src/Aspire/DigitalBrain.Aspire.Hosting/OAuth/OAuthProviderHosting.cs`
If any hit references the deleted `DigitalBrainBuilder.Owner` / `WithOwner` / `UseOwner` members: replace that usage with the constant `DigitalBrainNames.DefaultOwner` (available after Task 6 — in that case reorder: finish Task 6 first, then this step). Hits on `DefaultOwner` alone are fine.

- [ ] **Step 3: Commit**

```powershell
git add -A && git commit -m @'
Restore OAuth hosting and global usings dropped in the cleanup commits

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

### Task 6: Unify the name constants into one linked source file

**Files:**
- Create: `src/Aspire/Shared/DigitalBrainNames.cs`
- Modify: `src/Aspire/DigitalBrain.Aspire.Hosting/DigitalBrain.Aspire.Hosting.csproj` (add Compile link + global Using)
- Modify: `src/Aspire/DigitalBrain.Aspire/DigitalBrain.Aspire.csproj` (add Compile link)
- Delete: `src/Aspire/DigitalBrain.Aspire.Hosting/DigitalBrainNames.cs` (the stray file declaring class `DigitalBrainResources` — unused by any caller)
- Delete: `src/Aspire/DigitalBrain.Aspire/DigitalBrainResourceNames.cs`
- Modify (rename sweep): `src/Aspire/DigitalBrain.Aspire/DigitalBrainClientHostingExtensions.cs`, `src/Aspire/DigitalBrain.Aspire/DigitalBrainRuntimeHostingExtensions.cs`, `src/Aspire/DigitalBrain.Aspire/DigitalBrainScriptHost.cs`, `src/Kernel/DigitalBrain.Kernel/Auth/Hosting/AuthHostingExtensions.cs`, plus any other file `grep -rl "DigitalBrainResourceNames" src/` finds

**Interfaces:**
- Consumes: Tasks 3–5 (the restored files that use `DigitalBrainResourceNames`).
- Produces: `public static class DigitalBrainNames` in namespace `DigitalBrain.Aspire`, compiled into BOTH `DigitalBrain.Aspire` and `DigitalBrain.Aspire.Hosting`. Members: `DefaultBrain`, `DefaultOwner`, `Storage`, `Clustering`, `Reminders`, `Journal`, `Streams`, `PubSub`, `JournalConnection`, `StreamProvider`, `PubSubStore`, `Owner`, `Modules`, `StateProtectionKey`. Later phases' conformance test (spec §6.1) pins this file's linkage.

- [ ] **Step 1: Create the shared file**

`src/Aspire/Shared/DigitalBrainNames.cs` — exact content (values merge master's two aligned-by-comment classes; lowercase resource names are what the runtime requests as keyed clients):

```csharp
namespace DigitalBrain.Aspire;

// Single source of truth for resource/connection names and configuration keys, physically
// linked into both DigitalBrain.Aspire (silo/client) and DigitalBrain.Aspire.Hosting (AppHost)
// because neither project can reference the other. Each assembly compiles its own copy of
// this public type — an assembly referencing both packages must alias one of them.
public static class DigitalBrainNames
{
    public const string DefaultBrain = "brain";
    public const string DefaultOwner = "dev";

    public const string Storage = "storage";
    public const string Clustering = "clustering";
    public const string Reminders = "reminders";
    public const string Journal = "journal";
    public const string Streams = "streams";
    public const string PubSub = "pubsub";

    public const string JournalConnection = "journal";
    public const string StreamProvider = "DigitalBrain";
    public const string PubSubStore = "PubSubStore";

    public const string Owner = "DigitalBrain:Owner";
    public const string Modules = "DigitalBrain:Modules";
    public const string StateProtectionKey = "DigitalBrain:Security:StateProtectionKey";
}
```

- [ ] **Step 2: Link it into both projects**

In `src/Aspire/DigitalBrain.Aspire/DigitalBrain.Aspire.csproj`, add:

```xml
  <ItemGroup>
    <Compile Include="../Shared/DigitalBrainNames.cs" Link="DigitalBrainNames.cs" />
  </ItemGroup>
```

In `src/Aspire/DigitalBrain.Aspire.Hosting/DigitalBrain.Aspire.Hosting.csproj`, add:

```xml
  <ItemGroup>
    <Compile Include="../Shared/DigitalBrainNames.cs" Link="DigitalBrainNames.cs" />
    <Using Include="DigitalBrain.Aspire" />
  </ItemGroup>
```

(The `Using` makes `DigitalBrainNames` visible to hosting files, whose namespace is `DigitalBrain.Aspire.Hosting`; the runtime project's files already sit in `DigitalBrain.Aspire`.)

- [ ] **Step 3: Delete the two superseded files**

```powershell
git rm src/Aspire/DigitalBrain.Aspire.Hosting/DigitalBrainNames.cs src/Aspire/DigitalBrain.Aspire/DigitalBrainResourceNames.cs
```

- [ ] **Step 4: Rename sweep, member-qualified pairs first**

Run over every file `grep -rl "DigitalBrainResourceNames" src/` reports (Bash tool):

```bash
grep -rl "DigitalBrainResourceNames" src/ | xargs sed -i \
  -e 's/DigitalBrainResourceNames\.OwnerConfigurationKey/DigitalBrainNames.Owner/g' \
  -e 's/DigitalBrainResourceNames\.ModulesConfigurationKey/DigitalBrainNames.Modules/g' \
  -e 's/DigitalBrainResourceNames\.StateProtectionKeyConfigurationKey/DigitalBrainNames.StateProtectionKey/g' \
  -e 's/DigitalBrainResourceNames\.StreamProviderName/DigitalBrainNames.StreamProvider/g' \
  -e 's/DigitalBrainResourceNames\.PubSubStoreName/DigitalBrainNames.PubSubStore/g' \
  -e 's/DigitalBrainResourceNames\.JournalConnectionName/DigitalBrainNames.JournalConnection/g' \
  -e 's/DigitalBrainResourceNames\.JournalResource/DigitalBrainNames.Journal/g' \
  -e 's/DigitalBrainResourceNames\.DefaultBrainName/DigitalBrainNames.DefaultBrain/g' \
  -e 's/DigitalBrainResourceNames\./DigitalBrainNames./g'
```

(The final catch-all pair handles same-named members: `Storage`, `Clustering`, `Reminders`, `Streams`, `PubSub`, `DefaultOwner`.)

- [ ] **Step 5: Point `DefaultOwner` back at the constant**

In `src/Aspire/DigitalBrain.Aspire/DigitalBrainClientHostingExtensions.cs`, change:

```csharp
    public const string DefaultOwner = "dev";
```
to
```csharp
    public const string DefaultOwner = DigitalBrainNames.DefaultOwner;
```
(finalv2 hardcoded `"dev"` only because the constant vanished.)

- [ ] **Step 6: Verify no stragglers**

Run: `grep -rn "DigitalBrainResourceNames\|class DigitalBrainResources" src/` → expected: no output.

- [ ] **Step 7: Commit**

```powershell
git add -A && git commit -m @'
Unify resource name constants into one linked source file

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

### Task 7: Full solution build to green

**Files:**
- Modify: only files the compiler flags; expected candidates listed below.

**Interfaces:**
- Consumes: everything above.
- Produces: `dotnet build DigitalBrain.slnx -warnaserror` exit code 0 — the contract every later phase builds on.

- [ ] **Step 1: Build**

Run: `dotnet build DigitalBrain.slnx -warnaserror` (timeout 600000 ms; first build restores and compiles 32 projects).

- [ ] **Step 2: Resolve residual errors by these rules**

Expected residual classes and their fixes (anything outside these: STOP and report to the user instead of improvising):

1. **Restored code calls a deleted owner-plumbing member** (`DigitalBrainBuilder.Owner`, `.WithOwner(...)`, `UseOwner`, `ApplyOwner`, `ProductSurfaceResources.HttpEndpointName`): replace the call site with the finalv2 pattern — owner comes from the per-project env stamp (`ShellHostingExtensions.OwnerEnvironmentVariable` / `ShellHostingExtensions.DefaultOwner`, as `AppHost.cs` already does) or the constant `DigitalBrainNames.DefaultOwner`; the kernel HTTP endpoint name is `ShellHostingExtensions.HttpEndpointName`.
2. **Missing `using DigitalBrain.Aspire;`** in a restored Kernel file now referencing `DigitalBrainNames`: add the using (the global `Using` item covers only the Hosting project).
3. **Analyzer warnings-as-errors in restored files** (IDE/CA rules that tightened between commits): fix the code style in place; do not suppress, do not touch `.editorconfig`.

- [ ] **Step 3: Rebuild until exit code 0**

Run: `dotnet build DigitalBrain.slnx -warnaserror`
Expected: `Build succeeded.` with 0 warnings, 0 errors.

- [ ] **Step 4: Commit**

```powershell
git add -A && git commit -m @'
Restore solution to a green -warnaserror build

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

### Task 8: Boot verification with `aspire run`

**Files:** none (verification only; any fix discovered here belongs to Task 7's rules).

**Interfaces:**
- Consumes: Task 7's green build, Task 1's repointed `aspire.config.json`.
- Produces: the phase 0 exit evidence — brain fabric + kernel + mcp healthy under the real AppHost.

- [ ] **Step 1: Preflight Docker**

Run: `docker info` → daemon reachable (Azurite and Qdrant containers need it). If Docker is unavailable, STOP and report — do not fake the verification.

- [ ] **Step 2: Launch**

Run `aspire run` from the repo root **in the background** (Bash `run_in_background`; it stays up until stopped).

- [ ] **Step 3: Wait for health**

Poll with `aspire ps` (or `aspire wait kernel` if available in this CLI version) until resources report Running/Healthy — give Azurite + silo up to 5 minutes on first pull.
Acceptance: **`brain` fabric resources, `kernel`, and `mcp` are healthy.** `ollama`, `openwebui`, and the Flutter `window host` MAY be degraded on machines without GPU/Flutter — record their state honestly; they are not phase 0 acceptance criteria.
Cross-check the kernel directly: `curl -fsS http://localhost:5080/health` → `Healthy`.

- [ ] **Step 4: Stop and record**

Stop the background `aspire run` (Ctrl-C equivalent: kill the background task; then `aspire stop` if resources linger). Append a short run log (resource list + states + timestamp) to the final report — not to the repo.

- [ ] **Step 5: Final verification and report**

Re-run `dotnet build DigitalBrain.slnx -warnaserror` one last time (exit 0), then report: build green, boot evidence, and any module resources that were degraded and why.
