# DigitalBrain: small framework, reusable testing, C# behaviors

Date: 2026-09-19
Status: approved by the user on 2026-09-19; detailed implementation plan pending review and execution selection.
Target: `E:/intochat/digitalbrain/DigitalBrain.slnx`.
Reference prototype: `E:/intochat/digitalbrainnew/DigitalBrain.slnx`.

## 1. Intent and decisions

Build a small, understandable production framework and reusable testing package first. Then migrate real modules, deleting the old programming machinery as its consumers disappear. C# is the behavior language; neurons expose typed operations and publish typed facts.

Confirmed by the user during brainstorming:

- Persist module state. Live signals and C# behaviors can restart without replay.
- Make a clean break in contracts and start with fresh stored state.
- Migrate modules incrementally.

Consequences: no generic durable command execution, durable signal delivery, workflow checkpointing, replay interpreter, or compatibility facade. Module-specific business rules and provider correctness still matter. A timeout after an external operation can leave its outcome unknown; the framework must not automatically repeat that operation.

Success means fewer concepts for both module authors and behavior authors, a common production/test execution path, meaningful tests of real modules, and actual deletion of obsolete code. Line counts are evidence of size, not the definition of correctness.

## 2. What the repositories currently implement

CodeGraph was used on both repositories, supplemented by project/configuration reads and focused test execution. The older `docs/new_programming.md` explicitly describes its codebase claims as unverified; it is background, not an approved specification.

The existing product combines an Orleans actor runtime, persistent neurons, typed module operations, string/JSON signals, durable reaction queues, command outcome tracking, a behavior graph interpreter, AI and integration modules, MCP access, and the IntoChat/Flutter application. The runtime converts many ordinary method calls into command admission, scheduled signals, later reactions, snapshot writes, and announcements.

The new project demonstrates a much simpler model: `Get<T>(id)`, ordinary typed calls, and `await foreach` over typed signals. Examples include invoice notification/audit and a Monday ZIP-to-CSV-to-database behavior. Its implementation is a prototype rather than a migrated application:

- Behavior entry points start `DigitalBrainSimulation` and reference `DigitalBrain.Testing`.
- Gmail, Twitter, and ETL examples rely on test-owned grains and contracts.
- `HostedDigitalBrain`, the only inspected implementation of the behavior-facing client, lives inside the testing package.
- Most modules and IntoChat are absent from the new solution. Old behavior files remain on disk; several old tests/storage helpers are excluded with `Compile Remove`.
- The new neuron stores listeners and inbox entries in activation memory. The global signal collector is a bounded in-memory view, not a journal.
- Signal delivery currently has three paths: broadcast channels, client observers, and direct bound listeners. Behaviors consume observers; the global collector consumes broadcast channels.

Counts below cover non-generated C# files on disk, excluding `bin`/`obj`; excluded source is still counted:

| Area | Existing files / lines | Prototype files / lines |
| --- | ---: | ---: |
| DigitalBrain runtime | 26 / 2,258 | 3 / 154 |
| DigitalBrain.Contracts | 41 / 755 | 5 / 61 |
| BehaviorRuntime | 31 / 1,922 | 31 / 1,930 |
| DigitalBrain.Testing | 7 / 526 | 11 / 874 |

Relevant evidence:

- Existing `DigitalBrain/Neuron/Neuron.cs`: `DurableGrain`, retry reminders, staged work, persistence fences, command/reaction restrictions.
- Existing `DigitalBrain/Neuron/NeuronOfState.cs`: snapshot envelope combines state, applied-delivery marker, and announcements.
- Existing `DigitalBrain/Commands/CommandExecution.cs`: command IDs, result replay, argument hashing, outcome recording.
- Existing `Testing/BrainSimulation.cs`: useful real in-process cluster, DI customization, restart and persistent-storage support.
- Prototype `Testing/DigitalBrainSimulation.cs`: production-shaped client hidden in test infrastructure, unconditional web host, unbounded observer channel.
- Prototype `Tests/Google/GmailFacts.cs:41` and `Tests/Etl/MondayInvoiceFacts.cs:22`: polling repeatedly sends the stimulus, hiding possible readiness and duplicate-processing defects.
- Existing project references: AI -> MCP executable -> BehaviorRuntime; ClickHouse/Supabase implementations -> Flutter implementation; several contracts -> UI contracts.

## 3. Alternatives considered

