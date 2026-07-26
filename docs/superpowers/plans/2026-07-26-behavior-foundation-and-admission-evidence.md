# Behavior Foundation and Admission — Evidence

> **Status:** Designed/current. This responsibility record is part of the [foundation/admission plan index](2026-07-26-behavior-foundation-and-admission.md).

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
