# DigitalBrain Neurons, Synapses, Behaviors, Memory, and Discovery

**Status:** Approved

**Date:** 2026-07-30

**Scope:** Architectural direction and implementation sequencing only

## 1. Outcome

DigitalBrain becomes a system where reusable modules publish neurons and directed synapse contracts, behaviors compose those contracts as single-file C# programs, Tasks provides durable execution, and the assistant discovers new capabilities automatically.

The motivating flow must work without a hand-written `read_recent_messages` tool:

```text
User: "Read my last three emails"
  → AI searches the active capability projection
  → exact catalog identifies GoogleModule / IGmail / GmailRequest
  → assistant sends GmailRequest
  → GoogleModule selects and calls its configured Gmail MCP tools
  → missing authorization yields a user-action synapse
  → the owning Task pauses and Flutter shows "Connect Google"
  → authorization completes and the same Task continues
  → GmailResponse returns the requested emails
```

Adding another module must follow the same path without editing the assistant. Published behaviors must also become discoverable.

## 2. Design principles

1. **Pure directed synapses.** Neuron interfaces identify capabilities. Synapses are the contracts sent between neurons. Operation-shaped interface methods are not the programming model.
2. **Deep, reusable modules.** Google, Salesforce, Tasks, Memory, Behaviors, and community modules own their internal providers and expose small neuron/synapse surfaces.
3. **One durable lifecycle.** The existing Tasks module owns task state, attempts, blockers, cancellation, retry, continuation, and outcomes. There is no parallel `KernelTask` or `WorkId`.
4. **Manifests are canonical at runtime.** CLR names, preview compiler lowering, MCP tool names, and provider details do not define persisted identities.
5. **Behavior intent is readable and executable.** English BDD scenarios define expected behavior; C# implements it.
6. **External systems stay behind module boundaries.** Qdrant, Gmail MCP, OAuth, and Salesforce details are provider implementation choices.
7. **Discovery is automatic but verified.** Semantic search finds candidates; the exact active catalog validates them.
8. **Do not make the kernel understand accounts, providers, or behavior code.**

## 3. Current architecture findings

The current system already contains most of the correct foundations:

- The source generator discovers referenced `IModule` implementations and produces `CompiledModuleCatalog`.
- Aspire `AddModule<TModule>()` selects active modules and projects their IDs into resources.
- Neurons, synapse journals, owner isolation, capability facts, and source-generated dispatch are established.
- `DigitalBrain.Testing` runs real compiled modules in an Orleans test cluster with controlled time, journals, restart support, fault injection, and scripted external edges.
- `TasksModule` already models goals, attempts, blockers, continuation, cancellation, and results.
- `BehaviorNeuron` already owns signed revisions, canonical artifacts, an admission gate, execution limits, and a separate Behavior Host seam.
- The behavior compiler already parses with `LanguageVersion.Preview`.
- Google and Salesforce MCP authorization rails already have typed facts and tests that keep secrets out of journals.

The important gaps are:

- `Agent.ToolsFor(...)` supplies AI capabilities manually, so adding GoogleModule does not automatically expose Gmail to the assistant.
- Current behavior grants and host access are method-shaped instead of neuron/synapse-shaped.
- The current behavior program surface does not cleanly support user-defined, multi-case trigger contracts.
- Behavior assemblies are still loadable in-process in places; authored code must not enter the silo.
- The Flutter app can inspect the Behaviors module in Brain, but does not expose behavior listing, explanation, editing, tests, revisions, or operational controls.
- There is no reusable public vector-memory module or automatic semantic capability projection.

IAW provides useful precedents—Orleans-connected C# scripting, durable startup work, and Qdrant-backed semantic lookup—but DigitalBrain should reuse the ideas through its existing modules, Tasks lifecycle, and directed synapses instead of copying IAW's cluster-client shape.

## 4. Module and synapse programming model

Neuron interfaces remain small identity contracts:

```csharp
public interface IGmail : INeuron;
public interface ISalesforce : INeuron;
public interface IVectorMemory : INeuron;
```

They do not accumulate methods such as `ReadRecentMessages`. A caller gets a typed neuron proxy and sends a synapse:

