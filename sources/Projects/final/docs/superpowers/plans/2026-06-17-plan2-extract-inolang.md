# Plan 2 — Extract the `DigitalBrain.InoLang` Language Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Carve the Ino/Yaml *language* (parser, AST, validator, interpreter, yaml parser) out of `DigitalBrain.Core` into a new leaf project `DigitalBrain.InoLang` that depends only on `DigitalBrain.Protocol` (+ YamlDotNet), keeping the whole solution building and every test green.

**Architecture:** Second milestone of `docs/superpowers/specs/2026-06-17-multirepo-distribution-design.md`. Same namespace-preserving move technique proven in Plan 1: move files into the new project but keep their existing `DigitalBrain.Core.Domain.Ino` / `DigitalBrain.Core.Domain.Yaml` namespaces, so consumers compile unchanged; `Core` then references `InoLang`. Does NOT split git repos yet.

**Scope decision (recorded in spec):** `DigitalBrain.SourceGen` is NOT part of this plan. It is the Orleans dispatch-manifest generator with no relation to the Ino language; it moves to **os** in Plan 3. Plan 2 extracts only the five language files.

**Tech Stack:** .NET 11 (`net11.0`), Microsoft.Orleans 10 (the Ino AST records use `[GenerateSerializer]`, so InoLang must run Orleans serialization codegen), YamlDotNet, xUnit v3 under Microsoft Testing Platform, central package management.

**Naming:** The new project is `DigitalBrain.InoLang`. The existing `DigitalBrain.Ino` project is a different thing (app-layer Experiences: `LlmAgentNeuron`, `MemoryNeuron`) and is left untouched here — it consumes the language via the unchanged namespaces (transitively through `Sdk → Core → InoLang`).

---

## Pre-verified facts (from codebase inspection, 2026-06-17)

- The five files to move and their current namespaces (preserved on move):
  - `Domain/Ino/InoAst.cs` → `DigitalBrain.Core.Domain.Ino` (records with `[GenerateSerializer]`; `System.Text.Json` only)
  - `Domain/Ino/InoParser.cs` → `DigitalBrain.Core.Domain.Ino` (`System` only; public `Parse`, `ToCanonical`, `ParseBoot`)
  - `Domain/Ino/InoValidator.cs` → `DigitalBrain.Core.Domain.Ino` (string/AST based; public `Validate`)
  - `Domain/Ino/RuleInterpreter.cs` → `DigitalBrain.Core.Domain.Ino` (uses `DigitalBrain.Core.Domain.Events` = `Synapse`, now in Protocol; has a **dead** `using DigitalBrain.Core.UI;` — `CardItem` is from `InoAst`, no UI type is used)
  - `Domain/Yaml/YamlParser.cs` → `DigitalBrain.Core.Domain.Yaml` (uses `DigitalBrain.Core.Domain.Ino` + `YamlDotNet`; public `Parse`, `ParseBoot`, `ValidateYaml`)
- `InoExperience` fields: `Name`, `Version`, `Description?`, `Emits` (`string[]`), `Rules` (`RuleDeclaration[]`), … There is NO `Triggers` field; an `on: X` line becomes a `RuleDeclaration` with `On == "X"`.
- Consumers reach the language via the unchanged namespaces; the only direct in-Core consumer is `Application/IRuleHostNeuron.cs` (so `Core` must reference `InoLang`). All other consumers (`DigitalBrain.Ino`, `Kernel`, `Sdk`, `Core.Tests`) get it transitively through their existing reference to `Core`/`Sdk`.
- `YamlDotNet` and `Microsoft.Orleans.Sdk` are already pinned in `Directory.Packages.props`.

## File Structure (created/modified in this plan)

- **Create** `src/DigitalBrain.InoLang/DigitalBrain.InoLang.csproj` — leaf language project (Protocol + Orleans.Sdk + YamlDotNet).
- **Create** `src/DigitalBrain.InoLang.Tests/DigitalBrain.InoLang.Tests.csproj` + `LanguageLeafTests.cs` — leaf-self-containment + parse round-trip guard.
- **Move** (Core → InoLang, namespaces unchanged): `Domain/Ino/InoAst.cs`, `Domain/Ino/InoParser.cs`, `Domain/Ino/InoValidator.cs`, `Domain/Ino/RuleInterpreter.cs`, `Domain/Yaml/YamlParser.cs`.
- **Edit** `RuleInterpreter.cs` — remove the single dead `using DigitalBrain.Core.UI;` line.
- **Modify** `src/DigitalBrain.Core/DigitalBrain.Core.csproj` — add `ProjectReference` to InoLang.
- **Modify** `DigitalBrain.slnx` — add both new projects.

