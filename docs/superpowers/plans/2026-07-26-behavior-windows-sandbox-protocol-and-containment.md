# Behavior Windows Sandbox: Protocol and Containment

> **Status:** Designed/current. This responsibility record is part of the [Windows sandbox plan index](2026-07-26-behavior-windows-sandbox.md); it does not authorize a live Behavior execution rail.

### Task 1: Add the fixed Protobuf protocol and package boundaries

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `DigitalBrain.slnx`
- Create: `src/DigitalBrain.Behaviors.Protocol/DigitalBrain.Behaviors.Protocol.csproj`
- Create: `src/DigitalBrain.Behaviors.Protocol/Protos/behavior_broker.proto`
- Create: `src/DigitalBrain.Behaviors.Windows/DigitalBrain.Behaviors.Windows.csproj`
- Create: `hosts/DigitalBrain.BehaviorWorker/DigitalBrain.BehaviorWorker.csproj`
- Test: `tests/DigitalBrain.Tests/Boundary/BehaviorWorkerBoundaries.cs`
- Test: `tests/DigitalBrain.Behaviors.Tests/BrokerProtocolCompatibility.cs`

**Interfaces:**
- Consumes: execution/revision/call identities and opaque bytes from the foundation/kernel plans.
- Produces: generated `BehaviorBroker` client/base plus stable v1 messages.

- [ ] **Step 1: Write worker authority and protocol-field tests**

```csharp
[Fact(DisplayName = "Behavior worker contains protocol, SDK, and contract codecs — never cluster or providers")]
public void WorkerHasNoInfrastructureAuthority()
{
    PackageBoundarySupport.AssertDoesNotReference(
        PackageInventory.BehaviorWorker,
        "Microsoft.Orleans.Client", "Azure.", "OpenAI", "ModelContextProtocol");
}

[Fact]
public void ProtocolV1PreservesExecutionAndReplayBindingFields()
    => ProtoAssert.Message<CapabilityCall>(
        "protocol_version", "execution_id", "revision_digest",
        "call_ordinal", "request_fingerprint", "contract_alias",
        "method_alias", "target_neuron", "payload");
```

- [ ] **Step 2: Run tests and verify failure**

Run: `dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release --filter "FullyQualifiedName~BehaviorWorkerBoundaries"; dotnet test tests/DigitalBrain.Behaviors.Tests/DigitalBrain.Behaviors.Tests.csproj -c Release --filter "FullyQualifiedName~BrokerProtocolCompatibility"`

Expected: FAIL because projects and messages do not exist.

- [ ] **Step 3: Add exact packages and protocol**

```xml
<PackageVersion Include="Grpc.AspNetCore" Version="2.80.0" />
<PackageVersion Include="Grpc.Net.Client" Version="2.80.0" />
<PackageVersion Include="Grpc.Tools" Version="2.80.0" />
<PackageVersion Include="Grpc.Core.Api" Version="2.80.0" />
<PackageVersion Include="Google.Protobuf" Version="3.31.1" />
<PackageVersion Include="Microsoft.Windows.CsWin32" Version="0.3.298" />
```

Define unary RPCs:

```proto
service BehaviorBroker {
  rpc ClaimExecution(ClaimExecutionRequest) returns (ExecutionStart);
  rpc InvokeCapability(CapabilityCall) returns (CapabilityResult);
  rpc CompleteExecution(ExecutionCompletion) returns (ExecutionAck);
}
```

Use `bytes` for IDs/digests/fingerprints/payloads, explicit `uint32 protocol_version`, and bounded
failure codes/details. Reserve removed field numbers; never reuse them.

- [ ] **Step 4: Run boundary and protocol tests**

Run: `dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release --filter "FullyQualifiedName~BehaviorWorkerBoundaries"; dotnet test tests/DigitalBrain.Behaviors.Tests/DigitalBrain.Behaviors.Tests.csproj -c Release --filter "FullyQualifiedName~BrokerProtocolCompatibility"`

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add Directory.Packages.props DigitalBrain.slnx src/DigitalBrain.Behaviors.Protocol src/DigitalBrain.Behaviors.Windows hosts/DigitalBrain.BehaviorWorker tests
git commit -m "build(behaviors): add fixed sandbox broker protocol"
```

### Task 2: Prove LPAC launch, atomic Job containment, and token evidence

**Files:**
- Create: `src/DigitalBrain.Behaviors.Windows/NativeMethods.txt`
- Create: `src/DigitalBrain.Behaviors.Windows/Sandbox/WindowsBehaviorSandbox.cs`
- Create: `src/DigitalBrain.Behaviors.Windows/Sandbox/AppContainerProfile.cs`
- Create: `src/DigitalBrain.Behaviors.Windows/Sandbox/BehaviorJob.cs`
- Create: `src/DigitalBrain.Behaviors.Windows/Sandbox/ProcessAttributeList.cs`
- Create: `src/DigitalBrain.Behaviors.Windows/Sandbox/WorkerTokenEvidence.cs`
- Create: `src/DigitalBrain.Behaviors.Windows/Sandbox/RuntimeSandboxPolicy.cs`
- Test: `tests/DigitalBrain.Behaviors.Tests/Windows/WindowsSandboxBoundary.cs`

**Interfaces:**
- Consumes: verified worker path, revision directory, execution limits.
- Produces: `IBehaviorSandbox.LaunchAsync(ApprovedBehaviorExecution, CancellationToken)`.

- [ ] **Step 1: Write real Windows negative tests**

```csharp
[WindowsFact]
public async Task RuntimeWorkerIsLpacAndAtomicallyBoundToOneProcessJob()
{
    await using var worker = await fixture.LaunchAsync();
    Assert.True(worker.Token.IsAppContainer);
    Assert.True(worker.Token.IsLessPrivilegedAppContainer);
    Assert.Equal(fixture.ExpectedAppContainerSid, worker.Token.AppContainerSid);
    Assert.Equal(1u, worker.Job.ActiveProcessLimit);
}