```csharp
await using var brain =
    await DigitalBrainClient.ConnectAsync<ResearchCompanyRequest>();

var gmail = brain.Get<IGmail>();

var emails = await gmail.SendAsync(
    new GmailRequest("Read my last three emails"));
```

`SendAsync` is common client plumbing over directed delivery, correlation, and result synapses. `IGmail` does not declare `SendAsync` or Gmail operations.

`GmailRequest` is intentionally intent-level. GoogleModule can use an LLM and the live MCP tool catalog to translate it into provider calls. Its public contract does not mirror every Gmail MCP method.

The same rule applies to Salesforce, Memory, Tasks, and community neurons.

### Capability grants

Behavior grants are expressed as allowed directed edges:

```text
behavior contract
  → target neuron contract
  → accepted request/result synapse contract IDs and versions
```

They replace method aliases. The behavior compiler derives requested edges from `brain.Get<TNeuron>()` and sent synapse types. Admission rejects undeclared or unavailable dependencies, and Flutter explains newly requested capabilities before publication.

## 5. Capability catalog and automatic discovery

The existing generated module catalog becomes the authoritative structural catalog.

Each compiled module contributes generated metadata for:

- Module ID and version
- Neuron contract IDs
- Accepted and emitted synapse contract IDs and versions
- JSON schemas
- Reader-facing descriptions and examples
- Required module configuration keys
- Compatibility information

`AddModule<TModule>()` determines which compiled entries are active. Published behavior manifests enter the same active catalog dynamically.

The catalog is exact and authoritative. It does not depend on vector search.

MemoryModule maintains a rebuildable semantic projection of the catalog in a reserved system namespace. Before an AI turn:

1. Search the capability projection using the user's prompt.
2. Resolve candidates against the exact active catalog.
3. Remove inactive, incompatible, or unauthorized candidates.
4. Materialize only the relevant neuron/synapse schemas as model tools.
5. Let the model select and send the appropriate synapse.

This replaces hardcoded `ToolsFor(...)` lists. A new module or published behavior becomes searchable without changing AI code.

Tasks may durably reconcile or retry projection work, but Tasks does not own the catalog. If VectorMemory is unavailable, exact lookup still works; only semantic discovery is degraded.

## 6. MemoryModule

Memory is a reusable community module, not an internal assistant database.

The initial public neuron is `IVectorMemory`, with vector-specific store, search, and removal synapses. Callers provide an owner-visible namespace and payload/reference appropriate to their use case.

Required scopes include:

- Reserved DigitalBrain capability projection
- Published behavior discovery
- Owner/user memory
- Module-owned memory
- Community/application-defined namespaces

Access checks prevent user or community data from overwriting system projections, while still allowing any authorized module or behavior to use vector storage for its own purpose.

Qdrant is an encapsulated provider configured through Aspire, conceptually:

```csharp
brain.AddModule<MemoryModule>(memory =>
    memory.WithQdrant(...));
```

Community code programs against `IVectorMemory`, not Qdrant. A private provider neuron may exist internally, but `IQdrant` is not the portable public contract.

Graph memory has a different model and different synapses. A future `IGraphMemory` remains separate rather than being forced through vector contracts.

## 7. Behavior authoring model

### Files

Each behavior revision has two human-facing authored files:

```text
Behavior.cs       single-file C# application, including its executable test bindings
Behavior.feature  English Gherkin scenarios
```

Generated manifests, schemas, assemblies, and signing material are artifact internals and are not presented as an editable project tree.

There is no `.ino` format and no embedded terminal, NuGet UI, or full IDE in the first release.

### Program shape

```csharp
await using var brain =
    await DigitalBrainClient.ConnectAsync<ResearchCompanyRequest>();

var request = brain.Trigger;
var gmail = brain.Get<IGmail>();
var memory = brain.Get<IVectorMemory>();
var salesforce = brain.Get<ISalesforce>();
```

The isolated worker supplies the trigger and the granted neuron proxies. The silo never loads authored CLR types.

### Behavior contract ladder