Every Task ends green: `dotnet build DigitalBrain.slnx` succeeds and `DigitalBrain.Core.Tests` stays at **82 passed / 6 skipped / 0 failed**.

---

## Task 0: Baseline — confirm current green state on a fresh branch

**Files:** none (verification + branch).

- [ ] **Step 1: Create the feature branch**

```bash
git checkout -b feat/extract-inolang
git branch --show-current   # expect feat/extract-inolang
```

- [ ] **Step 2: Build the solution**

Run: `dotnet build DigitalBrain.slnx -c Debug -v minimal`
Expected: `Build succeeded`, 0 errors (≈33 pre-existing warnings are fine).

- [ ] **Step 3: Record the baseline test count**

Run: `dotnet test src/DigitalBrain.Core.Tests/DigitalBrain.Core.Tests.csproj -c Debug --no-build`
Expected: `Failed: 0, Passed: 82, Skipped: 6, Total: 88`. This is the regression bar.

---

## Task 1: Create the empty `DigitalBrain.InoLang` leaf project

**Files:**
- Create: `src/DigitalBrain.InoLang/DigitalBrain.InoLang.csproj`
- Modify: `DigitalBrain.slnx`

- [ ] **Step 1: Write the project file**

Create `src/DigitalBrain.InoLang/DigitalBrain.InoLang.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>preview</LangVersion>
    <NoWarn>$(NoWarn);ORLEANSEXP005</NoWarn>
    <IsPackable>true</IsPackable>
    <PackageId>DigitalBrain.InoLang</PackageId>
    <GeneratePackageOnBuild>false</GeneratePackageOnBuild>
    <Version>0.1.0-preview</Version>
    <Description>InoLang: the .ino / os-on-yaml language for DigitalBrain — parser, AST, validator, rule interpreter. Depends only on the protocol seam.</Description>
    <PackageTags>orleans;digitalbrain;inolang;dsl;yaml</PackageTags>
  </PropertyGroup>

  <ItemGroup>
    <!-- Orleans.Sdk runs serialization codegen for the [GenerateSerializer] Ino AST records. -->
    <PackageReference Include="Microsoft.Orleans.Sdk" />
    <PackageReference Include="Microsoft.Orleans.Core.Abstractions" />
    <PackageReference Include="YamlDotNet" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\DigitalBrain.Protocol\DigitalBrain.Protocol.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Register in `DigitalBrain.slnx`**

Add a new folder block after the existing `/Ino/` folder block:

```xml
  <Folder Name="/InoLang/">
    <Project Path="src/DigitalBrain.InoLang/DigitalBrain.InoLang.csproj" />
  </Folder>
```

- [ ] **Step 3: Build the project alone**

Run: `dotnet build src/DigitalBrain.InoLang/DigitalBrain.InoLang.csproj -c Debug -v minimal`
Expected: `Build succeeded` (empty project).

- [ ] **Step 4: Build the full solution**

Run: `dotnet build DigitalBrain.slnx -c Debug -v minimal`
Expected: `Build succeeded`.

- [ ] **Step 5: Commit**

```bash
git add src/DigitalBrain.InoLang/DigitalBrain.InoLang.csproj DigitalBrain.slnx
git commit -m "feat(inolang): add empty DigitalBrain.InoLang leaf project"
```

---

## Task 2: Add the language guard test (red until the move)

**Files:**
- Create: `src/DigitalBrain.InoLang.Tests/DigitalBrain.InoLang.Tests.csproj`
- Create: `src/DigitalBrain.InoLang.Tests/LanguageLeafTests.cs`
- Modify: `DigitalBrain.slnx`

This test references only InoLang and parses real `.ino`/`.yaml`. It will not compile until Task 3 moves the language in. Compile-red is the failing state.

- [ ] **Step 1: Write the test project file**

Create `src/DigitalBrain.InoLang.Tests/DigitalBrain.InoLang.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>preview</LangVersion>
    <IsPackable>false</IsPackable>
    <OutputType>Exe</OutputType>
    <UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>
    <IsTestProject>true</IsTestProject>
    <TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport>
    <NoWarn>$(NoWarn);ORLEANSEXP005</NoWarn>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit.v3" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\DigitalBrain.InoLang\DigitalBrain.InoLang.csproj" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Write the guard test**

Create `src/DigitalBrain.InoLang.Tests/LanguageLeafTests.cs`:

