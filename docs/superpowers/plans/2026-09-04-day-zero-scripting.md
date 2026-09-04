# Day-Zero Scripting Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `scripts/start.cs` the first programmable behavior: after the kernel durably activates an owner brain, a separate Aspire-hosted scripting worker observes that activation, runs the current script version exactly once, and durably records a structured outcome.

**Architecture:** The Orleans kernel remains the trusted lifecycle and neuron substrate. `DigitalBrain.Scripting` is a sibling worker process that connects through the existing public `IDigitalBrain` client with `activateOnStart: false`. It reads the root outgoing journal as the activation source of truth, executes C# through Roslyn in its own process, and stores an append-only execution receipt keyed by owner + activation signal id + script SHA-256. This slice deliberately does not add `Publish`, `Subscribe`, `Run`, a behavior registry, or generated grain types.

**Tech Stack:** .NET 11, C# 14/latest, .NET Generic Host, Aspire 13.5.2, Orleans 10.2.2, Roslyn `Microsoft.CodeAnalysis.CSharp.Scripting` 5.9.0, System.Text.Json, xUnit v3.

**Spec:** `docs/superpowers/specs/2026-09-04-scripted-behaviors-design.md`

## Global Constraints

- Keep `DigitalBrain.Contracts` limited to stable public wire/client contracts. No scripting runtime types go there.
- Do not add a reference from `DigitalBrain` or `DigitalBrain.Silo` to `DigitalBrain.Scripting`.
- `start.cs` never calls `ActivateAsync` and never constructs `DigitalBrainActivated`.
- The worker is a trusted-development proof, not a security sandbox. State that clearly in code/docs; do not imply isolation stronger than a separate process.
- Observe activation from the durable outgoing journal, including activations written before the worker connects.
- Never retry the same `(owner, activation signal, script hash)` automatically, regardless of success or failure.
- Preserve cancellation all the way through journal watching and Roslyn execution.
- Make no unrelated abstraction or module refactors in this slice.

---

## Task 1: Remove known scaffold trash and restore the stable activation contract

**Files:**

- Delete: `src/Kernel/DigitalBrain.Contracts/Activated.cs`
- Delete: `src/Kernel/DigitalBrain/IKernel.cs`
- Delete: `src/Kernel/DigitalBrain.Kernel/DigitalBrain.Kernel/Class1.cs`
- Delete: `src/Kernel/DigitalBrain.Kernel/DigitalBrain.Kernel/DigitalBrain.Kernel.csproj`
- Modify: `src/Kernel/DigitalBrain.Contracts/Signals/DigitalBrainActivated.cs`
- Modify: `DigitalBrain.slnx`
- Test: `tests/DigitalBrain.Simulation.Tests/ContractOwnershipTests.cs`

**Interfaces consumed:** Existing `DigitalBrainActivated` serialization contract and solution structure.

**Interfaces produced:** One canonical activation signal with the stable wire alias `db.digitalbrain-activated`; no empty `IKernel`, duplicate `Activated`, or empty `DigitalBrain.Kernel` project.

- [ ] **Step 1: Pin the cleanup with the existing contract test**

Confirm the test already expresses the invariant:

```csharp
AssertAlias<DigitalBrainActivated>("db.digitalbrain-activated");
```

- [ ] **Step 2: Run the focused test and verify it fails**

Run:

```powershell
dotnet test tests/DigitalBrain.Simulation.Tests/DigitalBrain.Simulation.Tests.csproj --filter "FullyQualifiedName~ContractOwnershipTests" --no-restore
```

Expected: failure showing actual alias `DigitalBrainActivated` instead of `db.digitalbrain-activated`.

- [ ] **Step 3: Restore the stable alias and remove the dead types/projects**

Use the fixed alias:

```csharp
[GenerateSerializer]
[Alias("db.digitalbrain-activated")]
public sealed record DigitalBrainActivated([property: Id(0)] OwnerId Owner) : Signal;
```

Remove the three scaffold files. Remove the `DigitalBrain.Kernel` project entry from the solution. Rename solution folder `/Modules/DogitalBrain/` to `/Modules/DigitalBrain/`.