1. `Behavior.feature` is the authoritative, human-readable behavioral contract.
2. `Behavior.cs` is the implementation and executable test binding.
3. The friendly overview is generated from the approved scenarios and signed into the revision as a projection.

A natural-language change follows one path:

```text
change request
  → proposed scenario diff
  → user approval
  → C# and executable test-binding diff
  → compile and run every scenario
  → compatibility and security checks
  → regenerate overview
  → publish signed revision
```

Every scenario has a stable identity. Admission requires one executable result per scenario and rejects missing, duplicate, or orphaned test bindings.

## 8. Behavior input unions

Each behavior has one logical input synapse. C# 15 preview unions are the preferred authoring syntax for multiple accepted trigger cases:

```csharp
public sealed record ManualResearchRequest(string Prompt);

public union ResearchCompanyRequest(
    ManualResearchRequest,
    GmailMessageReceived,
    ScheduledTaskFired);
```

Cases may be:

- Existing reusable module synapses, such as `GmailMessageReceived`
- Behavior-owned synapses, such as `ManualResearchRequest`

Behavior-owned contracts are private by default and become generally discoverable only when explicitly published.

The root union is one behavior input endpoint. Its cases describe accepted source synapses; they do not create unrelated behavior entry points.

### Preview isolation

Preview is enabled only in the pinned Behavior compiler/worker toolchain.

- The repository-wide kernel and reusable modules remain on normal language settings.
- The kernel's existing abstract `Synapse` record remains unchanged.
- A native union's generated CLR struct is never a wire or journal format.
- The compiler lowers the root union into a canonical manifest `oneOf` schema.
- The signed artifact records the exact SDK/Roslyn toolchain.
- Previously published artifacts run with their pinned worker/toolchain support.
- Unsupported union shapes are rejected during admission.

Initially supported cases are immutable, unambiguous record shapes. Default/null union values, overlapping cases, and nested preview-union complexity are rejected.

### Stable identity and compatibility

Contract identity belongs to the manifest, not a CLR full name or assembly hash.

The manifest persists:

```text
BehaviorContractId
ContractMajorVersion
CaseId
CaseSchemaVersion
Payload schema
Result schema
```

Case IDs are generated once and remain stable. Renaming or structurally replacing a C# case requires an explicit mapping to the existing case ID or a new contract major version.

Adding or removing a root union case is treated as a breaking contract change unless an explicit compatibility policy proves otherwise. Existing callers, schedules, and Tasks stay pinned to their published contract version.

## 9. Activation bindings

Union membership defines what a behavior can accept. It does not automatically subscribe the behavior to every matching event.

A durable directed binding states what should activate it:

```text
source module + source synapse contract/version
  → optional module-owned source configuration
  → target behavior contract/version + union case ID
  → enabled state
```

Bindings are explicit and inspectable in Flutter. Source-specific configuration remains opaque to the source module. The kernel does not model Google accounts or Salesforce tenants.

When a matching event arrives, routing starts a Task for the target behavior with a protected payload reference.

## 10. Behavior runtime and Tasks

`BehaviorNeuron` remains the owner-scoped behavior control plane. It owns:

- Behavior identity
- Signed revisions
- Active revision
- Activation bindings
- Admission state
- Stop/start state

TasksModule owns execution:

- Task identity
- Attempts
- Blockers
- Retry and continuation
- Cancellation
- Result and failure state

There is no separate `KernelTask` type and no `WorkId`.

Each behavior attempt records deterministic operation history using:

```text
Task identity + attempt identity + operation sequence
```

On replay, completed operations return their recorded result instead of repeating the external effect. If a worker terminates while an external effect is in flight and completion cannot be proven, the Task becomes `OutcomeUncertain`; it is not retried blindly.

Auth or other user interaction may end the worker process. Continuing the Task starts a fresh worker and replays to the interrupted operation.

Authored behavior code runs in a separate, resource-limited worker process. It does not load into the Orleans silo. The worker receives only:

- The pinned artifact
- The protected trigger payload
- Granted neuron/synapse broker access
- Deterministic time/cancellation context

## 11. Module configuration and authorization

Module configuration is module-owned and may require Aspire secrets. GoogleModule and SalesforceModule publish their required configuration metadata. Missing configuration is shown as a module setup action.

