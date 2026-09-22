# Stage 2: programmable C# behaviors

Status: proposed design for implementation; no stage-2 runtime changes have been made.

## Outcome

An assistant can turn an instruction into a single C# source file, inspect the available neuron contracts, compile and exercise the file against test neurons, repair failures, and activate the exact tested artifact. The user can inspect its source, tests, active version, readiness, errors, and logs; stop it; update it; and roll back. A behavior can compose AI, Time, Flutter, and other installed modules through `IDigitalBrain`.

The reference is the programming style in `E:/intochat/digitalbrainnew/src/Behaviors/invoice-followup.cs`: ordinary C# subscribing to typed signals and calling typed neurons. Its simulation bootstrap is not the production runtime. Its other JSON graph implementation is not the architecture to port.

## Evidence from this repository

- Coding already has `ICodeWorkspace`, Roslyn-backed navigation, `IChangeSet`, diagnostic checking, process execution, and build/test helpers. These operate on solution edits; they do not establish an immutable tested deployment artifact.
- `DigitalBrain.Core.IBehavior` is a local object with `Task RunAsync(CancellationToken)`. `BehaviorHosting` runs registered behaviors, retries failures, and uses `BehaviorScopedBrain` and `BehaviorReadiness` to track subscriptions. Keep this local execution interface; do not turn it into an Orleans neuron.
- `src/Behaviors/timer-report.cs` is already a file-based app using an Orleans client. Its sibling `TimerReport.cs` explicitly awaits `SubscribeAsync<T>()` before consuming signals. Extract the repeated bootstrap into a reusable SDK entry point.
- IntoChat currently registers `ElonBitcoin` statically; `BehaviorEndpoints.MapBehaviors` is empty. There is no current dynamic graph service to migrate in this repository.
- Signals are live observations. Overflow or source reactivation terminates the subscription; events during disconnection can be missed. Readiness is meaningful, but durable delivery and exactly-once effects are not provided.
- Stage 1 supplies configurable `IAgent`, explicit tool selection, native/MCP tool adapters, bounded model/tool loops, cancellation, structured history, and model capability descriptors. Use those contracts rather than introducing another provider abstraction.

## Architecture decision

Use a contracts-first Coding pipeline and a separate Behavior lifecycle module, backed by a supervised local child process. Keep generated logic outside the silo.

| Boundary | Owns | Does not own |
|---|---|---|
| AI `IAgent` | Conversation, model selection, instructions, tool calls, usage | Compiler, deployment policy, worker lifetime |
| Coding `ICodeDraft` | Source/test revisions, diagnostics, check operations, immutable artifacts | Starting long-running behaviors |
| Behavior `IBehaviorProgram` | Desired deployment, CAS revisions, start/stop/rollback, execution status | Source editing or LLM prompting |
| Behavior supervisor | Process ownership, lifecycle reconciliation, deadlines, control channel | Model inference or business logic |
| Behavior SDK/runtime | Brain connection, scoped subscriptions, readiness, cancellation | Artifact approval or source generation |
| IntoChat integration | Workspace scope, tools, HTTP access, authoring orchestration | A second implementation of the pipeline |

Dependency direction: Behavior contracts may reference Coding artifact reference DTOs; Coding does not reference Behavior lifecycle implementation. The Behavior SDK remains usable by hand-written programs. AI has no dependency on Coding or Behavior. Agent tool adapters live in the application composition layer.

### Alternatives considered

1. **Load generated assemblies inside the silo:** simplest dispatch, but a crash, unbounded allocation, blocking operation, static state, or dependency conflict affects the host. Rejected.
2. **Supervised local child process:** selected for stage 2. Fits the current file-app example, permits hard termination and independent diagnostics, and works on the current Windows developer host.
3. **Container or remote execution service:** stronger isolation and multi-host scheduling, but introduces packaging, network authorization, and deployment infrastructure. A future executor can replace the local supervisor after the contracts stabilize.

A child process is a fault/lifecycle boundary, not a security sandbox. Stage 2 is trusted local execution, using the host's existing trust model. Neither a module manifest nor an `IDigitalBrain` wrapper is an authorization boundary. Arbitrary hostile code and shared multi-tenant execution require a restricted transport and OS/container isolation before being supported.