| Approach | Advantage | Cost | Decision |
| --- | --- | --- | --- |
| Extract a small production foundation from the prototype's model; prove it with real modules | Clear semantics, shared test path, deletion follows explicit migration gates | Requires finishing subscriptions, persistence tests, and production hosting | Recommended |
| Copy the prototype wholesale | Fast initial size reduction | Imports test-owned production behavior, excludes unmigrated functionality, leaves hidden legacy source | Reject |
| Gradually trim the existing durable runtime | Smaller changes to existing consumers | Old command/reaction semantics keep determining the design despite the clean-break decision | Reject |

The first design scope is the framework, shared testing, and one real pilot module. Later module families receive their own focused design and implementation work; this document supplies their order and completion rules, not invented rewrites of uninspected internals.

## 4. Target framework

### Package responsibilities

| Package | Owns | Must not own |
| --- | --- | --- |
| `DigitalBrain.Contracts` | Neuron identity/interface, typed signal base, shared subscription wire contracts | ASP.NET hosting, module registration, journals, command ledger, UI, providers |
| `DigitalBrain` | Small neuron base, production client, live subscription lifecycle, explicit runtime registration | TestingHost, domain fakes, workflow interpreter, generic command scheduling |
| Module contracts | Typed grain methods, results, state views, emitted signal records | Generic command envelopes, UI rendering contracts |
| Module implementation | Business rules, persisted state, provider calls, registration | Another module's implementation or test harness |
| `DigitalBrain.Testing` | Real test cluster, fixtures, observations, waits, persistence test adapters, diagnostics | Gmail/Twitter/ETL product logic, duplicate runtime, mandatory xUnit base class |
| HTTP/MCP/AI/UI adapters | Translate their transport or presentation to typed module operations | Make core contracts depend on their schemas or executables |

Keep these existing package seams where useful; do not add a package for every helper. Start with the production client in `DigitalBrain`; split it only if a demonstrated consumer needs a lighter dependency closure.

### Programming surface

The behavior-facing client needs two capabilities: resolve a typed grain and subscribe to typed facts from a particular grain. HTTP clients, cluster administration, global history, and test lifecycle are not part of this interface.

Proposed subscription shape, deliberately emphasizing readiness:

```csharp
// Proposed interface usage; not yet compiled.
var gmail = brain.Get<IGmail>("work");
await using var sent = await brain.SubscribeAsync<EmailSent>(gmail, cancellation);
await foreach (var email in sent.ReadAllAsync(cancellation))
{
    if (email.Subject.Contains("Invoice", StringComparison.OrdinalIgnoreCase))
        await notifications.Notify(email.Subject);
}
```

`SubscribeAsync` completes only when registration has been acknowledged. The returned subscription owns cleanup and a bounded local buffer. This adds an explicit lifetime to the prototype's lazy `On<T>` loop and removes ambiguity about when a producer can safely emit. Avoid a second convenience API until the pilot demonstrates a need.

Use one client subscription mechanism initially: the prototype's Orleans observers, completed with renewal, cancellation, cleanup, and visible failure. Keep wire-level subscribe/unsubscribe operations available to the runtime, but business authors use the client abstraction. Use full grain identity wherever subscriptions are keyed; string keys alone can collide across grain types.

Remove generic `Bind`, `Receive`, `Inbox`, `ISynapse`, `Ping`, and `Sleep` from the business programming surface unless a real migrated use case requires one. C# composition replaces graph wiring. Module methods replace generic incoming signal dispatch. Test-only probe grains may expose lifecycle controls without adding them to every production neuron.

Publishing a fact is a protected runtime capability. A consumer cannot use the public base interface to fabricate another module's facts. Typed signal records and normal Orleans serialization replace signal-name strings and JSON body plumbing for internal calls.

### State and failure semantics