There is no shared account registry, account selector lifecycle, or kernel account reference.

GoogleModule owns:

- Its MCP provider and live tool catalog
- OAuth/token state
- Any concept of multiple Google connections
- Default or prompt-based connection choice
- Ambiguity resolution

If a request cannot complete without user interaction, the module emits a minimal directed `UserActionRequired` control synapse to the owning Task. It contains only:

- Task identity
- Module identity
- Display text
- Protected action reference
- Expiration

Tasks pauses. Flutter renders the module-provided action. After completion, the same Task continues and behavior code observes a normal completed `SendAsync`.

Behavior code does not pattern-match OAuth results or catch provider authorization exceptions. Salesforce follows the same mechanism.

Secrets, tokens, authorization codes, raw protected payloads, and sensitive MCP results never enter journals, manifests, or VectorMemory.

## 12. Cancellation and Stop

Behavior code and neuron operations use ordinary cooperative `CancellationToken` handling.

The Flutter **Stop behavior** action:

1. Atomically closes the behavior's activation gate, preventing new Tasks without mutating its binding records.
2. Requests cancellation of active behavior Tasks.
3. Lets an active attempt stop at the next safe synapse boundary.
4. Uses `OutcomeUncertain` only when an already-started external effect cannot be classified.
5. Preserves source, scenarios, revision history, and bindings.

The visible lifecycle is:

```text
Running → Stopping → Stopped
```

Restart opens the activation gate and does not create a new behavior revision.

## 13. Flutter Behavior Studio

Flutter gains a first-class **Behaviors** workspace beside Chat, Activity, and Brain.

The approved product direction has six connected views:

1. **Library** — running, draft, and stopped behaviors with purpose and dependencies.
2. **Overview** — generated natural-language explanation, triggers, capabilities, health, Run once, Stop, and Ask assistant to change.
3. **Scenarios** — readable executable Gherkin and pass/fail evidence.
4. **Assistant change** — request, scenario approval, code generation, verification, and publication steps.
5. **Source + tests** — the real single-file C# program and English feature file, with compile/test/compatibility/security results.
6. **Revisions** — immutable signed revision history and restoration by creating a new verified revision.

The default experience is intent and evidence, not implementation. Developers can deliberately enter Source + tests. Non-programmers can understand behavior, stop it immediately, and request a test-driven modification without editing C#.

Publishing remains impossible until all admission checks pass.

## 14. Testing strategy

Extend `DigitalBrain.Testing`; do not build a parallel simulator.

### Contract/compiler tests

- Preview union lowering to canonical schemas
- Stable contract and case identity
- Compatible and breaking edits
- Toolchain pinning
- Rejection of unsupported/default union values
- One executable result per BDD scenario

### Module/neuron tests

Run real compiled modules in `DigitalBrainFixture`. Fake only external provider edges:

- MCP provider
- OAuth callback
- Qdrant/vector provider
- Model response

Assert incoming/outgoing synapses, Task state, and journal evidence.

### Behavior acceptance tests

- Signed artifact admission in the isolated worker
- Scenario pass/fail reporting
- Capability-grant enforcement
- Process termination and deterministic replay
- Cancellation at synapse boundaries
- `OutcomeUncertain` for ambiguous effects
- Secrets absent from journals and vector projections

### Provider contract tests

Run the same `IVectorMemory` contract suite against an in-memory provider and Qdrant. Verify scope isolation and provider interchangeability.

### Critical Aspire tests

Keep full assembled tests few and outcome-focused:

```text
prompt asks for last three emails
  → automatic IGmail discovery
  → GmailRequest delivery
  → authorization action
  → Task suspension
  → callback
  → continuation/replay
  → final response
```

Also prove:

- Adding a test module makes it discoverable without AI code changes.
- Publishing a behavior indexes it for semantic discovery.
- User/community vector data cannot contaminate capability discovery.
- A union-case rename cannot silently change contract identity.

## 15. Implementation sequence

This umbrella design should be implemented as separate reviewable plans and PRs:

### Slice 1 — Synapse-first contracts and generated catalog metadata

