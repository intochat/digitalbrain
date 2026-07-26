# Behavior Foundation and Admission Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the stable Behavior authoring contracts and a compile-once admission pipeline that produces immutable, content-addressed evidence without loading candidate code into the silo.

**Architecture:** `DigitalBrain.Behaviors` is the small packable SDK; `DigitalBrain.Behaviors.Runtime` owns trusted admission/storage adapters; `DigitalBrain.BehaviorBuilder` runs restore, build, analyzers, and PE checks in a child boundary. Artifact bytes are canonical, SHA-256-addressed, create-only in a separate blob container, and remain ineligible for approval until the sandbox plan supplies green BDD evidence.

**Tech Stack:** Microsoft .NET SDK 10.0.302 file-based apps and CLI, Microsoft.CodeAnalysis.BannedApiAnalyzers 5.6.0, System.Reflection.Metadata/PEReader, Reqnroll.xUnit.v3 3.3.4, Azure.Storage.Blobs 12.29.1, Aspire.Azure.Storage.Blobs 13.4.6, System.Security.Cryptography.Cose 10.0.10, JsonSchema.Net 9.3.0 behind a license gate.

## Global Constraints

- Candidate source is one `.cs` file and contains no user-controlled `#:` directives.
- Repository and Behavior compilation use Microsoft .NET SDK `10.0.302`, `rollForward: disable`, and `allowPrerelease: false`; install that supported SDK before execution because the current machine exposes only a 10.0.400 preview build.
- The exact installed .NET SDK executable is the only compiler; do not embed Roslyn or call `dotnet run`/`dotnet pack`.
- Restore uses a fresh package cache, a read-only vetted local feed, `packages.lock.json`, and a second clean `--locked-mode` pass with no network.
- Contract packages may contain managed contract assemblies only; reject build targets, analyzers, tools, content, native/runtime assets, and provider implementations.
- Compiler warnings and policy diagnostics are errors.
- `PEReader` and `MetadataReader` run only inside the constrained builder process; the silo never parses candidate PE/PDB bytes.
- A proposal may supply `.feature` text but never C# bindings, hooks, plugins, or test configuration.
- The unsigned deterministic artifact envelope is the revision identity; a detached COSE signature is provenance only.
- The journal is authority; Blob Storage is an untrusted immutable byte store in a container separate from the journal.
- Intent schemas require draft 2020-12, are self-contained, reject unknown keywords/remote references, and are hidden behind `IIntentSchemaValidator`.
- `JsonSchema.Net` 9.3.0 may be restored only after `eng/approved-dependencies.json` records explicit acceptance of its binary EULA.

---

### Task 1: Add the SDK, runtime, builder, and focused test boundaries

**Files:**
- Modify: `global.json`
- Modify: `Directory.Packages.props`
- Modify: `DigitalBrain.slnx`
- Create: `src/DigitalBrain.Behaviors/DigitalBrain.Behaviors.csproj`
- Create: `src/DigitalBrain.Behaviors.Runtime/DigitalBrain.Behaviors.Runtime.csproj`
- Create: `hosts/DigitalBrain.BehaviorBuilder/DigitalBrain.BehaviorBuilder.csproj`
- Create: `tests/DigitalBrain.Behaviors.Tests/DigitalBrain.Behaviors.Tests.csproj`
- Modify: `tests/DigitalBrain.Tests/Packages/PackageInventory.cs`
- Modify: `tests/DigitalBrain.Tests/Packages/PackableProjects.cs`
- Test: `tests/DigitalBrain.Tests/Boundary/BehaviorPackageBoundaries.cs`

**Interfaces:**
- Consumes: `DigitalBrain.Abstractions`, existing central package management, root boundary-test helpers.
- Produces: Packable author SDK, non-packable trusted runtime/builder, and one focused test project.

- [ ] **Step 1: Write the failing package-boundary test**

```csharp
[Fact(DisplayName = "Behavior SDK is Abstractions-only; builder and runtime never flow into module contracts")]
public void BehaviorPackageBoundariesAreOneWay()
{
    Assert.Equal(
        [PackageInventory.Abstractions],
        PackageBoundarySupport.DirectCompileProjectReferencesOf(PackageInventory.Behaviors));
    Assert.Empty(
        PackageBoundarySupport.DirectPackageReferencesOf(PackageInventory.Behaviors));
}
```