- [ ] **Step 4: Run the focused test and solution build**

Run:

```powershell
dotnet test tests/DigitalBrain.Simulation.Tests/DigitalBrain.Simulation.Tests.csproj --filter "FullyQualifiedName~ContractOwnershipTests" --no-restore
dotnet build DigitalBrain.slnx --no-restore
```

Expected: both pass with zero errors and zero warnings.

- [ ] **Step 5: Commit**

```powershell
git add DigitalBrain.slnx src/Kernel/DigitalBrain.Contracts src/Kernel/DigitalBrain src/Kernel/DigitalBrain.Kernel
git commit -m "refactor: remove duplicate kernel scaffolding"
```

---

## Task 2: Define the worker-owned execution model and durable ledger

**Files:**

- Modify: `Directory.Packages.props`
- Modify: `src/Kernel/DigitalBrain.Scripting/DigitalBrain.Scripting.csproj`
- Create: `src/Kernel/DigitalBrain.Scripting/Startup/StartupActivation.cs`
- Create: `src/Kernel/DigitalBrain.Scripting/Startup/StartupScript.cs`
- Create: `src/Kernel/DigitalBrain.Scripting/Startup/StartupExecution.cs`
- Create: `src/Kernel/DigitalBrain.Scripting/Startup/IStartupExecutionLedger.cs`
- Create: `src/Kernel/DigitalBrain.Scripting/Startup/FileStartupExecutionLedger.cs`
- Create: `tests/DigitalBrain.Scripting.Tests/DigitalBrain.Scripting.Tests.csproj`
- Create: `tests/DigitalBrain.Scripting.Tests/FileStartupExecutionLedgerTests.cs`
- Modify: `DigitalBrain.slnx`

**Interfaces consumed:** `OwnerId`, `SignalId`, local filesystem, System.Text.Json.

**Interfaces produced:** Small immutable records for activation, script content/version, and execution outcome; a replaceable idempotency ledger.

- [ ] **Step 1: Add the test project and write failing persistence tests**

The test project references `DigitalBrain.Scripting`, uses `xunit.v3.mtp-v2`, and is added under `/Tests/`. Add `InternalsVisibleTo` in the worker project.

Write these tests:

```csharp
[Fact]
public async Task Recorded_execution_is_visible_to_a_new_ledger_instance()
{
    var directory = Directory.CreateTempSubdirectory("digitalbrain-scripting-");
    try
    {
        var key = new StartupExecutionKey("owner", "signal", "sha256");
        var execution = StartupExecution.Succeeded(key, "started", DateTimeOffset.UnixEpoch);

        await new FileStartupExecutionLedger(directory.FullName).RecordAsync(execution, TestContext.Current.CancellationToken);

        var restored = await new FileStartupExecutionLedger(directory.FullName)
            .FindAsync(key, TestContext.Current.CancellationToken);
        Assert.Equal(execution, restored);
    }
    finally
    {
        directory.Delete(recursive: true);
    }
}

[Fact]
public async Task Recording_the_same_key_twice_keeps_the_first_terminal_outcome()
{
    // Record success, then attempt a failure for the same key.
    // Assert FindAsync returns the original success.
}
```

- [ ] **Step 2: Run the new tests and verify they fail to compile**

Run:

```powershell
dotnet test tests/DigitalBrain.Scripting.Tests/DigitalBrain.Scripting.Tests.csproj
```

Expected: missing startup execution and ledger types.

- [ ] **Step 3: Implement the minimal execution records**

Use these shapes:

```csharp
internal sealed record StartupActivation(string Owner, string SignalId);

internal sealed record StartupScript(string Path, string Source, string Sha256)
{
    public static async Task<StartupScript> ReadAsync(string path, CancellationToken cancellationToken);
}

internal readonly record struct StartupExecutionKey(
    string Owner,
    string ActivationSignalId,
    string ScriptSha256);

internal sealed record StartupExecution(
    StartupExecutionKey Key,
    bool IsSuccess,
    string Summary,
    IReadOnlyList<string> Diagnostics,
    DateTimeOffset CompletedAt)
{
    public static StartupExecution Succeeded(StartupExecutionKey key, string summary, DateTimeOffset completedAt);
    public static StartupExecution Failed(StartupExecutionKey key, string summary, IReadOnlyList<string> diagnostics, DateTimeOffset completedAt);
}

internal interface IStartupExecutionLedger
{
    Task<StartupExecution?> FindAsync(StartupExecutionKey key, CancellationToken cancellationToken);
    Task RecordAsync(StartupExecution execution, CancellationToken cancellationToken);
}
```

