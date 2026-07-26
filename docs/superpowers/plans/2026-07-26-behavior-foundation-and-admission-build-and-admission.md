# Behavior Foundation and Admission — Build and admission

> **Status:** Designed/current. This responsibility record is part of the [foundation/admission plan index](2026-07-26-behavior-foundation-and-admission.md).

### Task 4: Generate stable synapse and module capability catalogs

**Files:**
- Modify: `src/DigitalBrain.SourceGeneration/DispatchManifestGenerator.cs`
- Create: `src/DigitalBrain.SourceGeneration/BehaviorCapabilityGenerator.cs`
- Create: `src/DigitalBrain.SourceGeneration/Diagnostics/BehaviorCapabilityDiagnostics.cs`
- Create: `src/DigitalBrain.Behaviors/Capabilities/IBehaviorCapabilityClient.cs`
- Create: `src/DigitalBrain.Behaviors/Capabilities/BehaviorMethodDescriptor.cs`
- Test: `tests/DigitalBrain.Tests/SourceGeneration/BehaviorCapabilityGeneration.cs`
- Test: `tests/DigitalBrain.Tests/SourceGeneration/StableSynapseAliases.cs`

**Interfaces:**
- Consumes: Orleans `[Alias]`/`[Id]`, module `INeuron` interfaces, `Task`/`Task<T>`/`ValueTask`/`ValueTask<T>`.
- Produces: stable alias catalog, generated worker adapters, trusted invokers, and exact result codecs.

- [ ] **Step 1: Write generator golden tests**

```csharp
[Fact]
public void GeneratedCatalogUsesAliasInsteadOfClrFullName()
{
    var generated = GeneratorFixture.Run("""
        [Alias("sample.opened")]
        public sealed record Opened([property: Id(0)] string Id) : Synapse;
        """);
    Assert.Contains("\"sample.opened\"", generated.Source);
    Assert.DoesNotContain("Sample.Namespace.Opened", generated.Source);
}

[Fact]
public void UnsupportedCapabilitySignaturesFailCompilation()
{
    var result = GeneratorFixture.Run("public interface IBad : INeuron { Stream Open(); }");
    Assert.Contains(result.Diagnostics, d => d.Id == "DBB001");
}
```

- [ ] **Step 2: Run generator tests and verify failure**

Run: `dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release --filter "FullyQualifiedName~BehaviorCapabilityGeneration|FullyQualifiedName~StableSynapseAliases"`

Expected: FAIL because aliases and capability artifacts are not generated.

- [ ] **Step 3: Generate exact adapters and codecs**

Generate one descriptor per method:

```csharp
public sealed record BehaviorMethodDescriptor(
    string ContractAlias,
    string MethodAlias,
    string TargetGrainType,
    Type[] ParameterTypes,
    Type? ResultType);
```

Support only `Task`, `Task<T>`, `ValueTask`, and `ValueTask<T>` methods with Orleans-serializable
parameters plus an optional final `CancellationToken`. Generate worker adapters that call
`IBehaviorCapabilityClient.InvokeAsync` and trusted invokers that decode declared parameter types,
obtain the Orleans generated proxy, invoke it, and encode the declared result. Emit stable
diagnostics for overload alias collisions, unsupported returns, `ref`/`out`, pointers, generic
methods, missing aliases, and non-serializable types.

- [ ] **Step 4: Run generator and solution compilation tests**

