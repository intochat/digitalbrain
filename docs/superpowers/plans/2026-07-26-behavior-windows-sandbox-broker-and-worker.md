# Behavior Windows Sandbox: Broker and Worker

> **Status:** Designed/current. This responsibility record is part of the [Windows sandbox plan index](2026-07-26-behavior-windows-sandbox.md); it does not authorize a live Behavior execution rail.

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