- Modules use Orleans `IPersistentState<TState>` directly, with module-owned state schemas and stable serializer identifiers. Start without a generic `Neuron<TState>` wrapper.
- A successful state-changing call completes after its required state write. On failed persistence, reload or deactivate before allowing further reads/mutations to treat tentative state as committed. Prove this rule in tests.
- Publish success facts after successful persistence where the fact describes a stored change. State commit and live publication are intentionally not atomic: a crash between them may lose the fact. Reads expose the committed state.
- Provider calls return typed results or throw meaningful errors. Keep provider-specific ambiguity/idempotency handling where the business operation needs it, without a global command journal.
- Do not replay behavior bodies or automatically retry external side effects. A normal host/process supervisor may restart the behavior from its beginning.
- Cancellation terminates enumeration and releases observer references. Subscription registration failure and cleanup failure must not leak resources or mask the original operation failure.
- Healthy subscriptions renew before the observer lease expires. A failed renewal or detected disconnect faults the subscription so the host can restart it. Recovery has a possible live-event gap and no replay promise.
- Buffers are bounded. If a local consumer falls behind, fault that subscription with a useful error rather than silently dropping locally queued facts or growing memory without limit. This does not upgrade the underlying transport to reliable delivery.
- Do not promise global ordering, exactly-once delivery, or that publication means every behavior completed. Diagnostic traces are not a durable event log.

### Hosting and file-based apps

Production and tests invoke the same explicit runtime and module registration. Prefer ordinary registration methods and explicit composition over `ModuleManifest`, string-loaded types, hosting attributes, projections, and a custom builder hierarchy. Keep useful Aspire resource wiring as straightforward host composition.

Keep HTTP endpoint mapping outside core contracts. HTTP tests opt into a host; ordinary grain tests should not start Kestrel. Production behavior files reference production packages/projects only.

Use genuine SDK file-based app directives for entry points. Put reusable behavior logic in a normal source file/class that the entry point includes or references and tests compile directly. Keep top-level startup separate from the testable method. Avoid maintaining a hand-written executable project per behavior merely to build it in the solution. CI must explicitly build the file-based entry points as well as project-based libraries/tests.

Do not assume Native AOT compatibility: explicitly use managed publication initially, then evaluate AOT separately if required. Reuse the repository's pinned SDK/package versions.

## 5. Shared testing design

Evolve `src/Testing/DigitalBrain.Testing/DigitalBrain.Testing.csproj`; do not replace xUnit with a new test framework or build a fake Orleans runtime.

### Small reusable surface

1. A disposable `BrainTestHost` starts an isolated real Orleans test cluster using production registration. It exposes the production brain client and explicit service overrides.
2. A typed signal probe subscribes before an action, records matching facts, supports bounded asynchronous expectations, and records useful failure diagnostics.
3. A behavior run handle owns cancellation and its task. Readiness tracking observes actual completed client subscriptions; it does not declare readiness merely because `Run` returned a task. It surfaces faults immediately and joins the behavior during cleanup.
4. Restart/storage support provides unique persistent stores, grain/silo restart controls, and narrowly scoped read/write fault injection.
5. One bounded wait helper supports asynchronous read-only predicates, cancellation, timeout, and last-observed-value diagnostics. It bounds the read itself, not just delays between reads.

These are proposed responsibilities, not five mandatory public classes. Start with only the helpers used by the kernel suite and pilot module. Keep xUnit assertions in test projects and runner configuration in `Module.Tests.props`.

### Test layers

| Layer | Exercise | Replace | Purpose |
| --- | --- | --- | --- |
| Plain behavior/domain tests | Actual reusable C# behavior/logic | Narrow provider or client adapters only where useful | Filtering, transformations, branch decisions |
| Module integration tests, default | Real production grain, serialization, runtime, client, registration | External provider at its existing seam | Typed calls, state, emitted facts, errors |
| Persistence/fault tests | Real grain and serialization across fresh activations/hosts | Controlled storage adapter | Surviving writes, failed writes, restart semantics |
| Provider/HTTP integration tests | Real adapter and endpoint registration | Local protocol fixture or disposable backing service | Serialization, HTTP failures, provider assumptions |
| Small application smoke suite | Production composition and selected behaviors | Explicit external test dependencies | Wiring and file-based entry points |

Every asynchronous scenario follows: arrange -> subscribe/start -> await readiness -> trigger once -> assert observations/results -> cancel and join. Polling callbacks never send messages or mutate state. Negative assertions need a known processing checkpoint or a clearly bounded observation interval; an immediate empty list is not sufficient.

Each test gets isolated grain IDs/storage. Shared expensive fixtures are opt-in and must retain per-test isolation. Cleanup runs even after assertion/startup failure. Teardown must dispose hosts, subscriptions, behavior tasks, and temporary resources with bounded waits. Capture recent logs and behavior failures; avoid putting credentials or email bodies in default diagnostics.