Run: `dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release --filter "FullyQualifiedName~BehaviorCapabilityGeneration|FullyQualifiedName~StableSynapseAliases"`

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/DigitalBrain.SourceGeneration src/DigitalBrain.Behaviors tests/DigitalBrain.Tests
git commit -m "feat(sourcegen): generate exact behavior capability catalogs"
```

### Task 5: Build with the exact SDK and a vetted offline contract feed

**Files:**
- Create: `src/DigitalBrain.Behaviors.Runtime/Compilation/IBehaviorRevisionCompiler.cs`
- Create: `src/DigitalBrain.Behaviors.Runtime/Compilation/DotNetBehaviorRevisionCompiler.cs`
- Create: `src/DigitalBrain.Behaviors.Runtime/Compilation/ContractPackageCatalog.cs`
- Create: `src/DigitalBrain.Behaviors.Runtime/Compilation/BehaviorBuildWorkspace.cs`
- Create: `hosts/DigitalBrain.BehaviorBuilder/Program.cs`
- Create: `hosts/DigitalBrain.BehaviorBuilder/BuildCommand.cs`
- Create: `hosts/DigitalBrain.BehaviorBuilder/Policy/BannedSymbols.txt`
- Test: `tests/DigitalBrain.Behaviors.Tests/CompilerPipeline.cs`
- Test: `tests/DigitalBrain.Behaviors.Tests/ContractPackageAdmission.cs`

**Interfaces:**
- Consumes: canonical manifest, exact contract catalog, pinned SDK path, fresh workspace.
- Produces: `BehaviorCompilationReport CompileAsync(BehaviorCompilationRequest, CancellationToken)`.

- [ ] **Step 1: Write failing compilation and package-content tests**

```csharp
[Fact]
public async Task CompileUsesLockedOfflineRestoreAndProducesALibrary()
{
    var report = await fixture.Compiler.CompileAsync(
        FixtureProposal.StartUi,
        TestContext.Current.CancellationToken);
    Assert.True(report.Succeeded);
    Assert.Equal("10.0.302", report.Sdk.Version);
    Assert.True(report.Output.IsLibrary);
    Assert.DoesNotContain(report.Processes, p => p.Arguments.Contains(" run ", StringComparison.Ordinal));
}

[Theory]
[InlineData("build/Injected.targets")]
[InlineData("analyzers/dotnet/cs/Evil.dll")]
[InlineData("runtimes/win-x64/native/evil.dll")]
public void ContractCatalogRejectsExecutablePackageAssets(string entry)
    => Assert.False(ContractPackageFixture.WithEntry(entry).IsAdmissible);
```

- [ ] **Step 2: Run focused tests and verify failure**

Run: `dotnet test tests/DigitalBrain.Behaviors.Tests/DigitalBrain.Behaviors.Tests.csproj -c Release --filter "FullyQualifiedName~CompilerPipeline|FullyQualifiedName~ContractPackageAdmission"`

Expected: FAIL with missing compiler/catalog.

- [ ] **Step 3: Implement the supported CLI pipeline**

Materialize a workspace outside the repository and user profile containing exact `global.json`,
`NuGet.Config`, `.globalconfig`, `Directory.Build.props`, `BannedSymbols.txt`, generated
`Program.cs`, and `packages.lock.json`. Set:

```csharp
#:property TargetFramework=net10.0
#:property OutputType=Library
#:property PublishAot=false
#:property AllowUnsafeBlocks=false
```

Reject proposal directives before materialization. Run the exact `dotnet.exe` with
`UseShellExecute=false`:

```text
restore Program.cs --locked-mode --configfile NuGet.Config --packages <fresh-cache> --no-http-cache
build Program.cs --no-restore --no-incremental --configuration Release --output <fresh-output>
```

The catalog accepts only exact ID/version/hash contract packages with managed `ref/` or `lib/`
assemblies and no executable package assets. Record full `dotnet --info`, command arguments,
environment allowlist, lock hash, stdout/stderr, exit codes, and time limits.

- [ ] **Step 4: Run compiler tests**

Run: `dotnet test tests/DigitalBrain.Behaviors.Tests/DigitalBrain.Behaviors.Tests.csproj -c Release --filter "FullyQualifiedName~CompilerPipeline|FullyQualifiedName~ContractPackageAdmission"`

Expected: PASS, including a forced offline restore.

- [ ] **Step 5: Commit**

```powershell
git add src/DigitalBrain.Behaviors.Runtime hosts/DigitalBrain.BehaviorBuilder tests/DigitalBrain.Behaviors.Tests
git commit -m "feat(behaviors): compile revisions with the pinned offline SDK"
```

### Task 6: Enforce source policy and sandboxed PE admission

**Files:**
- Create: `src/DigitalBrain.SourceGeneration/BehaviorProgramAnalyzer.cs`
- Create: `hosts/DigitalBrain.BehaviorBuilder/Admission/BehaviorPeAdmission.cs`
- Create: `hosts/DigitalBrain.BehaviorBuilder/Admission/BehaviorSourceAdmission.cs`
- Create: `src/DigitalBrain.Behaviors.Runtime/Compilation/BehaviorAdmissionReport.cs`
- Test: `tests/DigitalBrain.Behaviors.Tests/SourceAdmission.cs`
- Test: `tests/DigitalBrain.Behaviors.Tests/PeAdmission.cs`

**Interfaces:**
- Consumes: compiler output in the constrained builder.
- Produces: a data-only admission report; no `Assembly`, Roslyn, MSBuild, or metadata-reader type crosses the process boundary.

- [ ] **Step 1: Add forbidden-source and malformed-PE tests**

```csharp
[Theory]
[InlineData("System.IO.File.ReadAllText(\"secret\")")]
[InlineData("System.Diagnostics.Process.Start(\"cmd\")")]
[InlineData("DateTimeOffset.UtcNow")]
[InlineData("Guid.NewGuid()")]
[InlineData("Task.Run(() => 1)")]
public async Task SourcePolicyRejectsAmbientAuthority(string expression)
    => Assert.Contains(
        (await fixture.CompileBodyAsync($"_ = {expression};")).Diagnostics,
        d => d.Code.StartsWith("DBB", StringComparison.Ordinal));