- [ ] **Step 2: Run the focused test and observe the missing projects**

Run: `dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release --filter "DisplayName~Behavior SDK is Abstractions-only"`

Expected: FAIL because `PackageInventory.Behaviors` and the projects do not exist.

- [ ] **Step 3: Add exact central versions and project references**

Pin the supported SDK:

```json
{
  "sdk": {
    "version": "10.0.302",
    "rollForward": "disable",
    "allowPrerelease": false
  }
}
```

```xml
<PackageVersion Include="Microsoft.CodeAnalysis.BannedApiAnalyzers" Version="5.6.0" />
<PackageVersion Include="Microsoft.Orleans.Serialization" Version="10.2.2-rc.2" />
<PackageVersion Include="Azure.Storage.Blobs" Version="12.29.1" />
<PackageVersion Include="Aspire.Azure.Storage.Blobs" Version="13.4.6" />
<PackageVersion Include="System.Security.Cryptography.Cose" Version="10.0.10" />
```

Make `DigitalBrain.Behaviors` packable and reference only `DigitalBrain.Abstractions`. Make Runtime
reference Behaviors, Kernel, Azure Blob, and COSE. Do not add or restore the schema package before
Task 7's recorded license decision. Make Builder reference Behaviors and
`Microsoft.Orleans.Serialization`; it must not reference Kernel, Orleans Client, Azure, providers,
or module runtimes.

- [ ] **Step 4: Run package tests**

Run: `dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release --filter "FullyQualifiedName~BehaviorPackageBoundaries|FullyQualifiedName~PackableProjects"`

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add global.json Directory.Packages.props DigitalBrain.slnx src/DigitalBrain.Behaviors src/DigitalBrain.Behaviors.Runtime hosts/DigitalBrain.BehaviorBuilder tests/DigitalBrain.Behaviors.Tests tests/DigitalBrain.Tests
git commit -m "build(behaviors): establish admission package boundaries"
```

### Task 2: Add stable Behavior identities and the safe program SDK

**Files:**
- Create: `src/DigitalBrain.Abstractions/BehaviorId.cs`
- Create: `src/DigitalBrain.Abstractions/BehaviorRevisionId.cs`
- Create: `src/DigitalBrain.Abstractions/BehaviorExecutionId.cs`
- Create: `src/DigitalBrain.Behaviors/IBehaviorProgram.cs`
- Create: `src/DigitalBrain.Behaviors/IIntentProgram.cs`
- Create: `src/DigitalBrain.Behaviors/IBehaviorContext.cs`
- Create: `src/DigitalBrain.Behaviors/BehaviorExecutionMetadata.cs`
- Test: `tests/DigitalBrain.Behaviors.Tests/BehaviorIdentities.cs`
- Test: `tests/DigitalBrain.Behaviors.Tests/ProgramSurface.cs`

**Interfaces:**
- Consumes: `OwnerId`, `NeuronId`, `CommandId`, `Synapse`, module interfaces derived from `INeuron`.
- Produces: The roadmap identities and `IBehaviorProgram<TTrigger>`, `IIntentProgram<TRequest,TResponse>`, `IBehaviorContext`.

- [ ] **Step 1: Write identity and surface tests**

```csharp
[Theory]
[InlineData("com.digitalbrain.start-ui")]
[InlineData("community.alice.mail-triage")]
public void BehaviorIdsAcceptCanonicalDnsStyleNames(string value)
    => Assert.Equal(value, BehaviorId.Parse(value).Value);

[Theory]
[InlineData("StartUi")]
[InlineData("two..dots")]
[InlineData("space here")]
public void BehaviorIdsRejectNonCanonicalNames(string value)
    => Assert.Throws<FormatException>(() => BehaviorId.Parse(value));

