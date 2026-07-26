# Behavior Foundation and Admission — SDK and identities

> **Status:** Designed/current. This responsibility record is part of the [foundation/admission plan index](2026-07-26-behavior-foundation-and-admission.md).

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
