# Plan 4 — Form the app-author SDK (Connectors → Sdk; old Sdk → Hosting) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `DigitalBrain.Sdk` the **app-author surface** (the connector/integration neurons). The name is currently taken by a host/client infrastructure library, so first rename that to `DigitalBrain.Hosting`, then rename `DigitalBrain.Connectors` → `DigitalBrain.Sdk`. Keep the solution building and all tests green at every task.

**Architecture:** Fourth milestone of `docs/superpowers/specs/2026-06-17-multirepo-distribution-design.md` (with the brainstorm decision: "Connectors→Sdk, the host/launcher pieces become their own thing"). Two clean single-assembly renames — no namespace entanglement (unlike Plan 3). After this, the app layer is `DigitalBrain.Sdk` (connectors), and host/client infra is `DigitalBrain.Hosting`. The eventual repo split (Plan 5) puts `Sdk` + Awesome + Ino.Experiences + `.ino`/`.yaml` + Clients in the **apps** repo and `Hosting` with **os**.

**Tech Stack:** .NET 11, Orleans 10, `.slnx`, xUnit v3 / MTP, central package management. No new dependencies.

---

## Pre-verified facts (codebase inspection 2026-06-17)

- **Existing `DigitalBrain.Sdk`** = host/client infrastructure: `DigitalBrain/` (`DigitalBrainLauncher`, `DigitalBrainCluster`, `MarketplacePeer`), `Microsoft/Aspire/` (`Aspire.cs` IAspire impl, `DigitalBrainDomainResource`), `Microsoft/Flutter/`, `Microsoft/Windows/FileSystem.cs`. Namespaces: `DigitalBrain.Sdk.DigitalBrain`, `DigitalBrain.Sdk.Microsoft.Aspire`, `DigitalBrain.Sdk.Microsoft.Flutter`, `DigitalBrain.Sdk.Microsoft.Windows` (plus one stray `namespace Projects;` — leave it). Used by Aspire.Hosting, Clients.Console, Ino, Kernel, Mcp (and AppHost — verify via grep).
- **`DigitalBrain.Connectors`** = `Experiences/` (`GmailConnectorNeuron`, `TelegramConnectorNeuron`, `GoogleAuthConnectorNeuron`, `FileSystemConnectorGrain`), namespace `DigitalBrain.Connectors.Experiences`. Used by Kernel. Its `ProjectReference` to the old Sdk is **vestigial** (no real `using DigitalBrain.Sdk.*` in code — only a comment) → drop it.
- Both `.csproj` paths appear in `run-ci.ps1` (lines ~36 and ~38, a pack-verify array).
- `.csproj` ProjectReference paths use Windows backslashes (`..\DigitalBrain.Sdk\DigitalBrain.Sdk.csproj`); grep for BOTH `/` and `\` forms.
- No `InternalsVisibleTo` grants name these two projects.
- The pattern string `DigitalBrain.Sdk` is specific — it does NOT match `Microsoft.NET.Sdk` or `Microsoft.Orleans.Sdk`. Likewise `DigitalBrain.Connectors` is specific. The Protocol-owned Aspire namespace is `DigitalBrain.Protocol.Microsoft.Aspire` (does NOT contain `DigitalBrain.Sdk`) — it must stay untouched.

Baseline (must stay green): `dotnet test DigitalBrain.slnx -c Debug` → Os.Tests 82/6/0, Protocol.Tests 3/0/0, InoLang.Tests 3/0/0.

---

## Task 0: Branch + baseline

- [ ] **Step 1:** `git checkout -b feat/connectors-to-sdk` (confirm `git branch --show-current`).
- [ ] **Step 2:** Build: `dotnet build DigitalBrain.slnx -c Debug -v minimal` → `Build succeeded`, 0 errors.
- [ ] **Step 3:** Tests: `dotnet test DigitalBrain.slnx -c Debug` → 82/6/0 + 3/0/0 + 3/0/0. Regression bar.

---

## Task 1: Rename existing `DigitalBrain.Sdk` → `DigitalBrain.Hosting`

**Files:** rename `src/DigitalBrain.Sdk/` → `src/DigitalBrain.Hosting/`; rename + edit the csproj; blanket-rename the `DigitalBrain.Sdk` namespace string; update dependent ProjectReferences; `DigitalBrain.slnx`; `run-ci.ps1`.

- [ ] **Step 1: Move the project folder + csproj**

```bash
git mv src/DigitalBrain.Sdk src/DigitalBrain.Hosting
git mv src/DigitalBrain.Hosting/DigitalBrain.Sdk.csproj src/DigitalBrain.Hosting/DigitalBrain.Hosting.csproj
```

- [ ] **Step 2: Edit `src/DigitalBrain.Hosting/DigitalBrain.Hosting.csproj`**

Drop any stale packaging props (`<IsPackable>`, `<PackageId>DigitalBrain.Sdk</PackageId>`, `<GeneratePackageOnBuild>`, `<Version>`, `<Description>`, `<PackageTags>` and their comments) and add explicit names. The PropertyGroup should be:

```xml
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>preview</LangVersion>
    <NoWarn>$(NoWarn);ORLEANSEXP005</NoWarn>
    <AssemblyName>DigitalBrain.Hosting</AssemblyName>
    <RootNamespace>DigitalBrain.Hosting</RootNamespace>
  </PropertyGroup>