Module-specific fakes live under their module's `Tests/`, or a module-owned testing package only when another test assembly actually needs reuse. For example, Gmail tests run the real Gmail neuron with a fake provider; they do not substitute an unrelated Gmail grain from the common testing package.

### Persistence and time

Retain the useful idea of `FileGrainStorage`, not an unconditional copy. The existing adapter serializes state but generates ETags without checking write conflicts, so it cannot validate production concurrency semantics. Specify/test its limited role, add required storage semantics, and retain a focused real-provider test where correctness depends on provider behavior. Memory storage alone does not prove process-restart persistence.

Replace journal fault injection with faults at the actual state-storage seam: reject a write, hold/release a read, and test uncertain outcomes only if the production provider exposes that possibility. Delete journal storage adapters when the old runtime retires.

Inject `TimeProvider` for domain time. Advancing it must not be advertised as advancing Orleans' scheduler or reminders. The Time module owns its scheduling tests, including a small real-runtime reminder test and a documented overdue-schedule policy. Do not recreate a general deterministic distributed simulator.

### Required foundation tests

- Two independent ready subscribers observe one typed publication; unrelated types and sources do not leak.
- Same string key under different grain types stays isolated.
- Cancellation/disposal removes registrations; registration/cleanup failures release object references.
- Lease renewal works beyond the lease interval; a failed renewal becomes a visible subscription failure.
- Buffer overflow is bounded and visible; observer transport remains documented as best effort.
- A successful state write survives deactivation and a new host using the persistent store.
- A refused write produces no success fact and is not exposed as committed state after recovery.
- State committed before an interrupted publication survives while the fact is allowed to be absent.
- Restart does not replay old signals or resume an interrupted C# stack.
- A failed behavior fails its test promptly; test cancellation and disposal cannot hang indefinitely.
- One provider failure test and one module restart test use the real pilot implementation.

## 6. Deletion ledger

Deletion is conditional on migrating/removing callers, not on hiding their files from compilation.

| Existing area | Action | Replacement / reason |
| --- | --- | --- |
| Contracts `Commands/*`, command IDs, `Accepted<T>` | Delete | Normal typed calls/results; module-owned operation identifiers only when required |
| Runtime `Commands/*` | Delete | No framework replay/deduplication ledger |
| Contracts `Journals/*`; runtime `Journal*`, `BoundedJournal` | Delete | Optional diagnostics plus module state reads |
| `PendingWork`, `RetryScheduler`, `INeuronInbox`, `ReactionContext`, admission types | Delete | Ordinary method execution; no generic durable mailbox |
| `AnnouncementDrain`, `SnapshotEnvelope`, applied-delivery markers | Delete | Module state and separately published live facts |
| `PersistenceFence` and activation/caller filters | Delete old implementations | Small explicit state failure discipline; ordinary tracing and adapter authorization |
| Synapse types and generic routing | Delete | Behavior code subscribes and calls typed methods |
| Descriptor/invoker catalog in core | Remove from core | Keep only demonstrated dynamic tool needs in AI/MCP adapters |
| `ModuleManifest`, `ModuleHostingAttribute`, builder/projection hierarchy | Replace | Explicit runtime/module/resource registration |
| Entire `BehaviorRuntime` | Delete after consumers migrate | C# behavior code; no new DSL/interpreter |
| File/fault journal test storage and journal waits | Delete | State-storage tests and typed probes |
| Prototype test-owned Gmail/Twitter/ETL logic | Keep as examples only or move to owner tests | Real modules and production client |
| Generic base inbox and global signal history | Remove from business contracts | Optional bounded diagnostic collection |
| Unused package references, compile exclusions, legacy solution entries | Delete | Active source/build inventory |

Do not delete OAuth, token handling, domain validation, provider error handling, or permission enforcement merely because they are verbose. Separate real domain requirements from obsolete command/graph plumbing.

## 7. Migration sequence and acceptance gates

### Stage A: production foundation plus shared tests

Implement the small contracts/runtime/client and shared test host together. Use a minimal test-owned persistent probe grain to establish semantics. Finish the subscription and persistence tests above before migrating external providers.

Work in an isolated migration checkout/branch. During module migration, keep an explicit temporary foundation solution listing migrated projects; label it as partial. The existing complete solution remains the inventory of the final product. Do not report the whole migration green while only the foundation solution passes. No old/new runtime bridge and no indefinite parallel framework packages.