[Fact]
public void ContextExposesNoInfrastructureAuthority()
{
    var exposed = typeof(IBehaviorContext).GetMembers()
        .SelectMany(MemberSignature.AllTypes)
        .ToArray();
    Assert.DoesNotContain(exposed, t =>
        t == typeof(IServiceProvider)
        || t.FullName is "Orleans.IGrainFactory" or "System.Net.Http.HttpClient");
}
```

- [ ] **Step 2: Run tests and verify failure**

Run: `dotnet test tests/DigitalBrain.Behaviors.Tests/DigitalBrain.Behaviors.Tests.csproj -c Release --filter "FullyQualifiedName~BehaviorIdentities|FullyQualifiedName~ProgramSurface"`

Expected: FAIL with missing types.

- [ ] **Step 3: Implement canonical IDs and the SDK**

```csharp
public interface IBehaviorContext
{
    BehaviorExecutionMetadata Execution { get; }
    DateTimeOffset UtcNow { get; }
    CommandId DeterministicCommandId(string purpose);
    TContract Get<TContract>(string name) where TContract : class, INeuron;
    ValueTask<T?> ReadStateAsync<T>(string key, CancellationToken cancellationToken = default);
    void SetState<T>(string key, T value);
}

public interface IBehaviorProgram<in TTrigger> where TTrigger : Synapse
{
    ValueTask ExecuteAsync(
        TTrigger trigger,
        IBehaviorContext context,
        CancellationToken cancellationToken);
}

public interface IIntentProgram<TRequest, TResponse>
{
    ValueTask<TResponse> ExecuteAsync(
        TRequest request,
        IBehaviorContext context,
        CancellationToken cancellationToken);
}
```

Validate `BehaviorId` as 3–128 lowercase ASCII characters, dot-separated labels, each label
starting/ending alphanumeric with internal hyphens. Validate `BehaviorRevisionId` as exactly 64
lowercase hexadecimal characters. Add Orleans `[GenerateSerializer]`, `[Alias]`, and `[Id]`
metadata to all cross-process/cross-grain records.

- [ ] **Step 4: Run focused tests**

Run: `dotnet test tests/DigitalBrain.Behaviors.Tests/DigitalBrain.Behaviors.Tests.csproj -c Release --filter "FullyQualifiedName~BehaviorIdentities|FullyQualifiedName~ProgramSurface"`

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/DigitalBrain.Abstractions src/DigitalBrain.Behaviors tests/DigitalBrain.Behaviors.Tests
git commit -m "feat(behaviors): define stable identities and safe program SDK"
```

### Task 3: Define canonical manifests, grants, schemas, and artifact envelopes

**Files:**
- Create: `src/DigitalBrain.Behaviors/Manifest/BehaviorDefinitionManifest.cs`
- Create: `src/DigitalBrain.Behaviors/Manifest/BehaviorEntryPoints.cs`
- Create: `src/DigitalBrain.Behaviors/Manifest/BehaviorCapabilityGrant.cs`
- Create: `src/DigitalBrain.Behaviors/Manifest/BehaviorResourceLimits.cs`
- Create: `src/DigitalBrain.Behaviors/Artifacts/BehaviorArtifactDigest.cs`
- Create: `src/DigitalBrain.Behaviors/Artifacts/BehaviorArtifactEnvelope.cs`
- Create: `src/DigitalBrain.Behaviors.Runtime/Artifacts/CanonicalArtifactWriter.cs`
- Create: `src/DigitalBrain.Behaviors.Runtime/Artifacts/CanonicalArtifactReader.cs`
- Test: `tests/DigitalBrain.Behaviors.Tests/CanonicalArtifacts.cs`

**Interfaces:**
- Consumes: IDs and SDK contracts from Task 2.
- Produces: `BehaviorDefinitionManifest`, `BehaviorArtifactDigest`, and deterministic envelope read/write.

- [ ] **Step 1: Write deterministic-envelope and hostile-ZIP tests**