```

Leave the `<ItemGroup>`s (Orleans/Aspire/MessagePack packages; Os/Protocol/Aspire.Hosting/SourceGen project refs) unchanged.

- [ ] **Step 3: Blanket-rename the namespace string in all `.cs`**

```bash
for f in $(git ls-files | grep -E '\.cs$'); do
  sed -i 's/DigitalBrain\.Sdk/DigitalBrain.Hosting/g' "$f"
done
```
This renames every `namespace DigitalBrain.Sdk.*` declaration and every `using DigitalBrain.Sdk.*` consumer. It is safe: `DigitalBrain.Sdk` is single-assembly here and does not collide with `Microsoft.*.Sdk` or `DigitalBrain.Protocol.Microsoft.Aspire`.

- [ ] **Step 4: Update dependent ProjectReference paths**

```bash
for f in $(grep -rl "DigitalBrain.Sdk/DigitalBrain.Sdk.csproj\|DigitalBrain.Sdk\\\\DigitalBrain.Sdk.csproj" --include=*.csproj src | grep -v /obj/); do
  sed -i 's#DigitalBrain\.Sdk\\DigitalBrain\.Sdk\.csproj#DigitalBrain.Hosting\\DigitalBrain.Hosting.csproj#g; s#DigitalBrain\.Sdk/DigitalBrain\.Sdk\.csproj#DigitalBrain.Hosting/DigitalBrain.Hosting.csproj#g' "$f"
done
```
(Known dependents include Aspire.Hosting, Clients.Console, Ino, Kernel, Mcp, AppHost, and the vestigial one in Connectors — rely on the grep.)

- [ ] **Step 5: Update `DigitalBrain.slnx`**

Change the project path `src/DigitalBrain.Sdk/DigitalBrain.Sdk.csproj` → `src/DigitalBrain.Hosting/DigitalBrain.Hosting.csproj`. If there is an `/Sdk/` solution folder, rename it to `/Hosting/` (cosmetic).

- [ ] **Step 6: Update `run-ci.ps1`**

Change `src/DigitalBrain.Sdk/DigitalBrain.Sdk.csproj` → `src/DigitalBrain.Hosting/DigitalBrain.Hosting.csproj`.

- [ ] **Step 7: Build**

Run: `dotnet build DigitalBrain.slnx -c Debug -v minimal` → `Build succeeded`, 0 errors. If `CS0234`/`CS0246` about `DigitalBrain.Sdk` remains, a fully-qualified reference or a csproj path was missed — grep `DigitalBrain.Sdk` over `src` and fix. Report BLOCKED only for non-mechanical issues.

- [ ] **Step 8: Test**

Run: `dotnet test DigitalBrain.slnx -c Debug` → 82/6/0 + 3/0/0 + 3/0/0.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "refactor(hosting): rename DigitalBrain.Sdk (host/launcher/aspire-impl/client helpers) to DigitalBrain.Hosting"
```