- Replace method-shaped behavior grants with neuron/synapse edges.
- Generate module neuron/synapse/schema metadata.
- Expose the exact active catalog to clients and AI orchestration.
- Preserve existing module behavior through compatibility adapters only where migration requires them.

### Slice 2 — Behavior artifact and preview-union compiler

- Establish `Behavior.cs` + `Behavior.feature`.
- Add union lowering, stable case IDs, compatibility gates, and toolchain pinning.
- Update the signed artifact and BDD admission report.

### Slice 3 — Tasks-owned isolated behavior execution

- Move all authored code to the isolated worker.
- Broker granted synapses.
- Add operation-history replay, user-action suspension, cooperative cancellation, and `OutcomeUncertain`.
- Remove obsolete in-process behavior loading.

### Slice 4 — MemoryModule and capability projection

- Add reusable `IVectorMemory`.
- Add in-memory test provider and Qdrant provider.
- Add protected namespaces and capability/behavior semantic projections.

### Slice 5 — Google and Salesforce neuron surfaces

- Replace public MCP-method mirroring with intent synapses such as `GmailRequest`.
- Keep MCP tool selection, OAuth, connection state, and provider details inside each module.
- Integrate `UserActionRequired` with Tasks and Flutter.

### Slice 6 — Automatic AI routing

- Replace hardcoded `Agent.ToolsFor(...)` capability lists with semantic candidate retrieval plus exact-catalog validation.
- Materialize only relevant neuron/synapse tools per turn.

### Slice 7 — Flutter Behavior Studio

- Add the six approved views and flows.
- Add Stop/start, assistant-led test-first revision, source/test inspection, admission results, and revision history.

### Slice 8 — End-to-end hardening

- Add the critical Gmail, discovery, replay, cancellation, memory-isolation, and behavior-publication Aspire tests.
- Remove compatibility seams only after the new flow is proven.

## 16. Explicit non-goals

- Repository-wide C# preview
- Kernel-wide `ISynapse` migration merely to support union syntax
- Operation-specific APIs such as `read_recent_messages`
- A shared account subsystem
- `KernelTask` or `WorkId`
- Loading authored behavior assemblies into the silo
- Vector search as the authoritative capability catalog
- Public Qdrant coupling
- Combining vector and graph memory contracts
- Automatic subscription merely because a union accepts an event type
- A full IDE, terminal, package manager, or debugger inside Flutter
- Direct editing of generated manifests or behavior overview text

## 17. Acceptance criteria

The design is successfully implemented when:

1. Adding an active module automatically contributes its neurons and synapses to exact and semantic discovery.
2. The assistant can satisfy “read my last three emails” through `IGmail` and `GmailRequest` without a Gmail-specific AI method wrapper.
3. Missing authorization produces a clickable Flutter action and continues the same Task afterward.
4. A community developer can use `IVectorMemory` independently of DigitalBrain's internal projections.
5. A user can create a behavior-specific input union without changing a central interface.
6. A behavior revision cannot publish until its English scenarios, C# implementation, compatibility, capability grants, and security checks pass.
7. Behavior code runs only in an isolated worker and replays without duplicating proven external operations.
8. A non-programmer can understand, stop, and request a test-driven behavior change from Flutter.
9. Adding Google, Salesforce, Memory, or fifteen future modules does not require editing hardcoded assistant tool lists.

## 18. References

- Current module composition: `src/core/kernel/DigitalBrain.SourceGeneration/DispatchManifestGenerator.Composition.cs`
- Current AI tool seam: `src/modules/ai/DigitalBrain.Modules.AI/Agent.cs`
- Current behavior runtime: `src/core/behaviors/`
- Current Tasks module: `src/modules/tasks/`
- Current test framework: `src/core/testing/DigitalBrain.Testing/`
- Current MCP authorization proof: `src/core/mcp/DigitalBrain.Integrations.Tests/AuthorizationRail.cs`
- [C# union reference](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/union)
- [C# union feature specification](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/proposals/unions)
- [What's new in C# 15](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-15)
- [Roslyn language feature status](https://github.com/dotnet/roslyn/blob/main/docs/Language%20Feature%20Status.md)