`StartupScript.ReadAsync` reads exact UTF-8 source bytes and sets `Sha256` to lowercase hexadecimal SHA-256. `FileStartupExecutionLedger` stores newline-delimited JSON in `<stateDirectory>/startup-executions.jsonl`, guards access with a `SemaphoreSlim`, loads existing records lazily, and uses append + `FlushAsync` for each first terminal outcome. Duplicate keys are ignored.

- [ ] **Step 4: Add only the required project/package references**

In `Directory.Packages.props`:

```xml
<PackageVersion Include="Microsoft.CodeAnalysis.CSharp.Scripting" Version="5.9.0" />
```

In `DigitalBrain.Scripting.csproj`, add project references to:

```xml
<ProjectReference Include="../DigitalBrain.Contracts/DigitalBrain.Contracts.csproj" />
<ProjectReference Include="../../Aspire/DigitalBrain.Aspire/DigitalBrain.Aspire.csproj" />
```

Also add the Roslyn package reference, `InternalsVisibleTo`, and copy `scripts/start.cs` to output (the script itself is added in Task 5).

- [ ] **Step 5: Run tests and commit**

Run:

```powershell
dotnet test tests/DigitalBrain.Scripting.Tests/DigitalBrain.Scripting.Tests.csproj
```

Expected: both ledger tests pass.

```powershell
git add Directory.Packages.props DigitalBrain.slnx src/Kernel/DigitalBrain.Scripting tests/DigitalBrain.Scripting.Tests
git commit -m "feat(scripting): add durable startup execution ledger"
```

---

## Task 3: Compile and run the startup script behind a tiny typed context

**Files:**

- Create: `src/Kernel/DigitalBrain.Scripting/Startup/StartupScriptContext.cs`
- Create: `src/Kernel/DigitalBrain.Scripting/Startup/IStartupScriptRunner.cs`
- Create: `src/Kernel/DigitalBrain.Scripting/Startup/CSharpStartupScriptRunner.cs`
- Create: `tests/DigitalBrain.Scripting.Tests/CSharpStartupScriptRunnerTests.cs`
- Create: `tests/DigitalBrain.Scripting.Tests/FakeDigitalBrain.cs`

**Interfaces consumed:** Public `IDigitalBrain`, Roslyn scripting APIs, `StartupScript`.

**Interfaces produced:** A runner that returns a structured terminal result instead of throwing compilation/runtime failures into the host.

- [ ] **Step 1: Write failing runner tests**

Cover all three outcomes:

```csharp
[Fact]
public async Task Script_can_read_the_connected_brain_owner()
{
    var script = StartupScript.FromSource("start.cs", "return Brain.Owner.Value;");
    var result = await runner.RunAsync(script, new FakeDigitalBrain("alice"), CancellationToken.None);
    Assert.True(result.IsSuccess);
    Assert.Equal("alice", result.Summary);
}

[Fact]
public async Task Compilation_errors_are_returned_as_diagnostics()
{
    var script = StartupScript.FromSource("start.cs", "this is not C#;");
    var result = await runner.RunAsync(script, new FakeDigitalBrain("alice"), CancellationToken.None);
    Assert.False(result.IsSuccess);
    Assert.NotEmpty(result.Diagnostics);
}

[Fact]
public async Task Runtime_errors_are_returned_without_terminating_the_worker()
{
    var script = StartupScript.FromSource("start.cs", "throw new InvalidOperationException(\"boom\");");
    var result = await runner.RunAsync(script, new FakeDigitalBrain("alice"), CancellationToken.None);
    Assert.False(result.IsSuccess);
    Assert.Contains("boom", result.Summary, StringComparison.Ordinal);
}
```