## Public concepts and contracts

Use `INeuron`, explicit Orleans aliases/serializer IDs, immutable DTOs, and typed `Signal` records, following Flutter's command/read/signal pattern. Keep SDK types, Roslyn objects, `Process`, `Type`, delegates, and absolute executable paths out of public contracts.

### Coding

`ICodeDraft : INeuron`, keyed by the trusted workspace scope plus draft ID:

```csharp
Task<CodeDraftSnapshot> Read(CancellationToken cancellationToken = default);
Task<CodeDraftSnapshot> Save(SaveCodeDraft request, CancellationToken cancellationToken = default);
Task<CodeCheckSnapshot> Check(CheckCodeDraft request, CancellationToken cancellationToken = default);
Task<CodeCheckSnapshot> ReadCheck(Guid operationId, CancellationToken cancellationToken = default);
Task CancelCheck(Guid operationId, CancellationToken cancellationToken = default);
```

`SaveCodeDraft` contains `ExpectedRevision`, `OperationId`, `Source`, `Tests`, `ModuleIds`. `Tests` is a separate C# test source string: the deployed behavior is one source file, while its evidence remains separate. Save creates a revision; it cannot activate anything. `CheckCodeDraft` pins `Revision` and `OperationId`. Check returns a durable operation snapshot promptly; background reconciliation performs compilation/testing. Repeated operation IDs with the same input return the original result; reuse with different input is a conflict.

`CodeDraftSnapshot` contains revision, exact source/tests, module IDs, and latest check ID. `CodeCheckSnapshot` contains operation ID, draft revision, status, structured diagnostics, test counts, timestamps, and optional `CodeArtifactRef`. Status is `Queued`, `Building`, `Testing`, `Passed`, `Failed`, `Cancelled`, or `Interrupted`. A host crash interrupts a check; it does not silently rerun generated tests. A new explicit operation may retry.

`CodeArtifactRef(string Id, string SourceHash, string EnvironmentHash)` is an opaque verified reference, not a path. Its stored manifest contains hashes of source, tests, fixed project template, SDK, all dependency/contract assemblies, platform, compiler options, final executable payload, test report, and validator version. A passing record requires a real successful build, at least one discovered and passed test, zero failures/skips, and required host-authored conformance tests. AI-authored tests alone are not a correctness guarantee.

Signals: `CodeDraftSaved(DraftId, Revision)`, `CodeCheckChanged(DraftId, OperationId, Revision, Status)`. Signals notify; `Read`/`ReadCheck` are authoritative. Publication failure after persistence does not turn a committed result into failure.

Keep `ICodeWorkspace` and `IChangeSet` for repository work. Add the file-app path without routing every generated behavior through edits to the live solution. Reuse Roslyn navigation to provide contract documentation/examples to the agent; package that knowledge with the exact contract fingerprint used by the build.

### Behavior lifecycle

`IBehaviorProgram : INeuron`, keyed by trusted workspace scope plus program ID, is distinct from local `DigitalBrain.Core.IBehavior`:

```csharp
Task<BehaviorSnapshot> Read(CancellationToken cancellationToken = default);
Task<BehaviorSnapshot> Deploy(DeployBehavior request, CancellationToken cancellationToken = default);
Task<BehaviorSnapshot> Stop(ChangeBehaviorState request, CancellationToken cancellationToken = default);
Task<BehaviorSnapshot> Start(ChangeBehaviorState request, CancellationToken cancellationToken = default);
Task<BehaviorSnapshot> Rollback(RollbackBehavior request, CancellationToken cancellationToken = default);
Task<BehaviorLogPage> ReadLogs(long afterSequence, int limit = 100, CancellationToken cancellationToken = default);
```

All mutations carry `ExpectedRevision` and `OperationId`. Deploy additionally carries a passing artifact reference and nonsecret configuration JSON; rollback carries a retained deployment revision. Secrets are supplied by host configuration and never embedded in source, artifacts, tool replies, or arguments. A new deployment revision records artifact, config, creation time, and originating agent run ID when available. Rollback creates a new revision pointing at a retained verified artifact/config; it never rewrites history.

