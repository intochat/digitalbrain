# Behavior Windows Sandbox Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Execute unknown admitted Behavior artifacts in a verified Windows LPAC process whose only useful authority is one bounded, per-execution capability-broker channel.

**Architecture:** A Windows-only adapter creates the runtime worker with `STARTUPINFOEX`, LPAC security capabilities, atomic Job assignment, child-process restriction, mitigation policy, and exact ACLs. The trusted host exposes unary Protobuf gRPC over a `LOCAL\` Kestrel named pipe; the worker loads only the admitted artifact and approved contract assemblies, uses standalone Orleans serialization, and exits after one execution.

**Tech Stack:** Self-contained .NET 10 `win-x64`, Microsoft.Windows.CsWin32 0.3.298, Windows AppContainer/LPAC and Job Objects, ASP.NET Core/Kestrel HTTP/2 named pipes, Grpc.AspNetCore/Grpc.Net.Client/Grpc.Tools 2.80.0, Google.Protobuf 3.31.1, Orleans Serialization 10.2.2-rc.2.

## Global Constraints

- LPAC plus a non-breakaway Job Object is the local-owner/community-code tier, not a hostile hosted multi-tenant boundary.
- Hosted mutually hostile tenants require Hyper-V isolation, a dedicated VM, or an equivalent proven boundary.
- Runtime workers are self-contained, one process per execution, have no child processes, and are terminated with the entire Job on deadline/protocol violation.
- A runtime worker receives no Orleans client/proxy, Azure credential, provider SDK, repository, signing key, general network, or writable artifact directory.
- Artifact and worker files are read/execute-only for the exact AppContainer SID; only a bounded execution temp/profile path is writable.
- Named pipe DACL grants only broker identity and the exact AppContainer SID; `CurrentUserOnly` alone is insufficient.
- One-use bootstrap authorization binds protocol version, execution, owner, Behavior, revision digest, nonce, and deadline.
- Protobuf is the fixed control envelope; opaque CLR payload bytes use standalone Orleans serialization with exact generated codecs.
- The trusted broker service never invokes module grains directly; it reenters `BehaviorNeuron`, then the non-Neuron broker grain and existing delegation filter path.
- Candidate BDD uses the same worker/sandbox/broker path as production execution.
- Non-Windows production registration fails closed; a fake launcher is test-only.

---

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

### Task 4: Host the exact-DACL Kestrel named-pipe broker

**Files:**
- Create: `src/DigitalBrain.Behaviors.Windows/Ipc/BehaviorBrokerEndpoint.cs`
- Create: `src/DigitalBrain.Behaviors.Windows/Ipc/BehaviorBrokerGrpcService.cs`
- Create: `src/DigitalBrain.Behaviors.Windows/Ipc/BehaviorBootstrapCredential.cs`
- Create: `src/DigitalBrain.Behaviors.Windows/Ipc/BehaviorPipeSecurity.cs`
- Create: `src/DigitalBrain.Behaviors.Windows/Hosting/BehaviorSandboxServiceCollectionExtensions.cs`
- Modify: `hosts/DigitalBrain.Host/Program.cs`
- Test: `tests/DigitalBrain.Behaviors.Tests/Windows/BehaviorBrokerPipe.cs`

**Interfaces:**
- Consumes: one execution lease and exact AppContainer SID.
- Produces: per-execution `LOCAL\DigitalBrain\<256-bit-random>` HTTP/2 endpoint and unary broker service.

- [ ] **Step 1: Write endpoint, DACL, handshake, and size-limit tests**

```csharp
[WindowsFact]
public async Task OnlyExactWorkerSidCanClaimTheExecution()
{
    await fixture.AssertConnectionRejectedAsync(fixture.OtherAppContainerSid);
    var start = await fixture.ConnectExpectedWorkerAsync();
    Assert.Equal(fixture.Execution, start.ExecutionId);
    await fixture.AssertSecondClaimRejectedAsync();
}

[WindowsFact]
public void BrokerOpensNoTcpListener()
    => Assert.Empty(fixture.ProcessTcpListeners());