```csharp
using System.Linq;
using DigitalBrain.Core.Domain.Ino;
using DigitalBrain.Core.Domain.Yaml;
using Xunit;

namespace DigitalBrain.InoLang.Tests;

public class LanguageLeafTests
{
    private const string SampleIno =
        "name: memory\n" +
        "version: 1.0.0\n" +
        "desc: Memory\n" +
        "triggers: MemoryRecall\n" +
        "emits: MemoryRecallSynapse,UiSurface\n" +
        "observed-synapses: 0\n" +
        "\n" +
        "on: MemoryRecall\n" +
        "  show card( \"Memory $key\", column( text( \"$value\" ) ) )\n";

    private const string SampleYaml =
        "schemaVersion: \"os-on-yaml/v0\"\n" +
        "neuron:\n" +
        "  id: memory\n" +
        "  grainType: memory\n" +
        "  version: 1.0.0\n" +
        "  desc: Memory experience\n" +
        "  emits:\n" +
        "    - UiSurface\n" +
        "  observedSynapses: 0\n" +
        "  rules:\n" +
        "    - on: RememberSynapse\n" +
        "      do:\n" +
        "        - show:\n" +
        "            card:\n" +
        "              title: \"Remembered\"\n";

    [Fact]
    public void InoLang_assembly_does_not_reference_Core()
    {
        var asm = typeof(InoParser).Assembly;
        Assert.Equal("DigitalBrain.InoLang", asm.GetName().Name);
        Assert.DoesNotContain("DigitalBrain.Core", asm.GetReferencedAssemblies().Select(a => a.Name));
    }

    [Fact]
    public void InoParser_parses_name_emits_and_rule()
    {
        var exp = InoParser.Parse(SampleIno);

        Assert.Equal("memory", exp.Name);
        Assert.Equal("1.0.0", exp.Version);
        Assert.Contains("MemoryRecallSynapse", exp.Emits);
        Assert.Contains(exp.Rules, r => r.On == "MemoryRecall");
    }

    [Fact]
    public void YamlParser_parses_neuron_id_and_rule()
    {
        var exp = YamlParser.Parse(SampleYaml);

        Assert.NotNull(exp);
        Assert.Equal("memory", exp!.Name);
        Assert.Contains(exp.Rules, r => r.On == "RememberSynapse");
    }
}
```

- [ ] **Step 3: Register the test project in `DigitalBrain.slnx`**

Add inside the existing `/Tests/` folder:

```xml
    <Project Path="src/DigitalBrain.InoLang.Tests/DigitalBrain.InoLang.Tests.csproj" />
```

- [ ] **Step 4: Confirm the RED state**

Run: `dotnet build src/DigitalBrain.InoLang.Tests/DigitalBrain.InoLang.Tests.csproj -c Debug -v minimal`
Expected: FAIL — `error CS0234: The type or namespace name 'Domain'/'Ino'/'Yaml' does not exist in the namespace 'DigitalBrain.Core'` (the language still lives in Core, not InoLang). Capture those lines. Compile failure here is success for this task.

- [ ] **Step 5: Commit the red guard**

```bash
git add src/DigitalBrain.InoLang.Tests DigitalBrain.slnx
git commit -m "test(inolang): add language leaf guard (red until move)"
```

---

## Task 3: Move the five language files into InoLang

**Files:**
- Move: `src/DigitalBrain.Core/Domain/Ino/InoAst.cs`, `InoParser.cs`, `InoValidator.cs`, `RuleInterpreter.cs` → `src/DigitalBrain.InoLang/Domain/Ino/`
- Move: `src/DigitalBrain.Core/Domain/Yaml/YamlParser.cs` → `src/DigitalBrain.InoLang/Domain/Yaml/YamlParser.cs`
- Edit: `RuleInterpreter.cs` (remove dead `using DigitalBrain.Core.UI;`)
- Modify: `src/DigitalBrain.Core/DigitalBrain.Core.csproj`

- [ ] **Step 1: Move the files with git (namespaces unchanged)**

```bash
mkdir -p src/DigitalBrain.InoLang/Domain/Ino src/DigitalBrain.InoLang/Domain/Yaml
git mv src/DigitalBrain.Core/Domain/Ino/InoAst.cs        src/DigitalBrain.InoLang/Domain/Ino/InoAst.cs
git mv src/DigitalBrain.Core/Domain/Ino/InoParser.cs     src/DigitalBrain.InoLang/Domain/Ino/InoParser.cs
git mv src/DigitalBrain.Core/Domain/Ino/InoValidator.cs  src/DigitalBrain.InoLang/Domain/Ino/InoValidator.cs
git mv src/DigitalBrain.Core/Domain/Ino/RuleInterpreter.cs src/DigitalBrain.InoLang/Domain/Ino/RuleInterpreter.cs
git mv src/DigitalBrain.Core/Domain/Yaml/YamlParser.cs   src/DigitalBrain.InoLang/Domain/Yaml/YamlParser.cs
```

Do NOT change any `namespace` line.

- [ ] **Step 2: Remove the dead UI using from RuleInterpreter**