`BehaviorSnapshot` separates desired state (`Stopped`/`Running`) from observed state (`Stopped`, `Starting`, `Running`, `Stopping`, `Completed`, `Failed`, `Interrupted`). Include state revision, desired deployment revision, active deployment revision, generation ID, readiness, last error, and retained deployment metadata. Deploy records a request to run and returns `Starting`; success does not imply readiness. Start resumes the current deployment; Stop persists desired `Stopped` before asking the worker to terminate. Repeated calls are idempotent.

Signals: `BehaviorDeploymentChanged(ProgramId, Revision, ArtifactId)`, `BehaviorExecutionChanged(ProgramId, GenerationId, State, Ready, Error)`, `BehaviorLogAvailable(ProgramId, GenerationId, LastSequence)`. Logs are paginated and bounded; signals do not carry unbounded stdout. Include sequence/truncation information so clients can detect missing log history.

### SDK authoring surface

The generated file uses a proposed `BehaviorApp.RunAsync<TBehavior>(args, requirements)` entry point. `TBehavior : IBehavior` receives `IDigitalBrain` through constructor injection. The file can contain its behavior and private helper types; it does not define neurons, load arbitrary projects, or alter SDK bootstrap.

```csharp
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Time.Timers.Signals;

await BehaviorApp.RunAsync<TimerLogger>(args, brain =>
    [SubscriptionRequirement.For<TimerTick>(brain.Get<DigitalBrain.Time.Timers.ITimer>("tea"))]);

public sealed class TimerLogger(IDigitalBrain brain) : IBehavior
{
    public async Task RunAsync(CancellationToken cancellation = default)
    {
        var timer = brain.Get<DigitalBrain.Time.Timers.ITimer>("tea");
        await using var ticks = await brain.SubscribeAsync<TimerTick>(timer, cancellation);
        await foreach (var tick in ticks.ReadAllAsync(cancellation))
        {
            Console.WriteLine($"TimerTick {tick.TimerId} {tick.ObservedAt:O}");
        }
    }
}
```

This is proposed API usage, not currently compilable source. The host-owned project template supplies approved references. Production does not start a simulation. Tests instantiate the same behavior class with the test brain, without invoking the top-level entry point. Build the executable once; test projects reference that output and separate test source. Do not transform the behavior into different test and deployment implementations.

## Compilation and validation

- Authoring unit: one UTF-8 C# source file plus separate tests and an explicit installed-module list. Compile it in a generated, host-owned project; the implementation artifact may contain multiple DLLs and metadata. Single-file source does not mean single-file deployment binary.
- Disallow user-controlled `#:project`, `#:package`, `#:include`, `#:property`, MSBuild files, analyzers/generators, shell commands, and restore sources in managed drafts. Resolve module IDs against a host allowlist. Dependencies and SDK are pinned and resolved by trusted build configuration. Existing hand-authored file apps remain supported outside this managed pipeline.
- Proposed defaults: source 128 KiB, test source 128 KiB, configuration 32 KiB, 32 modules, 100 diagnostics, 64 KiB diagnostic/log entry, two concurrent checks per host, build timeout 120 seconds, test timeout 120 seconds, and a 10 MiB retained log ring per program. Host configuration can lower these limits.
- Use clean private work directories with explicit package/build configuration; never inherit an arbitrary parent `Directory.Build.*` or `NuGet.Config`. Argument lists, explicit working directory, cleared/allowlisted environment, no shell interpolation. Tests receive test-only connections, not production credentials.
- Hash and seal the executable output after verification; no source compilation on Start/Deploy. Store atomically under a private content-addressed root. Validate hashes and host compatibility on every activation, including rollback. Source, test, dependency, SDK, validator, or artifact changes invalidate earlier evidence.
- Use machine-readable test results; zero tests or an unreadable result is failure even when the process exits zero. Fix/reuse `DotnetRunner` where it overlaps, preserving existing callers and the repository's Microsoft.Testing.Platform invocation.

## Runtime and update semantics