---

## Task 2: Rename `DigitalBrain.Connectors` → `DigitalBrain.Sdk`

Now the `DigitalBrain.Sdk` name is free. Rename the connector project into it and drop the vestigial host-side reference.

**Files:** rename `src/DigitalBrain.Connectors/` → `src/DigitalBrain.Sdk/`; rename + edit the csproj; blanket-rename `DigitalBrain.Connectors` namespace string; update Kernel's ProjectReference; `DigitalBrain.slnx`; `run-ci.ps1`.

- [ ] **Step 1: Move the project folder + csproj**

```bash
git mv src/DigitalBrain.Connectors src/DigitalBrain.Sdk
git mv src/DigitalBrain.Sdk/DigitalBrain.Connectors.csproj src/DigitalBrain.Sdk/DigitalBrain.Sdk.csproj
```

- [ ] **Step 2: Edit `src/DigitalBrain.Sdk/DigitalBrain.Sdk.csproj`**

Set explicit names and DROP the vestigial reference to the host-side project (which Task 1 renamed to `DigitalBrain.Hosting`). The PropertyGroup:

```xml
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>preview</LangVersion>
    <NoWarn>$(NoWarn);ORLEANSEXP005</NoWarn>
    <AssemblyName>DigitalBrain.Sdk</AssemblyName>
    <RootNamespace>DigitalBrain.Sdk</RootNamespace>
  </PropertyGroup>
```

Remove the `<ProjectReference Include="..\DigitalBrain.Hosting\DigitalBrain.Hosting.csproj" />` line (it was the vestigial `..\DigitalBrain.Sdk\...` ref, repointed by Task 1). KEEP the Os + SourceGen references and the package references.

- [ ] **Step 3: Blanket-rename the namespace string in all `.cs`**

```bash
for f in $(git ls-files | grep -E '\.cs$'); do
  sed -i 's/DigitalBrain\.Connectors/DigitalBrain.Sdk/g' "$f"
done
```
This renames `namespace DigitalBrain.Connectors.Experiences` → `DigitalBrain.Sdk.Experiences` and the consumers' `using DigitalBrain.Connectors.*` → `using DigitalBrain.Sdk.*`. Single-assembly, no collisions (the old host-side Sdk is now Hosting).

- [ ] **Step 4: Update dependent ProjectReference paths (Connectors → Sdk)**

```bash
for f in $(grep -rl "DigitalBrain.Connectors/DigitalBrain.Connectors.csproj\|DigitalBrain.Connectors\\\\DigitalBrain.Connectors.csproj" --include=*.csproj src | grep -v /obj/); do
  sed -i 's#DigitalBrain\.Connectors\\DigitalBrain\.Connectors\.csproj#DigitalBrain.Sdk\\DigitalBrain.Sdk.csproj#g; s#DigitalBrain\.Connectors/DigitalBrain\.Connectors\.csproj#DigitalBrain.Sdk/DigitalBrain.Sdk.csproj#g' "$f"
done
```
(Known dependent: Kernel — rely on the grep.)

- [ ] **Step 5: Update `DigitalBrain.slnx`**

Change the project path `src/DigitalBrain.Connectors/DigitalBrain.Connectors.csproj` → `src/DigitalBrain.Sdk/DigitalBrain.Sdk.csproj`. If there is a `/Connectors/` folder, rename to `/Sdk/`.

- [ ] **Step 6: Update `run-ci.ps1`**

