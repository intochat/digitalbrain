# Behavior Windows Sandbox: Admission and Failure Proof

> **Status:** Designed/current. This responsibility record is part of the [Windows sandbox plan index](2026-07-26-behavior-windows-sandbox.md); it does not authorize a live Behavior execution rail.

### Task 6: Run build/PE admission and BDD inside constrained processes

**Files:**
- Create: `src/DigitalBrain.Behaviors.Windows/Sandbox/BuildSandboxPolicy.cs`
- Modify: `src/DigitalBrain.Behaviors.Runtime/Compilation/DotNetBehaviorRevisionCompiler.cs`
- Modify: `src/DigitalBrain.Behaviors.Runtime/Verification/BehaviorRevisionVerifier.cs`
- Modify: `src/DigitalBrain.Behaviors.Runtime/Verification/IBehaviorTestHost.cs`
- Test: `tests/DigitalBrain.Behaviors.Tests/Windows/BehaviorAdmissionIsolation.cs`
- Modify: `tests/DigitalBrain.Behaviors.Tests/AdmissionEndToEnd.cs`

**Interfaces:**
- Consumes: Builder host, worker host, trusted Reqnroll bindings.
- Produces: `SandboxVerified` evidence and the first unknown revision eligible for owner approval.

- [ ] **Step 1: Write process-boundary and approval-gate tests**

```csharp
[WindowsFact]
public async Task UnknownRevisionBecomesEligibleOnlyAfterSandboxBdd()
{
    var admitted = await fixture.AdmitCommunityFixtureAsync();
    Assert.False(admitted.IsEligibleForOwnerApproval);
    var verified = await fixture.VerifyThroughSandboxAsync(admitted);
    Assert.Equal(BehaviorVerificationTrust.SandboxVerified, verified.Verification.Trust);
    Assert.True(verified.IsEligibleForOwnerApproval);
}
```

- [ ] **Step 2: Run tests and verify failure**

Run: `dotnet test tests/DigitalBrain.Behaviors.Tests/DigitalBrain.Behaviors.Tests.csproj -c Release --filter "FullyQualifiedName~BehaviorAdmissionIsolation|FullyQualifiedName~AdmissionEndToEnd"`

Expected: FAIL because admission/BDD do not use the launcher.

- [ ] **Step 3: Route builder and BDD through constrained policies**

The build policy is LPAC/no-network with read-only exact SDK/reference packs/local feed and bounded
child processes required by `dotnet restore/build`; it is not the runtime active-process-1 policy.
Metadata inspection stays inside Builder. Reqnroll remains a trusted test driver, but every
scenario execution calls the real sandbox worker through `IBehaviorTestHost`; proposal code is
never loaded into the test host.

Hash sandbox policy version, worker version, feature text, bindings version, and complete result
into verification evidence. Permit owner approval only for `SandboxVerified` or explicitly signed
`TrustedBuiltIn` evidence.

- [ ] **Step 4: Run admission/BDD tests**

Run: `dotnet test tests/DigitalBrain.Behaviors.Tests/DigitalBrain.Behaviors.Tests.csproj -c Release --filter "FullyQualifiedName~BehaviorAdmissionIsolation|FullyQualifiedName~AdmissionEndToEnd"`

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/DigitalBrain.Behaviors.Windows src/DigitalBrain.Behaviors.Runtime tests/DigitalBrain.Behaviors.Tests
git commit -m "feat(sandbox): verify unknown revisions through the production boundary"
```

### Task 7: Prove process, broker, and recovery failure modes

**Files:**
- Create: `tests/DigitalBrain.Compositions.Tests/Features/BehaviorSandboxRecovery.feature`
- Create: `tests/DigitalBrain.Behaviors.Tests/Windows/BehaviorSandboxLimits.cs`
- Create: `tests/DigitalBrain.Behaviors.Tests/Windows/BehaviorPipeAdversarial.cs`
- Create: `docs/architecture/behavior-windows-sandbox.md`
- Create: `docs/security/behavior-threat-model.md`
- Modify: `docs/architecture.md`
- Modify: `docs/index.md`

**Interfaces:**
- Consumes: Tasks 1–6 and kernel replay.
- Produces: proven local Windows sandbox tier, explicit hosted-tier limit, accurate current docs.

- [ ] **Step 1: Add recovery BDD**

```gherkin
Scenario: Worker dies after a capability result commits
  Given an unknown approved Behavior executes in LPAC
  And its first capability result is committed
  When the worker job is terminated before the response arrives
  And the execution lease is recovered
  Then the restarted worker receives the recorded result
  And the module effect count is one
```

- [ ] **Step 2: Add adversarial limit cases**

Cover memory, CPU, deadline, output, process count, cross-execution pipe, pipe squatting/server SID,
wrong credential/revision, oversized protobuf, malformed payload, broker shutdown, job-handle
close, artifact tamper, and neighbor-file access. Each case asserts a stable failure code and
terminal execution state.

- [ ] **Step 3: Run sandbox suites**

Run: `dotnet test tests/DigitalBrain.Behaviors.Tests/DigitalBrain.Behaviors.Tests.csproj -c Release --filter "FullyQualifiedName~Windows"; dotnet test tests/DigitalBrain.Compositions.Tests/DigitalBrain.Compositions.Tests.csproj -c Release --filter "FeatureTitle=Behavior sandbox recovery"`

Expected: PASS.

- [ ] **Step 4: Document the exact security claim**

Record the Win32 attributes, tested mitigation mask, Job limits, DACL, buffer/message caps, token
verification, artifact ACLs, credential lifecycle, and residual risks. State explicitly:

```text
Built: Windows local-owner/community-code LPAC tier.
Not claimed: robust hostile hosted multi-tenant isolation; that requires Hyper-V/VM isolation.
```

- [ ] **Step 5: Run the root gate and forbidden-authority searches**

Run:

```powershell
rg -n "DispatchProxy|MethodInfo\.Invoke|IClusterClient|Microsoft\.Orleans\.Client" hosts/DigitalBrain.BehaviorWorker src/DigitalBrain.Behaviors.Windows
dotnet format DigitalBrain.slnx --verify-no-changes
dotnet build DigitalBrain.slnx -c Release
dotnet test DigitalBrain.slnx -c Release --no-build
npm --prefix docs test
npm --prefix docs run build
git diff --check
```

Expected: search has no production matches; all gates exit `0`.

- [ ] **Step 6: Commit**

```powershell
git add src hosts tests docs Directory.Packages.props DigitalBrain.slnx
git commit -m "feat(sandbox): complete verified Windows behavior isolation"
```