```csharp
[Fact]
public async Task SameEvidenceProducesSameDigest()
{
    var first = await FixtureArtifacts.WriteAsync(order: ArtifactOrder.Forward);
    var second = await FixtureArtifacts.WriteAsync(order: ArtifactOrder.Reverse);
    Assert.Equal(first.Digest, second.Digest);
    Assert.Equal(first.Bytes, second.Bytes);
}

[Theory]
[InlineData("../escape.dll")]
[InlineData("/absolute.dll")]
[InlineData("ARTIFACT/behavior.dll")]
public async Task ReaderRejectsUnsafeOrCaseCollidingEntryNames(string entry)
    => await Assert.ThrowsAsync<BehaviorArtifactException>(
        () => FixtureArtifacts.ReadWithExtraEntryAsync(entry));
```

- [ ] **Step 2: Run tests and verify failure**

Run: `dotnet test tests/DigitalBrain.Behaviors.Tests/DigitalBrain.Behaviors.Tests.csproj -c Release --filter "FullyQualifiedName~CanonicalArtifacts"`

Expected: FAIL with missing artifact types.

- [ ] **Step 3: Implement the exact envelope**

Use BCL `ZipArchive` with `CompressionLevel.NoCompression`, fixed
`1980-01-01T00:00:00Z` timestamps, ordinally sorted names, canonical UTF-8 JSON, and these entries:

```text
manifest.json
program.cs
dependencies/packages.lock.json
artifact/Behavior.dll
artifact/Behavior.deps.json
features/<ordinal-name>.feature
evidence/compiler.json
evidence/admission.json
evidence/bdd.json
```

Reject absolute/traversal/link entries, duplicates including case-insensitive collisions, unknown
entries, more than 128 entries, any entry over 16 MiB, total expansion over 64 MiB, and trailing
bytes. Hash the complete unsigned envelope with `SHA256.HashData` and format lowercase hex.

- [ ] **Step 4: Run focused tests**

Run: `dotnet test tests/DigitalBrain.Behaviors.Tests/DigitalBrain.Behaviors.Tests.csproj -c Release --filter "FullyQualifiedName~CanonicalArtifacts"`

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/DigitalBrain.Behaviors src/DigitalBrain.Behaviors.Runtime tests/DigitalBrain.Behaviors.Tests
git commit -m "feat(behaviors): canonicalize immutable revision artifacts"
```

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

### Task 7: Validate intent schemas, provenance, and immutable blob storage

**Files:**
- Create: `eng/approved-dependencies.json`
- Create: `src/DigitalBrain.Behaviors.Runtime/Schemas/IIntentSchemaValidator.cs`
- Create: `src/DigitalBrain.Behaviors.Runtime/Schemas/JsonSchemaIntentValidator.cs`
- Create: `src/DigitalBrain.Behaviors.Runtime/Security/CoseArtifactSignatureVerifier.cs`
- Create: `src/DigitalBrain.Behaviors.Runtime/Storage/IBehaviorArtifactStore.cs`
- Create: `src/DigitalBrain.Behaviors.Runtime/Storage/AzureBlobBehaviorArtifactStore.cs`
- Modify: `src/DigitalBrain.Aspire.Hosting/DigitalBrainBuilder.cs`
- Modify: `src/DigitalBrain.Aspire.Hosting/DigitalBrainHostingExtensions.cs`
- Test: `tests/DigitalBrain.Behaviors.Tests/IntentSchemaProfile.cs`
- Test: `tests/DigitalBrain.Behaviors.Tests/ArtifactProvenance.cs`
- Test: `tests/DigitalBrain.Behaviors.Tests/ArtifactBlobStore.cs`

**Interfaces:**
- Consumes: canonical artifact digest and manifest schemas.
- Produces: `IIntentSchemaValidator`, `IArtifactSignatureVerifier`, `IBehaviorArtifactStore`.

- [ ] **Step 1: Record the dependency decision before restore**

Add a reviewed entry:

```json
{
  "JsonSchema.Net/9.3.0": {
    "license": "OSMFEEULA",
    "decision": "accepted",
    "scope": "DigitalBrain intent schema validation",
    "evidence": "owner approval recorded before first restore"
  }
}
```

If explicit owner/legal acceptance has not been obtained, stop this task without adding the
package reference.

After acceptance is recorded, add:

```xml
<PackageVersion Include="JsonSchema.Net" Version="9.3.0" />
```

- [ ] **Step 2: Write schema, signature, and blob race tests**

```csharp
[Theory]
[InlineData("https://example.com/remote.json")]
[InlineData("file:///c:/schema.json")]
public void SchemaProfileRejectsRemoteReferences(string reference)
    => Assert.False(fixture.Schemas.Compile(FixtureSchemas.WithRef(reference)).Succeeded);