Gate: framework runs without `DigitalBrain.Testing` in its production dependency graph; the shared harness uses the same runtime/client; an actual file-based app builds and connects through production hosting; state/restart and live-subscription semantics are proven.

### Stage B: Time pilot, then Memory

Time provides a small real module without external credentials, proving state, typed results, facts, cancellation, and scheduling. Preserve useful scheduled-state semantics and decide its overdue policy locally. Memory then proves replaceable external dependencies and module state without AI/UI composition.

Gate: each pilot's tests use its real grain, clean DI overrides, one-stimulus assertions, provider failure where applicable, and persistent-state recovery. The common test package contains no pilot-specific logic.

### Stage C: AI seam, integrations, and data modules

First remove AI's dependency on the MCP executable and old behavior runtime. Keep any reusable tool adaptation in a library or the owning adapter; do not make production module implementations depend on executables. Simplify AI contracts before migrating consumers of those contracts.

Then migrate Google, Microsoft, and Salesforce integrations. Convert acceptance/scheduled-signal pairs to typed methods and result/fact publication. Preserve provider-specific authorization and uncertain side-effect handling.

Decouple data contracts and implementations from Flutter rendering, then migrate ClickHouse, Supabase, Excel, and Coding in dependency order. The observed UI/AI dependencies prevent treating all these modules as independent rewrites. Reinspect each module before fixing its detailed order.

Gate per module: new contract and implementation, meaningful tests, migrated callers, obsolete files removed, package references reduced, no legacy command/JSON routing remaining unless justified at an external transport.

### Stage D: behavior and application cutover

Reimplement retained behaviors as normal C# methods with file-based entry points. Migrate IntoChat, HTTP/MCP adapters, Flutter projections, and production composition to typed operations/live facts. UI reads state when reconnecting; it cannot depend on replay of a global journal.

Retire the behavior editor/compiler/runtime surfaces that no longer serve C# programming. Product decisions about editing/deploying user-authored code belong to a later scope, not to this framework foundation.

Gate: the original full product inventory is accounted for as migrated or deliberately retired; production build, module suites, file-based app builds, and application smoke tests pass.

### Stage E: final removal

Delete `BehaviorRuntime`, obsolete runtime/contracts, unused packages and test helpers, all migration-only solution/configuration scaffolding, and stale documentation. Replace the main solution's entries with the final active product.

Gate: no legacy references or `Compile Remove` graveyard; no production reference to `DigitalBrain.Testing`; no module implementation dependency on UI implementation or executable hosts. Review both the compiled project graph and files left on disk.

## 8. Verification performed and remaining limits

On 2026-09-19, with SDK `11.0.100-rc.1.26425.128`, ran in each repository:

```powershell
dotnet test --project src/Modules/DigitalBrain/Tests/DigitalBrain.Runtime.Tests.csproj -p:CodeGraphRefresh=false --no-restore
```

- Existing repository: 12 passed, 0 failed, 0 skipped.
- Prototype repository: 6 passed, 0 failed, 0 skipped.
- Prototype build emitted MSB3539 warnings for all three behavior projects: `BaseIntermediateOutputPath` is set too late.
- These are focused baseline runs, not validation of all modules, persistence, production deployment, or the proposed design.
- No production source changed during this investigation. Existing Flutter generated-file modifications were left untouched.

Context7 was attempted but returned a monthly-quota error. Platform checks used primary Microsoft documentation:

- [File-based apps](https://learn.microsoft.com/en-us/dotnet/core/sdk/file-based-apps): project/include directives, inherited repository build configuration, explicit file execution, managed/AOT publishing.
- [Broadcast channels](https://learn.microsoft.com/en-us/dotnet/orleans/streaming/broadcast-channel): live broadcast is not persistent storage or replay.
- [Observers](https://learn.microsoft.com/en-us/dotnet/orleans/grains/observers): client object references, subscription management, observer lifetime.

## 9. Review and next deliverable

The user approved this written design on 2026-09-19, including the subscription surface, package ownership, testing responsibilities, and migration stages.

Self-review: the design preserves durable module state while explicitly dropping delivery/workflow recovery; it separates production hosting from tests; it distinguishes a partial migration build from the whole product; it places domain fakes with their owners; and every deletion has a consumer/cutover condition.

The next deliverable is a concrete implementation plan for Stage A and the Time pilot: exact files, behavior-level acceptance tests, commit-sized steps, and an execution choice. Later module plans should use evidence gathered during their own migrations.