Change `src/DigitalBrain.Connectors/DigitalBrain.Connectors.csproj` → `src/DigitalBrain.Sdk/DigitalBrain.Sdk.csproj`.

- [ ] **Step 7: Build**

Run: `dotnet build DigitalBrain.slnx -c Debug -v minimal` → `Build succeeded`, 0 errors. If dropping the vestigial Hosting ref surfaces a REAL `CS0246` (a connector actually used a Hosting type), re-add `<ProjectReference Include="..\DigitalBrain.Hosting\DigitalBrain.Hosting.csproj" />` to `src/DigitalBrain.Sdk/DigitalBrain.Sdk.csproj`, note it in the report (the ref was NOT vestigial after all), and rebuild. Otherwise leave it dropped.

- [ ] **Step 8: Test**

Run: `dotnet test DigitalBrain.slnx -c Debug` → 82/6/0 + 3/0/0 + 3/0/0.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "refactor(sdk): rename DigitalBrain.Connectors to DigitalBrain.Sdk (app-author neuron surface); drop vestigial host ref"
```

---

## Task 3: Final verification + solution tidy

- [ ] **Step 1: No stray old names in source/tooling**

Run: `grep -rn "DigitalBrain\.Connectors" src run-ci.ps1 DigitalBrain.slnx --include=*.cs --include=*.csproj 2>/dev/null | grep -v /obj/`
Expected: empty (or only a historical code COMMENT — if a comment mentions the old path, updating it is optional; note it).
Run: `grep -rn "DigitalBrain\.Sdk\b" src --include=*.cs | grep -v /obj/ | grep -iE "launcher|cluster|marketplacepeer|aspire|flutter" | head`
This confirms host-side types are now under `DigitalBrain.Hosting`, not `DigitalBrain.Sdk` — expect the only `DigitalBrain.Sdk` references to be the connector neurons.

- [ ] **Step 2: Confirm the new DAG**

Run: `grep -nE "ProjectReference" src/DigitalBrain.Sdk/DigitalBrain.Sdk.csproj`
Expected: references to `DigitalBrain.Os` and `DigitalBrain.SourceGen` only (no `DigitalBrain.Hosting`, unless Task 2 Step 7 proved it necessary). The app-author Sdk should not pull host-side infra.

- [ ] **Step 3: Solution folder tidy (cosmetic)**

In `DigitalBrain.slnx`, ensure folders are sensible: `/Sdk/` holds `DigitalBrain.Sdk`; `/Hosting/` holds `DigitalBrain.Hosting`. (Only requirement: every `<Project Path>` resolves.)

- [ ] **Step 4: Full suite**

Run: `dotnet test DigitalBrain.slnx -c Debug` → Os.Tests 82/6/0, Protocol.Tests 3/0/0, InoLang.Tests 3/0/0, overall 0 failures.

- [ ] **Step 5: Commit (if any tidy changes)**

```bash
git add -A
git commit --allow-empty -m "chore(sln): finalize Sdk/Hosting split; verify app-author Sdk DAG"
```

---

## Done-when (this plan)

- `DigitalBrain.Sdk` is the app-author surface (the former Connectors — Gmail/Telegram/GoogleAuth/FileSystem neurons), namespace `DigitalBrain.Sdk.*`, referencing only Os + SourceGen.
- The former host/client infra is `DigitalBrain.Hosting` (namespace `DigitalBrain.Hosting.*`), referenced by Kernel/AppHost/Clients/Mcp/Ino/Aspire.Hosting.
- No project, assembly, or namespace named `DigitalBrain.Connectors` remains; no host-side type remains under `DigitalBrain.Sdk`.
- Full solution builds; Os.Tests 82/6/0, Protocol.Tests 3/0/0, InoLang.Tests 3/0/0.

## Deviations log (fill during execution)

- (record if the Connectors→Hosting ref turned out NOT vestigial, or anything else non-mechanical)