[Fact]
public async Task ConcurrentCreateIsIdempotentOnlyForIdenticalBytes()
{
    await fixture.Store.PutAsync(fixture.Digest, fixture.Bytes(), TestContext.Current.CancellationToken);
    await fixture.Store.PutAsync(fixture.Digest, fixture.Bytes(), TestContext.Current.CancellationToken);
    await Assert.ThrowsAsync<BehaviorArtifactIntegrityException>(
        () => fixture.Store.PutAsync(fixture.Digest, fixture.DifferentBytes(), TestContext.Current.CancellationToken).AsTask());
}
```

- [ ] **Step 3: Run focused tests and verify failure**

Run: `dotnet test tests/DigitalBrain.Behaviors.Tests/DigitalBrain.Behaviors.Tests.csproj -c Release --filter "FullyQualifiedName~IntentSchemaProfile|FullyQualifiedName~ArtifactProvenance|FullyQualifiedName~ArtifactBlobStore"`

Expected: FAIL with missing adapters.

- [ ] **Step 4: Implement narrow adapters**

Require `$schema` exactly `https://json-schema.org/draft/2020-12/schema`; allow self-document
fragments only; reject unknown keywords, `pattern`, `patternProperties`, custom vocabularies,
remote IDs/refs, more than 64 KiB schema bytes, depth over 32, more than 256 properties, or more
than 64 combinator branches. Expose stable validation codes, not library result types.

Verify optional detached COSE Sign1 using ECDSA P-256/SHA-256 and external associated data
`DigitalBrain.BehaviorArtifact/v1`; keep publisher trust distinct from owner approval.

Add a separate Aspire blob child and connection name `behavior-artifacts`. Upload to
`sha256/{first-two-hex}/{digest}` with `IfNoneMatch = ETag.All`; on 412, download and hash; hash
every read before returning a scoped local lease.

- [ ] **Step 5: Run tests against Azurite**

Run: `dotnet test tests/DigitalBrain.Behaviors.Tests/DigitalBrain.Behaviors.Tests.csproj -c Release --filter "FullyQualifiedName~IntentSchemaProfile|FullyQualifiedName~ArtifactProvenance|FullyQualifiedName~ArtifactBlobStore"`

Expected: PASS, including create race, mismatched 412, and tampered-read rejection.

- [ ] **Step 6: Commit**

```powershell
git add eng/approved-dependencies.json src/DigitalBrain.Behaviors.Runtime src/DigitalBrain.Aspire.Hosting tests/DigitalBrain.Behaviors.Tests
git commit -m "feat(behaviors): validate schemas and store immutable artifacts"
```

### Task 8: Build the trusted Reqnroll verification vocabulary

**Files:**
- Create: `src/DigitalBrain.Behaviors.Runtime/Verification/IBehaviorTestHost.cs`
- Create: `src/DigitalBrain.Behaviors.Runtime/Verification/BehaviorRevisionVerifier.cs`
- Create: `tests/DigitalBrain.Behaviors.Tests/Features/BehaviorAdmission.feature`
- Create: `tests/DigitalBrain.Behaviors.Tests/Features/BehaviorSteps.cs`
- Create: `tests/DigitalBrain.Behaviors.Tests/reqnroll.json`
- Test: `tests/DigitalBrain.Behaviors.Tests/FeatureAdmission.cs`

**Interfaces:**
- Consumes: admitted artifact plus proposal-supplied feature text.
- Produces: trusted generic bindings and `VerifyAsync(BehaviorAdmittedArtifact, BehaviorFeature, CancellationToken)`.

- [ ] **Step 1: Write the admission feature**

```gherkin
Feature: Behavior revision evidence

  Scenario: Proposed features contain data but no executable bindings
    Given an admitted StartUi artifact
    And its proposal contains the activation feature
    When the trusted verifier materializes the test project
    Then only system-owned step bindings are compiled
    And the artifact is referenced without rebuilding it
```