```

- [ ] **Step 2: Run and verify failure**

Run: `dotnet test tests/DigitalBrain.Behaviors.Tests/DigitalBrain.Behaviors.Tests.csproj -c Release --filter "FullyQualifiedName~BehaviorBrokerPipe"`

Expected: FAIL because endpoint/service do not exist.

- [ ] **Step 3: Implement the pipe endpoint and service**

Use:

```csharp
options.ListenNamedPipe(pipeName, listen => listen.Protocols = HttpProtocols.Http2);
services.Configure<NamedPipeTransportOptions>(options =>
{
    options.CurrentUserOnly = true;
    options.PipeSecurity = BehaviorPipeSecurity.For(brokerSid, appContainerSid);
    options.MaxReadBufferSize = 256 * 1024;
    options.MaxWriteBufferSize = 256 * 1024;
});
```

Set gRPC request/response limits to 256 KiB and bounded deadlines. The one-use credential is passed
through inherited non-command-line launch data and zeroed after claim. The service validates every
identity field, then calls `BehaviorNeuron` to prepare/replay, invokes the exact non-Neuron broker
grain, and completes through a new correlated Behavior turn. It never resolves a module proxy
directly.

- [ ] **Step 4: Run pipe tests**

Run: `dotnet test tests/DigitalBrain.Behaviors.Tests/DigitalBrain.Behaviors.Tests.csproj -c Release --filter "FullyQualifiedName~BehaviorBrokerPipe"`

Expected: PASS, including malformed/oversized/late/duplicate request rejection.

- [ ] **Step 5: Commit**

```powershell
git add src/DigitalBrain.Behaviors.Windows hosts/DigitalBrain.Host tests/DigitalBrain.Behaviors.Tests
git commit -m "feat(sandbox): broker capabilities over a restricted named pipe"
```

### Task 5: Execute the admitted DLL in the one-shot worker

**Files:**
- Create: `hosts/DigitalBrain.BehaviorWorker/Program.cs`
- Create: `hosts/DigitalBrain.BehaviorWorker/Broker/NamedPipeBehaviorBrokerClient.cs`
- Create: `hosts/DigitalBrain.BehaviorWorker/Broker/PipeServerIdentityVerifier.cs`
- Create: `hosts/DigitalBrain.BehaviorWorker/Execution/BehaviorAssemblyLoader.cs`
- Create: `hosts/DigitalBrain.BehaviorWorker/Execution/BehaviorProgramRunner.cs`
- Create: `hosts/DigitalBrain.BehaviorWorker/Execution/WorkerBehaviorContext.cs`
- Create: `hosts/DigitalBrain.BehaviorWorker/Serialization/BehaviorSerializerProvider.cs`
- Test: `tests/DigitalBrain.Behaviors.Tests/Windows/BehaviorWorkerExecution.cs`

**Interfaces:**
- Consumes: exact admitted DLL, explicit approved contract assemblies, pipe/bootstrap handle.
- Produces: one claim → sequential calls → completion lifecycle and process exit.

- [ ] **Step 1: Write execution and type-confusion tests**

```csharp
[WindowsFact]
public async Task WorkerRunsExactArtifactAndReturnsCommittedCompletion()
{
    var outcome = await fixture.ExecuteStartUiAsync();
    Assert.Equal(BehaviorExecutionStatus.Completed, outcome.Status);
    Assert.Equal(1, outcome.CapabilityCalls);
}

[WindowsFact]
public async Task UnknownAliasOrConcreteTypeIsRejectedBeforeProgramEntry()
    => Assert.Equal("DBW203", (await fixture.ExecuteWithUnknownPayloadAliasAsync()).FailureCode);
```

- [ ] **Step 2: Run tests and verify failure**

Run: `dotnet test tests/DigitalBrain.Behaviors.Tests/DigitalBrain.Behaviors.Tests.csproj -c Release --filter "FullyQualifiedName~BehaviorWorkerExecution"`

Expected: FAIL because worker runtime does not exist.

- [ ] **Step 3: Implement standalone serialization and one-shot execution**

```csharp
services.AddSerializer(builder =>
{
    builder.AddAssembly(typeof(Synapse).Assembly);
    foreach (var assembly in approvedContractAssemblies)
        builder.AddAssembly(assembly);
});
```

Validate server owner SID before sending the credential. Create one `GrpcChannel` with
`SocketsHttpHandler.ConnectCallback` and `NamedPipeClientStream`, reuse it for all unary calls, and
use anonymous/no impersonation. Load the exact main DLL in one non-collectible
`AssemblyLoadContext` backed by `AssemblyDependencyResolver`; return the already loaded Behavior SDK
and approved contract assemblies from the default context so type identity is shared, and reject
every unmanaged resolution. Instantiate the single admitted program type, execute one matching
event/intent entry point, and submit state delta plus output. Refuse every missing/unapproved
dependency. Dispose and exit; do not add worker pooling or collectible-load-context reuse.

- [ ] **Step 4: Publish and run worker tests**

Run:

```powershell
dotnet publish hosts/DigitalBrain.BehaviorWorker/DigitalBrain.BehaviorWorker.csproj -c Release -r win-x64 --self-contained true
dotnet test tests/DigitalBrain.Behaviors.Tests/DigitalBrain.Behaviors.Tests.csproj -c Release --filter "FullyQualifiedName~BehaviorWorkerExecution"
```

Expected: publish and tests PASS.

- [ ] **Step 5: Commit**

```powershell
git add hosts/DigitalBrain.BehaviorWorker tests/DigitalBrain.Behaviors.Tests
git commit -m "feat(worker): execute one admitted behavior per sandbox process"
```

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