In `src/DigitalBrain.InoLang/Domain/Ino/RuleInterpreter.cs`, delete the single line:

```csharp
using DigitalBrain.Core.UI;
```

First confirm it is dead (no UI type used):
Run: `grep -nE "\b(UiSurface|UiWidget|MainPane|WindowFrame|BarChart|Graph3D|SurfacePlacement|Markdown|Hyperlink|ImageWidget|Button|Card|Column|Row|Divider|Icon|TextField|Progress|Toggle|Container)\b" src/DigitalBrain.InoLang/Domain/Ino/RuleInterpreter.cs`
Expected: no output (the only `Card`-ish types it uses are `CardItem`/`ShowCardIntent`, which are from `InoAst`/this file, not UI). If output appears naming a real UI widget type, STOP and report — the using is not dead and InoLang would need UI.

- [ ] **Step 3: Add the InoLang reference to Core**

In `src/DigitalBrain.Core/DigitalBrain.Core.csproj`, add to the existing `<ItemGroup>` that already references Protocol:

```xml
    <ProjectReference Include="..\DigitalBrain.InoLang\DigitalBrain.InoLang.csproj" />
```

- [ ] **Step 4: Build the full solution**

Run: `dotnet build DigitalBrain.slnx -c Debug -v minimal`
Expected: `Build succeeded`, 0 errors. Consumers compile unchanged (namespaces preserved). If a NEW error appears about a duplicate Ino type, a file was copied not moved; if about a missing type in InoLang, a needed file wasn't moved. Report BLOCKED with the exact error if unresolved.

- [ ] **Step 5: Run the full test suite (~3.5 min — long timeout, be patient)**

Run: `dotnet test src/DigitalBrain.Core.Tests/DigitalBrain.Core.Tests.csproj -c Debug --no-build`
Expected: `Failed: 0, Passed: 82, Skipped: 6, Total: 88`.

- [ ] **Step 6: Confirm move not copy**

Run: `ls src/DigitalBrain.Core/Domain/Ino/ src/DigitalBrain.Core/Domain/Yaml/ 2>&1`
Expected: the Ino directory is empty/absent and `YamlParser.cs` is gone from Core (the `Domain/Yaml` and `Domain/Ino` folders may be removed entirely).

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "refactor(inolang): move Ino/Yaml language into InoLang (namespaces unchanged; drop dead UI using)"
```

---

## Task 4: Turn the guard green and lock the boundary

**Files:** none (verification) unless a test exposes a real issue.

- [ ] **Step 1: Build the InoLang.Tests project — should now COMPILE**

Run: `dotnet build src/DigitalBrain.InoLang.Tests/DigitalBrain.InoLang.Tests.csproj -c Debug -v minimal`
Expected: `Build succeeded`.

- [ ] **Step 2: Run the language guard tests**

Run: `dotnet test src/DigitalBrain.InoLang.Tests/DigitalBrain.InoLang.Tests.csproj -c Debug --no-build`
Expected: 3 passed, 0 failed — including `InoLang_assembly_does_not_reference_Core`. If `InoParser_parses_name_emits_and_rule` or the yaml test fails on an assertion, the parser's actual output differs from the sample's expectation — report the actual values; do NOT weaken the leaf assertion.

- [ ] **Step 3: Run the full solution suite (several minutes — be patient)**

Run: `dotnet test DigitalBrain.slnx -c Debug`
Expected: Core.Tests 82/6/0, Protocol.Tests 3/0/0, InoLang.Tests 3/0/0, overall 0 failures. Capture per-project summary lines.

- [ ] **Step 4: Verify Core no longer owns the language**

Run: `ls src/DigitalBrain.Core/Domain/Ino/InoParser.cs src/DigitalBrain.Core/Domain/Yaml/YamlParser.cs 2>&1`
Expected: "No such file" for both.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit --allow-empty -m "test(inolang): language leaf guard green; boundary locked"
```

---

## Done-when (this plan)

- `DigitalBrain.InoLang` exists, references only Protocol + Orleans + YamlDotNet, and owns `InoAst`, `InoParser`, `InoValidator`, `RuleInterpreter`, `YamlParser`.
- `Core` references `InoLang`; all consumers compile unchanged (namespaces preserved).
- `LanguageLeafTests` proves InoLang does not reference Core and that `.ino`/`.yaml` actually parse.
- Full solution builds; Core.Tests at **82/6/0**; Protocol.Tests **3/0/0**; InoLang.Tests **3/0/0**.

## Deviations log (fill during execution)

- SourceGen intentionally NOT moved (kept for Plan 3 / os) — per spec decision 2026-06-17.
- (record any file that could not be moved cleanly and why)