Add `StartupScript.FromSource(path, source)` so tests and file loading share the same hashing path.

- [ ] **Step 2: Run tests and verify they fail**

Run:

```powershell
dotnet test tests/DigitalBrain.Scripting.Tests/DigitalBrain.Scripting.Tests.csproj --filter "FullyQualifiedName~CSharpStartupScriptRunnerTests"
```

Expected: missing runner/context types.

- [ ] **Step 3: Implement the narrow script surface**

```csharp
internal sealed class StartupScriptContext(IDigitalBrain brain, CancellationToken cancellationToken)
{
    public IDigitalBrain Brain { get; } = brain;
    public CancellationToken CancellationToken { get; } = cancellationToken;
}

internal sealed record StartupScriptRunResult(
    bool IsSuccess,
    string Summary,
    IReadOnlyList<string> Diagnostics);

internal interface IStartupScriptRunner
{
    Task<StartupScriptRunResult> RunAsync(
        StartupScript script,
        IDigitalBrain brain,
        CancellationToken cancellationToken);
}
```

`CSharpStartupScriptRunner` uses `CSharpScript.RunAsync` with `StartupScriptContext` as globals. References are restricted to the runtime and public contract assemblies required by the example. Imports are limited to `System`, `System.Threading`, `System.Threading.Tasks`, `DigitalBrain.Abstractions`, and relevant public abstraction namespaces. Catch `CompilationErrorException` and ordinary exceptions into `StartupScriptRunResult`; rethrow `OperationCanceledException` when the supplied token is cancelled.

The script return value becomes `Summary` via `ToString()`, defaulting to `"completed"` for null.

- [ ] **Step 4: Run the focused tests and commit**

Run:

```powershell
dotnet test tests/DigitalBrain.Scripting.Tests/DigitalBrain.Scripting.Tests.csproj --filter "FullyQualifiedName~CSharpStartupScriptRunnerTests"
```

Expected: all three runner tests pass.

```powershell
git add src/Kernel/DigitalBrain.Scripting tests/DigitalBrain.Scripting.Tests
git commit -m "feat(scripting): run CSharp startup scripts"
```

---

## Task 4: Observe durable activation without timing races

**Files:**

- Create: `src/Kernel/DigitalBrain.Scripting/Startup/IStartupActivationSource.cs`
- Create: `src/Kernel/DigitalBrain.Scripting/Startup/DigitalBrainActivationSource.cs`
- Create: `tests/DigitalBrain.Scripting.Tests/DigitalBrainActivationSourceTests.cs`
- Modify: `tests/DigitalBrain.Scripting.Tests/FakeDigitalBrain.cs`

**Interfaces consumed:** `IDigitalBrain.ReadJournalAsync`, `IDigitalBrain.WatchJournalAsync`, `JournalKind.Outgoing`, `DigitalBrainActivated`, `SignalDelivery.SignalId`.

**Interfaces produced:** An async activation stream that first catches up from persisted history and then watches from the returned resume sequence.

- [ ] **Step 1: Write failing history/watch tests**

Use `SignalDelivery.Create` with a root caller `NeuronId` and `DigitalBrainActivated` to build test deliveries. Add tests proving:

```csharp
[Fact]
public async Task Existing_activation_is_emitted_before_live_watch()
{
    // Fake ReadJournalAsync returns one activation and ResumeSequence 7.
    // Enumerate the first source item.
    // Assert its owner/signal id and that WatchJournalAsync was not needed first.
}

[Fact]
public async Task Watch_starts_at_the_history_resume_sequence()
{
    // Fake initial read is empty with ResumeSequence 7.
    // Fake watch yields an activation.
    // Assert watch was called with 7 and activation was emitted.
}
```

- [ ] **Step 2: Run the tests and verify they fail**

Run:

```powershell
dotnet test tests/DigitalBrain.Scripting.Tests/DigitalBrain.Scripting.Tests.csproj --filter "FullyQualifiedName~DigitalBrainActivationSourceTests"
```

Expected: missing activation source types.

- [ ] **Step 3: Implement read-then-watch**