One configured supervisor owns a local state/artifact volume. Orleans neurons persist intent; they do not own an OS process in activation fields. Use a durable program index and a reconciliation loop so deactivation and host restart do not lose deployments. Multi-host placement is explicitly out of scope; reject configuration that attempts shared active ownership.

The supervisor owns a per-program generation and a parent/child control channel independent of stdout. The child checks channel liveness and terminates on parent loss; the supervisor uses OS process-tree containment for uncooperative workers and build/test children. On Windows use kill-on-close job containment and validate that it actually attaches. Launch ownership must be established before user code can execute. Fail closed on unsupported containment; do not advertise unsupported platforms.

An update validates the new artifact before touching the current process, persists desired deployment, withdraws readiness, stops and confirms exit of the old process, then starts the new generation. If exit cannot be confirmed, fail the update without starting another generation. If the new revision fails readiness, mark it failed and retain the previous revision for explicit rollback. No automatic rollback loop and no simultaneous subscribers. This produces an observable subscription gap.

Readiness requires brain connection plus all declared required subscriptions. A late ready/exit/heartbeat from an old generation is ignored. Closing a required subscription withdraws readiness. Fix existing `BehaviorHost` so normal completion, exception, cancellation, and subscription failure each terminate the generation consistently. Avoid two retry owners: supervised apps disable the runtime's internal retry; static behaviors retain compatibility with their existing hosting policy.

Defaults: 30-second startup/readiness deadline, 15-second graceful stop, then force termination; 5-second control heartbeat and 20-second loss deadline. A normal return is `Completed` and is not automatically restarted. Unexpected exit uses at most three retries with 1/2/4-second backoff, then `Failed`. Persist retry counts and generation transitions. A stopped behavior remains stopped after host restart. A previously running service becomes `Interrupted`, then is reconciled to a fresh generation if desired state remains Running. Work interrupted halfway through a business operation is not automatically replayed.

Long-running logic reacts to existing Time timer/reminder neurons; do not create a second scheduler on `IAgent`. State that must survive process replacement belongs in existing domain neurons. In-memory locals are transient. Durable event history, transactional external side effects, automatic state schema migration, and exactly-once execution are separate future capabilities.

## Agent integration

Use existing `IAgent` and configured model profile. Do not introduce `ICodingLLM`, per-provider coding agents, or an agent inheritance hierarchy. A host-configured authoring agent gets contract lookup, draft read/save/check/status/cancel and behavior read/deploy/start/stop/rollback/log tools. Tool adapters enforce workspace scope from trusted invocation context; the model cannot supply another scope or OS path.

A small application-level `BehaviorAuthoringService` coordinates a bounded request: read current state and contract catalog, ask the agent for source and tests, save, check, send structured diagnostics back for up to two repairs, and return a passing artifact or a useful failure. Maximum three candidates per request and a ten-minute overall budget; propagate cancellation through agent/check execution. Authoring creates a draft by default. An explicit create-and-run request may activate using the host's execution policy; do not invent a mandatory per-tool confirmation workflow. Provider selection and model settings remain `AgentDefinition` concerns.

Native and MCP tools call the same application services. Do not create nested model loops or let AI-authored text invoke arbitrary compiler/OS commands. Tool selection is configuration, while authorization remains the application's responsibility. Persist draft/check/deployment/agent-run correlation IDs, but never chain-of-thought.

## Acceptance and scope

The first complete vertical slice uses a deterministic scripted model, a real compiler/test runner, a separate silo and worker, and a Timer-to-Flutter effect. It proves readiness, one observable effect, an edited replacement, failure isolation, stop, host restart, and rollback. It needs no paid model or live email account. Port the invoice-style example only against real installed contracts; use fakes to assert outbound effects, and do not send email in automated tests.

Stage 2 includes lifecycle HTTP/tool integration and inspectable status/source/logs. A visual code editor, graph designer, container fleet, package marketplace, new neuron generation, live assembly hot reload, general-purpose agent scheduler, and durable workflow engine are excluded. Contracts leave room for these without promising them now.

Implementation sequence and executable test gates: [stage-2 implementation plan](../plans/2026-09-22-programmable-behaviors.md).