[Fact]
public async Task BuilderRejectsPInvokeAndNeverReturnsCandidateBytesToTheSilo()
    => Assert.Equal(
        "DBB104",
        (await fixture.AdmitPInvokeFixtureAsync()).Diagnostics.Single().Code);

[Fact]
public async Task ManifestSubscriptionsMustEqualProgramTriggerInterfaces()
{
    var report = await fixture.AdmitAsync(
        source: FixturePrograms.Handling("db.started"),
        declaredSubscriptions: ["mail.received"]);

    Assert.Contains(report.Diagnostics, d => d.Code == "DBB105");
}
```

- [ ] **Step 2: Run tests and verify failure**

Run: `dotnet test tests/DigitalBrain.Behaviors.Tests/DigitalBrain.Behaviors.Tests.csproj -c Release --filter "FullyQualifiedName~SourceAdmission|FullyQualifiedName~PeAdmission"`

Expected: FAIL because policy and PE admission do not exist.

- [ ] **Step 3: Implement defense-in-depth admission**

Inject BannedApiAnalyzers 5.6.0 through the trusted template. Add one first-party analyzer for one
public program type, approved program interfaces, no user assembly/module attributes, no unsafe,
extern, initializers, source generators, directives, or unsupported concurrency.

Inside Builder only, use `PEReader`/`MetadataReader` to require managed IL, expected assembly/TFM,
one program implementation, allowlisted assembly identities, deterministic marker, and no module
refs, linked files, native imports, unexpected resources, exported helper types, or type/module
initializers. Derive the complete event-subscription set from every closed
`IBehaviorProgram<TTrigger>` interface on that program type, resolve each `TTrigger` through the
generated stable synapse-alias catalog, ordinally sort the aliases, and require an exact match with
the event entries recorded in `manifest.json`. Reject missing aliases, duplicate aliases, open
generic trigger types, and any manifest/source disagreement. Apply CPU, memory, process, output,
and wall-clock limits even to metadata parsing.

- [ ] **Step 4: Run admission tests**

Run: `dotnet test tests/DigitalBrain.Behaviors.Tests/DigitalBrain.Behaviors.Tests.csproj -c Release --filter "FullyQualifiedName~SourceAdmission|FullyQualifiedName~PeAdmission"`

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/DigitalBrain.SourceGeneration src/DigitalBrain.Behaviors.Runtime hosts/DigitalBrain.BehaviorBuilder tests/DigitalBrain.Behaviors.Tests
git commit -m "feat(behaviors): enforce source and PE admission policy"
```