```csharp
internal interface IStartupActivationSource
{
    IAsyncEnumerable<StartupActivation> WatchAsync(CancellationToken cancellationToken);
}
```

`DigitalBrainActivationSource` must:

1. Call `ReadJournalAsync(JournalKind.Outgoing, 0, token)`.
2. Yield only deliveries whose signal is `DigitalBrainActivated` for `brain.Owner`.
3. Start `WatchJournalAsync(JournalKind.Outgoing, initial.ResumeSequence, token)`.
4. Apply the same filter to every watched page.
5. Preserve the delivery's `SignalId.Value.ToString("D")` as the activation identity.

This is intentionally a small source adapter; deduplication belongs to the ledger key.

- [ ] **Step 4: Run tests and commit**

Run:

```powershell
dotnet test tests/DigitalBrain.Scripting.Tests/DigitalBrain.Scripting.Tests.csproj --filter "FullyQualifiedName~DigitalBrainActivationSourceTests"
```

Expected: both activation ordering tests pass.

```powershell
git add src/Kernel/DigitalBrain.Scripting tests/DigitalBrain.Scripting.Tests
git commit -m "feat(scripting): observe durable brain activation"
```

---

## Task 5: Orchestrate exactly-once-per-version startup execution

**Files:**

- Create: `src/Kernel/DigitalBrain.Scripting/Startup/StartupScriptOptions.cs`
- Create: `src/Kernel/DigitalBrain.Scripting/Startup/StartupScriptWorker.cs`
- Create: `tests/DigitalBrain.Scripting.Tests/StartupScriptWorkerTests.cs`

**Interfaces consumed:** Activation source, script loader, runner, ledger, `TimeProvider`, structured logging.

**Interfaces produced:** A hosted service that sequences activation -> load/hash -> deduplicate -> run -> persist -> log.

- [ ] **Step 1: Write failing orchestration tests with fakes**

Cover these invariants:

```csharp
[Fact]
public async Task Duplicate_activation_executes_the_same_script_version_once()
{
    // Source yields the same activation twice; runner counts calls.
    // Assert one runner call and one terminal ledger record.
}

[Fact]
public async Task Failed_execution_is_recorded_and_not_retried_automatically()
{
    // Runner returns failure; source repeats the activation.
    // Assert one runner call and persisted failure diagnostics.
}

[Fact]
public async Task A_changed_script_hash_can_run_for_the_same_activation()
{
    // Preload ledger with hash A, load hash B, emit same activation.
    // Assert hash B runs once.
}

[Fact]
public async Task Cancellation_stops_execution_without_recording_a_false_completion()
{
    // Runner blocks until cancellation and throws OperationCanceledException.
    // Assert no terminal record was written.
}
```

- [ ] **Step 2: Run the tests and verify they fail**

Run:

```powershell
dotnet test tests/DigitalBrain.Scripting.Tests/DigitalBrain.Scripting.Tests.csproj --filter "FullyQualifiedName~StartupScriptWorkerTests"
```

Expected: missing worker/options types.

- [ ] **Step 3: Implement the hosted worker**

Configuration shape:

```csharp
internal sealed class StartupScriptOptions
{
    public const string SectionName = "DigitalBrain:Scripting";
    public string ScriptPath { get; set; } = Path.Combine(AppContext.BaseDirectory, "scripts", "start.cs");
    public string StateDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DigitalBrain",
        "Scripting");
}
```

For each activation, the worker loads the current source, creates:

```csharp
new StartupExecutionKey(activation.Owner, activation.SignalId, script.Sha256)
```

It skips a key already in the ledger. Otherwise it invokes the runner, converts its result to `StartupExecution` using `TimeProvider.GetUtcNow()`, persists the record, then logs one structured success or failure event containing owner, activation id, script hash, and summary. File-load errors are also converted into a failed terminal execution when the script identity can be established; a missing/unreadable path is logged as a worker failure and must not stop the kernel.

The worker never acknowledges or republishes activation because journal observation requires no acknowledgement.

- [ ] **Step 4: Run worker and complete scripting tests**

Run:

```powershell
dotnet test tests/DigitalBrain.Scripting.Tests/DigitalBrain.Scripting.Tests.csproj
```

Expected: all ledger, runner, source, and worker tests pass.

- [ ] **Step 5: Commit**

```powershell
git add src/Kernel/DigitalBrain.Scripting tests/DigitalBrain.Scripting.Tests
git commit -m "feat(scripting): orchestrate versioned startup behavior"
```

---

## Task 6: Compose the worker, add the first script, and wire Aspire

**Files:**

- Replace: `src/Kernel/DigitalBrain.Scripting/Program.cs`
- Create: `src/Kernel/DigitalBrain.Scripting/scripts/start.cs`
- Modify: `src/Kernel/DigitalBrain.Scripting/DigitalBrain.Scripting.csproj`
- Modify: `src/Aspire/DigitalBrain.AppHost/AppHost.cs`
- Modify: `src/Aspire/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj`
- Modify: `src/Aspire/DigitalBrain.AppHost/ProductSurfaceResources.cs`
- Create: `tests/DigitalBrain.Scripting.Tests/ArchitectureTests.cs`
- Modify: `tests/DigitalBrain.Aspire.Tests/ProductSurfaceResourceNames.cs`

**Interfaces consumed:** `Host.CreateApplicationBuilder`, `AddDigitalBrainClient(activateOnStart: false)`, Aspire project resources, all startup worker services.

**Interfaces produced:** Runnable `scripting` Aspire resource and a meaningful `start.cs` that proves access to the connected brain without claiming kernel lifecycle ownership.

- [ ] **Step 1: Write failing composition and boundary tests**

Add:

```csharp
[Fact]
public void Kernel_does_not_reference_scripting()
{
    var references = typeof(DigitalBrain.Core.Neuron).Assembly.GetReferencedAssemblies();
    Assert.DoesNotContain(references, reference => reference.Name == "DigitalBrain.Scripting");
}

[Fact]
public void Start_script_does_not_activate_or_forge_kernel_activation()
{
    var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "scripts", "start.cs"));
    Assert.DoesNotContain("ActivateAsync", source, StringComparison.Ordinal);
    Assert.DoesNotContain("DigitalBrainActivated", source, StringComparison.Ordinal);
}
```

Update the Aspire surface-name fixture to include `Scripting = "scripting"` so resource naming stays explicit.

- [ ] **Step 2: Run focused tests and verify they fail**

Run:

```powershell
dotnet test tests/DigitalBrain.Scripting.Tests/DigitalBrain.Scripting.Tests.csproj --filter "FullyQualifiedName~ArchitectureTests"
dotnet test tests/DigitalBrain.Aspire.Tests/DigitalBrain.Aspire.Tests.csproj --no-restore
```

Expected: missing copied script and missing `scripting` surface name.

- [ ] **Step 3: Replace the placeholder program with real host composition**

Use this composition shape:

```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.AddDigitalBrainClient(activateOnStart: false);
builder.Services.Configure<StartupScriptOptions>(
    builder.Configuration.GetSection(StartupScriptOptions.SectionName));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IStartupActivationSource, DigitalBrainActivationSource>();
builder.Services.AddSingleton<IStartupScriptRunner, CSharpStartupScriptRunner>();
builder.Services.AddSingleton<IStartupExecutionLedger>(services =>
{
    var options = services.GetRequiredService<IOptions<StartupScriptOptions>>().Value;
    return new FileStartupExecutionLedger(options.StateDirectory);
});
builder.Services.AddHostedService<StartupScriptWorker>();
await builder.Build().RunAsync();
```

Keep `Program.cs` as composition only; all behavior stays in focused startup classes.

- [ ] **Step 4: Add the first script**

The first script should produce an observable, truthful result with only the approved client:

```csharp
return $"DigitalBrain owner '{Brain.Owner.Value}' startup behavior completed.";
```

Copy it into output via:

```xml
<Content Include="scripts/start.cs" CopyToOutputDirectory="PreserveNewest" />
```

This proves script generation/execution without inventing `PublishAsync` before its contract exists.

- [ ] **Step 5: Add the worker as an Aspire sibling**