- [ ] **Step 2: Run Reqnroll and verify failure**

Run: `dotnet test tests/DigitalBrain.Behaviors.Tests/DigitalBrain.Behaviors.Tests.csproj -c Release --filter "FeatureTitle=Behavior revision evidence"`

Expected: FAIL because the verifier and bindings do not exist.

- [ ] **Step 3: Implement the trusted vocabulary**

```csharp
public interface IBehaviorTestHost
{
    ValueTask<BehaviorTestResult> ExecuteAsync(
        BehaviorTestCase testCase,
        CancellationToken cancellationToken);
}
```

Allow feature steps for event/intent input, deterministic capability stub results, execution,
journal facts, capability requests, output, and failure facts. Reject proposal bindings, hooks,
plugins, `reqnroll.json`, project files, and generated code. Reference the exact admitted DLL in
the temporary test project; do not rebuild program source. Until the Windows sandbox plan plugs in
the production host, mark results `TrustedFixtureOnly` and forbid owner approval for unknown
revisions.

- [ ] **Step 4: Run Reqnroll and focused tests**

Run: `dotnet test tests/DigitalBrain.Behaviors.Tests/DigitalBrain.Behaviors.Tests.csproj -c Release --filter "FeatureTitle=Behavior revision evidence|FullyQualifiedName~FeatureAdmission"`

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/DigitalBrain.Behaviors.Runtime tests/DigitalBrain.Behaviors.Tests
git commit -m "test(behaviors): add trusted revision verification vocabulary"
```

### Task 9: Prove reproducibility and document the admitted-not-approved boundary

**Files:**
- Create: `tests/DigitalBrain.Behaviors.Tests/AdmissionEndToEnd.cs`
- Create: `docs/architecture/behavior-admission.md`
- Modify: `docs/architecture.md`
- Modify: `docs/index.md`

**Interfaces:**
- Consumes: Tasks 1–8.
- Produces: one admitted fixture artifact and current-state documentation that does not claim sandbox BDD or approval is built.

- [ ] **Step 1: Write the end-to-end test**

```csharp
[Fact]
public async Task ExactInputsProduceOneAdmittedDigestButCannotYetBeApproved()
{
    var first = await fixture.AdmitAsync(FixtureProposal.StartUi);
    var second = await fixture.AdmitAsync(FixtureProposal.StartUi);
    Assert.Equal(first.Digest, second.Digest);
    Assert.Equal(BehaviorVerificationTrust.TrustedFixtureOnly, first.Verification.Trust);
    Assert.False(first.IsEligibleForOwnerApproval);
}
```

- [ ] **Step 2: Run and verify failure**

Run: `dotnet test tests/DigitalBrain.Behaviors.Tests/DigitalBrain.Behaviors.Tests.csproj -c Release --filter "FullyQualifiedName~AdmissionEndToEnd"`

Expected: FAIL until the admission coordinator binds all evidence.

- [ ] **Step 3: Implement the coordinator and write current-state docs**

Document the exact artifact entries, CLI commands, contract package restrictions, schema profile,
digest/signature distinction, blob path, and the explicit state:

```text
Built: author SDK, compile/analysis pipeline, immutable artifacts, trusted BDD vocabulary.
Not built yet: hostile-code BDD/execution, owner approval, installation, event/intent runtime.
```

- [ ] **Step 4: Run the focused and root gates**

Run:

```powershell
dotnet test tests/DigitalBrain.Behaviors.Tests/DigitalBrain.Behaviors.Tests.csproj -c Release
dotnet format DigitalBrain.slnx --verify-no-changes
dotnet build DigitalBrain.slnx -c Release
dotnet test DigitalBrain.slnx -c Release --no-build
npm --prefix docs test
npm --prefix docs run build
git diff --check
```

Expected: all commands exit `0`.

- [ ] **Step 5: Commit**

```powershell
git add src hosts tests docs Directory.Packages.props DigitalBrain.slnx eng/approved-dependencies.json
git commit -m "feat(behaviors): complete immutable admission foundation"
```