[WindowsFact]
public async Task WorkerCannotSpawnReadParentProfileOrReachNetwork()
{
    var result = await fixture.RunNegativeAccessProbeAsync();
    Assert.Equal(AccessDenied, result.ParentFile);
    Assert.Equal(AccessDenied, result.ChildProcess);
    Assert.Equal(AccessDenied, result.Network);
}
```

- [ ] **Step 2: Run tests and verify failure**

Run: `dotnet test tests/DigitalBrain.Behaviors.Tests/DigitalBrain.Behaviors.Tests.csproj -c Release --filter "FullyQualifiedName~WindowsSandboxBoundary"`

Expected: FAIL because launcher and token proof do not exist.

- [ ] **Step 3: Generate only required Win32 APIs**

Include profile, SID, token, attribute-list, process, job, ACL, security-descriptor, and termination
functions in `NativeMethods.txt`. Keep pointer/union/handle manipulation inside the Windows
assembly and own every native handle with generated safe handles.

Create the runtime process once with `CreateProcessW` and an exact application path. Populate:

```text
PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIES
PROC_THREAD_ATTRIBUTE_ALL_APPLICATION_PACKAGES_POLICY = OPT_OUT
PROC_THREAD_ATTRIBUTE_JOB_LIST
PROC_THREAD_ATTRIBUTE_CHILD_PROCESS_POLICY = RESTRICTED
PROC_THREAD_ATTRIBUTE_MITIGATION_POLICY = versioned proven mask
```

Configure Job limits: kill-on-close, active process `1`, process/job memory caps, CPU hard cap, no
breakaway. Verify token evidence before releasing execution input. Call `TerminateJobObject` on
deadline, cancellation, protocol violation, or broker shutdown.

- [ ] **Step 4: Run containment tests**

Run: `dotnet test tests/DigitalBrain.Behaviors.Tests/DigitalBrain.Behaviors.Tests.csproj -c Release --filter "FullyQualifiedName~WindowsSandboxBoundary"`

Expected: PASS on supported Windows; non-Windows asserts production registration throws
`PlatformNotSupportedException`.

- [ ] **Step 5: Commit**

```powershell
git add src/DigitalBrain.Behaviors.Windows tests/DigitalBrain.Behaviors.Tests
git commit -m "feat(sandbox): launch behavior workers in verified LPAC jobs"
```

### Task 3: Stage immutable worker/artifact files with exact ACLs

**Files:**
- Create: `src/DigitalBrain.Behaviors.Windows/Storage/BehaviorExecutionDirectory.cs`
- Create: `src/DigitalBrain.Behaviors.Windows/Storage/BehaviorArtifactStager.cs`
- Create: `src/DigitalBrain.Behaviors.Windows/Security/BehaviorFileAcl.cs`
- Test: `tests/DigitalBrain.Behaviors.Tests/Windows/BehaviorArtifactAcl.cs`

**Interfaces:**
- Consumes: digest-verified artifact lease and AppContainer SID.
- Produces: atomically exposed read/execute revision directory plus bounded writable temp directory.

- [ ] **Step 1: Write ACL, tamper, and path tests**

```csharp
[WindowsFact]
public async Task WorkerCanReadExactRevisionButCannotWriteOrSeeNeighbors()
{
    await using var staged = await fixture.StageAsync();
    Assert.Equal(Success, await staged.ProbeAsync("artifact/Behavior.dll", FileAccess.Read));
    Assert.Equal(AccessDenied, await staged.ProbeAsync("artifact/Behavior.dll", FileAccess.Write));
    Assert.Equal(AccessDenied, await staged.ProbeNeighborAsync());
}
```

- [ ] **Step 2: Run and verify failure**

Run: `dotnet test tests/DigitalBrain.Behaviors.Tests/DigitalBrain.Behaviors.Tests.csproj -c Release --filter "FullyQualifiedName~BehaviorArtifactAcl"`

Expected: FAIL with missing stager.

- [ ] **Step 3: Implement stage–verify–ACL–expose**

Create a fresh staging directory under a configured sandbox root, extract through
`CanonicalArtifactReader`, re-hash every executable payload against the manifest, set explicit
DACLs, and atomically rename to the digest directory. Grant broker/service full control, the exact
LPAC SID read/execute on worker+revision, and write only on a capped temp/profile directory.
Reject reparse points, hard links, inherited broad ACEs, unexpected alternate streams, and
case-colliding names.

- [ ] **Step 4: Run storage boundary tests**

Run: `dotnet test tests/DigitalBrain.Behaviors.Tests/DigitalBrain.Behaviors.Tests.csproj -c Release --filter "FullyQualifiedName~BehaviorArtifactAcl"`

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/DigitalBrain.Behaviors.Windows tests/DigitalBrain.Behaviors.Tests
git commit -m "feat(sandbox): stage immutable behavior execution files"
```