Add a normal Aspire project reference to `DigitalBrain.Scripting.csproj`, then configure:

```csharp
var scripting = builder.AddProject<Projects.DigitalBrain_Scripting>(ProductSurfaceResources.Scripting)
    .WithReference(brain.AsClient())
    .WithEnvironment(
        ShellHostingExtensions.OwnerEnvironmentVariable,
        ShellHostingExtensions.DefaultOwner)
    .WithEnvironment(context =>
    {
        if (developmentClusterId is not null)
        {
            context.EnvironmentVariables["Orleans__ClusterId"] = developmentClusterId;
        }
    })
    .WaitFor(kernel);
```

Add `public const string Scripting = "scripting";` and remove the obsolete “Later” comment. The worker references the brain client resource and waits for kernel health; the kernel has no reverse dependency.

- [ ] **Step 6: Run focused tests, solution tests, and dependency inspection**

Run:

```powershell
dotnet test tests/DigitalBrain.Scripting.Tests/DigitalBrain.Scripting.Tests.csproj
dotnet test tests/DigitalBrain.Aspire.Tests/DigitalBrain.Aspire.Tests.csproj --no-restore
dotnet test tests/DigitalBrain.Substrate.Tests/DigitalBrain.Substrate.Tests.csproj --no-restore
dotnet test tests/DigitalBrain.Catalog.Tests/DigitalBrain.Catalog.Tests.csproj --no-restore
dotnet test tests/DigitalBrain.Simulation.Tests/DigitalBrain.Simulation.Tests.csproj --no-restore
dotnet build DigitalBrain.slnx --no-restore
dotnet list src/Kernel/DigitalBrain/DigitalBrain.csproj reference
dotnet list src/Kernel/DigitalBrain.Silo/DigitalBrain.Silo.csproj reference
```

Expected: all tests pass, build has zero errors/warnings, and neither dependency listing contains `DigitalBrain.Scripting`.

- [ ] **Step 7: Run the Aspire smoke test**

Use the Aspire skill to start the AppHost. Verify in resource state/logs:

1. `kernel` becomes healthy.
2. `scripting` starts after `kernel` health.
3. The worker logs one successful startup execution with owner, activation id, script hash, and the summary returned by `start.cs`.
4. Restart only `scripting`; verify the same key is skipped and no second success execution is recorded.
5. Stop the AppHost cleanly.

- [ ] **Step 8: Commit**

```powershell
git add src/Kernel/DigitalBrain.Scripting src/Aspire/DigitalBrain.AppHost tests/DigitalBrain.Scripting.Tests tests/DigitalBrain.Aspire.Tests
git commit -m "feat(scripting): run start behavior after activation"
```

---

## Final Verification and Scope Audit

- [ ] Run `dotnet build DigitalBrain.slnx --no-restore` and require zero warnings/errors.
- [ ] Run the Scripting, Aspire, Substrate, Catalog, and Simulation test projects and require all pass.
- [ ] Run `rg -n "TODO|FIXME|NotImplementedException|protocol not implemented" src/Kernel/DigitalBrain.Scripting tests/DigitalBrain.Scripting.Tests` and require no matches.
- [ ] Run `rg -n "DigitalBrain\.Scripting" src/Kernel/DigitalBrain src/Kernel/DigitalBrain.Silo` and require no matches.
- [ ] Run `rg -n "ActivateAsync|DigitalBrainActivated" src/Kernel/DigitalBrain.Scripting/scripts/start.cs` and require no matches.
- [ ] Confirm the worker can catch up when activation predates worker startup, and that a worker-only restart does not rerun the same script hash.
- [ ] Confirm the diff contains no `Publish`, `Subscribe`, `Run`, registry, workflow engine, generated grain, arbitrary NuGet loading, or sandbox claims.
- [ ] Review `git diff --stat` and `git diff` for accidental edits before declaring completion.

## Deferred Next Slice

After this proof is stable, design the generic durable `BehaviorNeuron` and add the first real capability: `Publish`. Only then change `start.cs` to publish `ApplicationStarted`. Runtime subscriptions and behavior-to-behavior `Run` remain later, separate changes.
