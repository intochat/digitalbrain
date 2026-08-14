# CoreV2 Framework Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (- [ ]) syntax for tracking.

**Goal:** Build a clean, deterministic CoreV2 framework that lets modules deliver durable intelligent behavior through typed Operations, BrainActivities, Neurons, Capabilities, and an explicit sharded BrainGraph, without importing any V1 DigitalBrain contracts or external-provider schemas.

**Architecture:** CoreV2 is a new bounded context under src/CoreV2 with a small Brain.Abstractions contracts project, an Orleans-based Brain.Core runtime, a reusable Brain.Testing in-process host, and one deterministic proof module. MCP and Flutter are future adapters over the Operation and Activity APIs; neither is part of this proof. The runtime persists durable Activity, graph, wiring, and outbox state; it validates manifests, resolves policy-derived endpoints, snapshots routes at emission, and exposes only redacted Activity projections.

**Tech Stack:** .NET 11 preview (global.json), C# latest, Orleans 10.2.2, Orleans TestingHost 10.2.2, xUnit v3, Microsoft.Testing.Platform, central NuGet package management, GitHub Actions.

## Global Constraints

- Create CoreV2 only beneath src/CoreV2 and tests/CoreV2. It must contain zero ProjectReference, namespace, runtime type, serializer alias, or persistence dependency on DigitalBrain.*.
- Retain all V1 source and behavior unchanged. CoreV2 is a clean proof and must not migrate, wrap, or reuse V1 Synapse, Neuron, IAgent, MCP gateway, UI, chat, capability-index, or broadcast types.
- Preserve the repository-wide settings in Directory.Build.props: net11.0, nullable enabled, implicit usings enabled, warnings as errors, preview analysis, and enforced code style.
- Do not add billing, prices, invoices, quotas, reservations, payment state, or metering implementation. Keep the future accounting seam as correlation and attribution fields only.
- Do not implement MCP transport, Flutter transport, Conversation, AI providers, Salesforce, Google, OAuth, external adapters, or a module marketplace in the proof.
- The only public behavior boundary is a versioned Operation. The product flow is discover, invoke, observe. There is no public fire, get_neurons, endpoint selector, graph mutation tool, raw journal reader, or arbitrary event injection endpoint.
- Operation input, terminal result, and optional progress updates are sealed CLR records in a module contracts project. Product adapters may convert JSON at ingress or egress, but JSON, JsonElement, object, dynamic, provider SDK messages, and provider tool schemas never enter the CoreV2 event bus.
- DomainEvent means an immutable past-tense internal fact. Operation means a versioned public intent. Capability means a typed module-published facility used by a Neuron inside a BrainActivity. Do not conflate these three categories.
- A Neuron is the only component that makes behavior decisions. BrainGraph, Wiring, policy, serializers, module registry, delivery dispatcher, and adapters remain deterministic infrastructure.
- All graph changes are authorized operations. A Rewire is evidence; it cannot mutate a Synapse by existing.
- Synapses have no expiry and no weight. An authorized install, replace, or retire creates an immutable revision. Usage is not implemented in the proof and must never mutate topology automatically.
- BrainGraph is logically per Workspace but physically sharded by deterministic outbound-source ownership. A source, contract, scope, and wiring slot are immutable for a SynapseKey.
- Delivery is at-least-once. The receiver atomically deduplicates a delivery derived from the source event firing and Synapse revision with its state, journal, and next outbox work.
- Every authenticated state-changing invocation includes a caller idempotency key. A retry returns the existing BrainActivity; reuse for another Operation is rejected.
- Every behavior Capability use requires an ambient ActivityContext with verified Workspace and Principal. The proof has no anonymous system path. Future background work must use a registered service Principal.
- Each module is operator-installed and trusted. A manifest validates structure; it is not a security sandbox.
- A contracts package is the only allowed compile-time dependency between modules. A consumer must never reference another module implementation, Entity, grain type, provider SDK type, or endpoint.
- Raw payload retention, cryptographic erasure, production identity, consent, billing, and external-secret storage are documented seams, not proof implementations. Tests must prove no raw state crosses the public Activity view.

## Scope and Delivery Shape

The implementation is intentionally one independently testable framework slice:

1. A test caller invokes a public proof Operation with a verified Workspace, Principal, and idempotency key.
2. CoreV2 authorizes and creates or resumes a BrainActivity, derives the declared entry role endpoint, and sends the typed operation to that Neuron.
3. The entry Neuron decides and emits a typed DomainEvent. BrainGraph resolves the source shard and stages the exact delivery route in the same durable turn.
4. The receiver deduplicates, journals, updates its state, and produces a typed terminal Activity result.
5. Tests prove a private event cannot cross module boundaries, a public event can cross only through a manifest-approved Synapse, a pure Reshape is applied, a Rewire creates a new revision, a Retire stops future routes, and an incomplete multi-shard Wiring remains invisible.
6. A fake typed Capability is invoked only through an ambient ActivityContext and an attenuated delegation. The test host exercises the same Operation and Activity APIs that MCP and Flutter will eventually use.

The proof deliberately does not prove provider calls, streaming models, external retries, interactive confirmation, module disablement, privacy-key deletion, cross-workspace wiring templates, or a production persistence backend. Its interfaces must leave clear extension points for those later projects without adding their runtime dependencies.

## Planned File Structure

| Path | Responsibility |
| --- | --- |
| plans/COREV2.md | Replace V1-derived direction with the approved CoreV2 boundary, non-goals, and proof scope. |
| plans/COREV2-DICTIONARY.md | Add Operation and Capability; remove expiry and public fire/get_neurons language; make internal versus public boundaries explicit. |
| plans/COREV2-SCENARIOS.md | Replace Salesforce and chat/chart scenarios with framework-neutral Operation, Rewire, Retire, Wiring, and capability scenarios. |
| plans/pseudocode.md | Replace V1-style Synapse-as-packet pseudocode with Operation-to-Activity-to-Neuron pseudocode. |
| src/CoreV2/Brain.Abstractions | Stable CoreV2 identity, context, Operation, DomainEvent, Capability, module-manifest, policy, graph, wiring, and Activity contracts. |
| src/CoreV2/Brain.Core | Orleans grains and deterministic services for invocation, Activity lifecycle, endpoint resolution, graph shards, outbox dispatch, delivery deduplication, reshape validation, wiring staging, and capability dispatch. |
| src/CoreV2/Brain.Testing | In-process CoreV2 test host, deterministic clock, caller factory, policy fixture, graph inspector, and fake capability registry. |
| src/CoreV2/Modules/Proof.Contracts | Public proof Operation/result/progress records and the public proof event contract used by consumer tests. |
| src/CoreV2/Modules/Proof | Explicit manifest, proof entry/source/receiver Neurons, pure Reshape, deterministic Capability consumer, and terminal-result writer. |
| tests/CoreV2/Brain.Abstractions.Tests | Unit tests for validation, immutable IDs, descriptor invariants, and manifest dependency rules. |
| tests/CoreV2/Brain.Core.Tests | Unit and Orleans TestCluster tests for Activity, graph, outbox, delivery, wiring, policy, and capability behavior. |
| tests/CoreV2/Brain.Proof.Tests | End-to-end test-host acceptance tests that invoke the proof Operation and observe only public Activity projections. |
| Directory.Packages.props | Add no packages unless the test projects require a centrally managed package already absent from the file. |
| DigitalBrain.slnx | Add a CoreV2 solution folder, the four CoreV2 projects, the proof module projects, and three test projects. |
| .github/workflows/ci.yml | Run the CoreV2 test projects after the existing release build. |

## Contract Map

The following names are fixed by this plan. Do not introduce similarly named alternatives during implementation.

~~~text
Product adapter
    authenticated caller + JSON only at the edge
                 |
                 v
IOperationGateway.InvokeAsync<TInput, TResult>()
    validates descriptor, schema adapter, caller key, Workspace policy
                 |
                 v
BrainActivityGrain
    accepted / running / settled projection; idempotency and parent linkage
                 |
                 v
declared entry Neuron role -> derived endpoint -> direct Send
                 |
                 v
BrainNeuron<TState>
    journal + state + staged outbox in one committed turn
                 |
                 v
BrainGraphShardGrain.Resolve()
    exact Synapse revision + optional pure Reshape snapshot
                 |
                 v
DeliveryDispatcher -> receiver dedup -> next durable turn

Neuron-only extension path:
    ICapabilityBroker.UseAsync<TRequest, TResult>()
    requires ActivityContext + Delegation

Module extension path:
    contracts package -> explicit ModuleManifest -> ModuleRegistry validation
~~~

### Task 1: Reconcile the CoreV2 Direction Documents

**Files:**
- Modify: plans/COREV2.md
- Modify: plans/COREV2-DICTIONARY.md
- Modify: plans/COREV2-SCENARIOS.md
- Modify: plans/pseudocode.md

**Interfaces:**
- Consumes: the approved framework rules in this plan.
- Produces: a consistent vocabulary and scenario set for all implementation tasks.

- [ ] **Step 1: Replace the CoreV2 thesis and direction**

State that MCP and Flutter are equal adapters, that Operations are the public boundary, and that the proof contains no provider integration. Add this exact public-control sentence:

~~~text
Product callers discover eligible Operations, invoke one explicit Operation, and observe its policy-filtered BrainActivity. They do not fire DomainEvents, select Neurons, inspect topology, or mutate BrainGraph directly.
~~~

- [ ] **Step 2: Update the dictionary without weakening its definitions**

Add these two entries:

~~~text
### Operation
What it does. Names one versioned public intent that an authenticated caller may invoke.
What it is. A sealed input/result/progress contract plus manifest descriptor, authorization requirement, idempotency scope, owning module, and entry Neuron role.
What it is not. A DomainEvent, an endpoint, a graph command, a provider tool, or a second message bus.

### Capability
What it does. Lets a Neuron use a typed module-published facility while preserving the current ActivityContext and delegated authority.
What it is. A versioned request/result contract resolved by CoreV2 through an explicit module manifest.
What it is not. A public product operation, a Neuron identity, a provider SDK object, or ambient service-provider access.
~~~

Remove Synapse expiry, trial expiry, the public fire tool, and get_neurons from every document. Replace the former three-model-tools statement with discover, invoke, observe.

- [ ] **Step 3: Replace provider- and presentation-shaped scenarios**

Delete Salesforce, chat, chart, OpportunitiesObserved, ChartPointsAdded, and any client-supplied SynapseKey examples. Add a framework-neutral scenario in which:

1. a caller invokes Proof.Run@1;
2. the framework creates a BrainActivity and sends it to the proof entry role;
3. the source emits a published ProofProduced event;
4. the first Synapse revision reaches a summary behavior;
5. a correction Operation produces a Rewire evidence event;
6. authorized BrainGraph replacement changes only the target and Reshape;
7. later emissions go to the assessment behavior;
8. a Retire prevents a later emission from resolving;
9. a Wiring proposal is staged and activated for another Principal using only roles and public contracts.

- [ ] **Step 4: Replace pseudocode with the approved boundary**

Use an opaque SynapseKey returned by BrainGraph, a caller idempotency key, an Operation descriptor, a direct send to the entry role, and an outbox route snapshot. Do not use typeof as a persisted contract identifier and do not include JSON, object, JsonElement, provider rows, chat rendering, or a direct graph call from a client.

- [ ] **Step 5: Verify document consistency**

Run:

~~~powershell
rg -n -i "salesforce|opportunitiesobserved|chartpointsadded|expiry|until:|get_neurons|public fire|json.*bus|weight" plans/COREV2.md plans/COREV2-DICTIONARY.md plans/COREV2-SCENARIOS.md plans/pseudocode.md
~~~

Expected: no matches except an explicit statement that provider schemas and JSON do not enter the CoreV2 bus.

- [ ] **Step 6: Commit**

~~~powershell
git add plans/COREV2.md plans/COREV2-DICTIONARY.md plans/COREV2-SCENARIOS.md plans/pseudocode.md
git commit -m "docs: define CoreV2 framework boundary"
~~~

### Task 2: Create the Isolated CoreV2 Solution and Test Baseline

**Files:**
- Create: src/CoreV2/Brain.Abstractions/Brain.Abstractions.csproj
- Create: src/CoreV2/Brain.Core/Brain.Core.csproj
- Create: src/CoreV2/Brain.Testing/Brain.Testing.csproj
- Create: src/CoreV2/Modules/Proof.Contracts/Brain.Modules.Proof.Contracts.csproj
- Create: src/CoreV2/Modules/Proof/Brain.Modules.Proof.csproj
- Create: tests/CoreV2/Brain.Abstractions.Tests/Brain.Abstractions.Tests.csproj
- Create: tests/CoreV2/Brain.Core.Tests/Brain.Core.Tests.csproj
- Create: tests/CoreV2/Brain.Proof.Tests/Brain.Proof.Tests.csproj
- Modify: DigitalBrain.slnx

**Interfaces:**
- Consumes: Directory.Build.props and Directory.Packages.props.
- Produces: an independently buildable CoreV2 graph with no DigitalBrain.* reference.

- [ ] **Step 1: Write a failing isolation test**

Create tests/CoreV2/Brain.Abstractions.Tests/ProjectIsolationTests.cs:

~~~csharp
using Xunit;

namespace Brain.Abstractions.Tests;

public sealed class ProjectIsolationTests
{
    [Fact]
    public void CoreV2_project_references_do_not_contain_legacy_digitalbrain_projects()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../../"));
        var projectFiles = Directory.GetFiles(
            Path.Combine(root, "src", "CoreV2"),
            "*.csproj",
            SearchOption.AllDirectories);

        Assert.NotEmpty(projectFiles);
        Assert.All(projectFiles, project =>
            Assert.DoesNotContain("DigitalBrain.", File.ReadAllText(project), StringComparison.Ordinal));
    }
}
~~~

- [ ] **Step 2: Create the project references**

Use these project relationships exactly:

~~~text
Brain.Abstractions
Brain.Core -> Brain.Abstractions
Brain.Testing -> Brain.Abstractions, Brain.Core
Brain.Modules.Proof.Contracts -> Brain.Abstractions
Brain.Modules.Proof -> Brain.Abstractions, Brain.Core, Brain.Modules.Proof.Contracts
Brain.Abstractions.Tests -> Brain.Abstractions
Brain.Core.Tests -> Brain.Abstractions, Brain.Core, Brain.Testing
Brain.Proof.Tests -> Brain.Abstractions, Brain.Core, Brain.Testing, Brain.Modules.Proof.Contracts, Brain.Modules.Proof
~~~

Reference Microsoft.Orleans.Sdk from Brain.Abstractions, Microsoft.Orleans.Server and Microsoft.Orleans.Persistence.AzureStorage from Brain.Core, Microsoft.Orleans.TestingHost from Brain.Testing and Brain.Core.Tests, and xunit.v3 plus Microsoft.NET.Test.Sdk from each test project. Use centrally managed versions already in Directory.Packages.props.

- [ ] **Step 3: Add the projects under a CoreV2 solution folder**

Run:

~~~powershell
dotnet sln DigitalBrain.slnx add src/CoreV2/Brain.Abstractions/Brain.Abstractions.csproj src/CoreV2/Brain.Core/Brain.Core.csproj src/CoreV2/Brain.Testing/Brain.Testing.csproj src/CoreV2/Modules/Proof.Contracts/Brain.Modules.Proof.Contracts.csproj src/CoreV2/Modules/Proof/Brain.Modules.Proof.csproj tests/CoreV2/Brain.Abstractions.Tests/Brain.Abstractions.Tests.csproj tests/CoreV2/Brain.Core.Tests/Brain.Core.Tests.csproj tests/CoreV2/Brain.Proof.Tests/Brain.Proof.Tests.csproj
~~~

After the command completes, move the eight project entries under one new Slnx folder:

~~~xml
<Folder Name="/CoreV2/">
  <Project Path="src/CoreV2/Brain.Abstractions/Brain.Abstractions.csproj" />
  <Project Path="src/CoreV2/Brain.Core/Brain.Core.csproj" />
  <Project Path="src/CoreV2/Brain.Testing/Brain.Testing.csproj" />
  <Project Path="src/CoreV2/Modules/Proof.Contracts/Brain.Modules.Proof.Contracts.csproj" />
  <Project Path="src/CoreV2/Modules/Proof/Brain.Modules.Proof.csproj" />
  <Project Path="tests/CoreV2/Brain.Abstractions.Tests/Brain.Abstractions.Tests.csproj" />
  <Project Path="tests/CoreV2/Brain.Core.Tests/Brain.Core.Tests.csproj" />
  <Project Path="tests/CoreV2/Brain.Proof.Tests/Brain.Proof.Tests.csproj" />
</Folder>
~~~

- [ ] **Step 4: Run the isolation test before implementation**

Run:

~~~powershell
dotnet test tests/CoreV2/Brain.Abstractions.Tests/Brain.Abstractions.Tests.csproj --no-restore
~~~

Expected: PASS after the project references exist; build failures before the projects are scaffolded are expected.

- [ ] **Step 5: Build the whole solution**

Run:

~~~powershell
dotnet build DigitalBrain.slnx -c Release -warnaserror --nologo
~~~

Expected: PASS with the new projects and no changes to V1 projects.

- [ ] **Step 6: Commit**

~~~powershell
git add DigitalBrain.slnx src/CoreV2 tests/CoreV2
git commit -m "build: scaffold isolated CoreV2 solution"
~~~

### Task 3: Define CoreV2 Identity, Context, and Contract Primitives

**Files:**
- Create: src/CoreV2/Brain.Abstractions/Identity/StrongIds.cs
- Create: src/CoreV2/Brain.Abstractions/Context/WorkspaceContext.cs
- Create: src/CoreV2/Brain.Abstractions/Contracts/ContractId.cs
- Create: src/CoreV2/Brain.Abstractions/Contracts/ContractVersion.cs
- Create: tests/CoreV2/Brain.Abstractions.Tests/IdentityAndContextTests.cs

**Interfaces:**
- Consumes: Brain.Abstractions project.
- Produces: WorkspaceId, PrincipalId, BrainActivityId, ModuleId, OperationId, CapabilityId, NeuronRoleId, SynapseKey, WiringId, ContractId, IdempotencyKey, WorkspaceContext, ActivityContext, and Delegation.

- [ ] **Step 1: Write failing primitive-validation tests**

~~~csharp
[Fact]
public void Contract_id_requires_module_name_contract_name_and_major_version()
{
    Assert.Throws<ArgumentException>(() => new ContractId("proof"));
    Assert.Throws<ArgumentException>(() => new ContractId("proof/run"));

    var id = new ContractId("proof/run@1");

    Assert.Equal("proof/run@1", id.Value);
}

[Fact]
public void activity_context_cannot_pair_an_empty_workspace_with_a_principal()
{
    var principal = new PrincipalId("principal/alice");
    var activity = BrainActivityId.New();

    Assert.Throws<ArgumentException>(() =>
        new ActivityContext(WorkspaceId.Empty, principal, activity, new CorrelationId("corr/1")));
}
~~~

- [ ] **Step 2: Implement validated opaque value types**

Implement readonly record structs with a private or validating public constructor. Use Guid values for BrainActivityId, SynapseKey, WiringId, and delivery identifiers; use non-empty canonical strings for WorkspaceId, PrincipalId, ModuleId, OperationId, CapabilityId, NeuronRoleId, ContractId, CorrelationId, and IdempotencyKey. Keep all parsing and validation in these files so Orleans grain code never accepts naked string identity values.

Use this context shape:

~~~csharp
public sealed record WorkspaceContext(
    WorkspaceId Workspace,
    PrincipalId Principal,
    bool IsServicePrincipal);

public sealed record ActivityContext(
    WorkspaceId Workspace,
    PrincipalId Principal,
    BrainActivityId Activity,
    CorrelationId Correlation,
    Delegation Delegation);
~~~

Delegation must contain an immutable set of OperationId and CapabilityId values and expose an Intersect method. It must not contain endpoint identities, secrets, raw policy objects, or provider scopes.

- [ ] **Step 3: Run abstraction tests**

Run:

~~~powershell
dotnet test tests/CoreV2/Brain.Abstractions.Tests/Brain.Abstractions.Tests.csproj --filter "FullyQualifiedName~IdentityAndContextTests"
~~~

Expected: PASS.

- [ ] **Step 4: Commit**

~~~powershell
git add src/CoreV2/Brain.Abstractions tests/CoreV2/Brain.Abstractions.Tests
git commit -m "feat(corev2): add validated context primitives"
~~~

### Task 4: Define Operation, DomainEvent, Capability, and Activity View Contracts

**Files:**
- Create: src/CoreV2/Brain.Abstractions/Operations/OperationContracts.cs
- Create: src/CoreV2/Brain.Abstractions/Events/DomainEventContracts.cs
- Create: src/CoreV2/Brain.Abstractions/Capabilities/CapabilityContracts.cs
- Create: src/CoreV2/Brain.Abstractions/Activities/ActivityContracts.cs
- Create: tests/CoreV2/Brain.Abstractions.Tests/ContractCategoryTests.cs

**Interfaces:**
- Consumes: Task 3 primitive types.
- Produces: IOperation<TInput, TResult>, IDomainEvent, OperationDescriptor, EventDescriptor, CapabilityDescriptor, OperationInvocation<TInput>, OperationAccepted, ActivityStatus, ActivityView, ActivityProgress<T>, ActivityResult<T>, and ICapabilityBroker.

- [ ] **Step 1: Write failing category-boundary tests**

~~~csharp
[Fact]
public void operation_descriptor_requires_distinct_input_and_result_contracts()
{
    var input = new ContractId("proof/run-input@1");

    Assert.Throws<ArgumentException>(() => new OperationDescriptor(
        new OperationId("proof.run"),
        input,
        input,
        new NeuronRoleId("proof.entry"),
        new ModuleId("proof"),
        new ContractVersion(1)));
}

[Fact]
public void activity_view_contains_status_and_redacted_contract_references_not_raw_event_payloads()
{
    var view = ActivityView.Accepted(
        BrainActivityId.New(),
        new OperationId("proof.run"),
        new ContractId("proof/run-result@1"));

    Assert.Equal(ActivityStatus.Accepted, view.Status);
    Assert.DoesNotContain(
        typeof(ActivityView).GetProperties(),
        property => property.Name.Contains("Journal", StringComparison.Ordinal));
}
~~~

- [ ] **Step 2: Implement the three contract categories**

Use these marker and generic interfaces:

~~~csharp
public interface IDomainEvent;

public interface IOperation<TInput, TResult>
    where TInput : class
    where TResult : class;

public interface ICapability<TRequest, TResult>
    where TRequest : class
    where TResult : class;
~~~

An OperationDescriptor must include OperationId, input ContractId, terminal-result ContractId, owning ModuleId, entry NeuronRoleId, and major ContractVersion. An EventDescriptor must include ContractId, owner ModuleId, event CLR type, and visibility. Visibility has exactly Internal and Published values. A CapabilityDescriptor must include CapabilityId, request ContractId, result ContractId, owner ModuleId, and major ContractVersion.

Define ActivityView only as a projection:

~~~csharp
public sealed record ActivityView(
    BrainActivityId Activity,
    OperationId Operation,
    ActivityStatus Status,
    ContractId TerminalResultContract,
    ActivityProgressReference? Progress,
    ActivityResultReference? Result,
    ActivityProblem? Problem);
~~~

ActivityResultReference and ActivityProgressReference contain only an approved contract reference and opaque activity-local payload reference. Their payload retrieval API is generic and policy-checked; do not put object, JsonElement, a journal entry, an Entity snapshot, or a provider response on ActivityView.

- [ ] **Step 3: Define invocation and capability contracts**

Use these signatures:

~~~csharp
public interface IOperationGateway
{
    Task<OperationAccepted> InvokeAsync<TInput, TResult>(
        OperationDescriptor operation,
        TInput input,
        WorkspaceContext caller,
        IdempotencyKey idempotencyKey,
        CancellationToken cancellationToken)
        where TInput : class
        where TResult : class;

    Task<ActivityView> ObserveAsync(
        BrainActivityId activity,
        WorkspaceContext caller,
        CancellationToken cancellationToken);
}

public interface ICapabilityBroker
{
    Task<TResult> UseAsync<TRequest, TResult>(
        CapabilityDescriptor capability,
        TRequest request,
        ActivityContext context,
        CancellationToken cancellationToken)
        where TRequest : class
        where TResult : class;
}
~~~

- [ ] **Step 4: Run contract tests**

Run:

~~~powershell
dotnet test tests/CoreV2/Brain.Abstractions.Tests/Brain.Abstractions.Tests.csproj --filter "FullyQualifiedName~ContractCategoryTests"
~~~

Expected: PASS.

- [ ] **Step 5: Commit**

~~~powershell
git add src/CoreV2/Brain.Abstractions tests/CoreV2/Brain.Abstractions.Tests
git commit -m "feat(corev2): define operation event and capability contracts"
~~~

### Task 5: Build Explicit Module Manifest Registration and Validation

**Files:**
- Create: src/CoreV2/Brain.Abstractions/Modules/ModuleManifest.cs
- Create: src/CoreV2/Brain.Abstractions/Modules/ModuleDependency.cs
- Create: src/CoreV2/Brain.Core/Modules/ModuleRegistry.cs
- Create: src/CoreV2/Brain.Core/Modules/ManifestValidator.cs
- Create: tests/CoreV2/Brain.Core.Tests/ManifestValidatorTests.cs

**Interfaces:**
- Consumes: Task 4 descriptors.
- Produces: ModuleManifest, ModuleDependency, IModuleRegistry, ManifestValidationException, and a resolved immutable ModuleSet.

- [ ] **Step 1: Write failing manifest tests**

~~~csharp
[Fact]
public void validator_rejects_a_consumer_of_an_internal_event_from_another_module()
{
    var producer = ManifestFactory.ProducerWithInternalEvent();
    var consumer = ManifestFactory.ConsumerOf(producer.Events.Single().Contract);

    var error = Assert.Throws<ManifestValidationException>(() =>
        ManifestValidator.Validate([producer, consumer]));

    Assert.Contains("internal event", error.Message, StringComparison.OrdinalIgnoreCase);
}

[Fact]
public void validator_rejects_duplicate_role_ids_owned_by_different_modules()
{
    var first = ManifestFactory.WithRole("proof.entry", "proof-a");
    var second = ManifestFactory.WithRole("proof.entry", "proof-b");

    Assert.Throws<ManifestValidationException>(() =>
        ManifestValidator.Validate([first, second]));
}
~~~

- [ ] **Step 2: Implement a data-only manifest**

ModuleManifest must contain one ModuleId, one semantic ModuleVersion, explicit dependency ranges, role descriptors, Operation descriptors, Event descriptors, Reshape descriptors, provided Capability descriptors, and required Capability descriptors. Do not scan assemblies for handlers, grain interfaces, attributes, or provider tools to construct routes.

Expose:

~~~csharp
public interface IModuleRegistry
{
    ModuleSet Resolve(IReadOnlyCollection<ModuleManifest> installed);
    ModuleManifest Get(ModuleId id);
    OperationDescriptor GetOperation(OperationId id);
    EventDescriptor GetEvent(ContractId id);
    CapabilityDescriptor GetCapability(CapabilityId id);
}
~~~

- [ ] **Step 3: Validate contracts and dependencies**

Reject duplicate IDs, missing dependency manifests, incompatible major versions, a consumer of an event that is not Published, a Reshape whose input or output event is not declared, duplicate provided capabilities, and an Operation whose entry role is absent from its owning manifest.

- [ ] **Step 4: Run manifest tests**

Run:

~~~powershell
dotnet test tests/CoreV2/Brain.Core.Tests/Brain.Core.Tests.csproj --filter "FullyQualifiedName~ManifestValidatorTests"
~~~

Expected: PASS.

- [ ] **Step 5: Commit**

~~~powershell
git add src/CoreV2/Brain.Abstractions src/CoreV2/Brain.Core tests/CoreV2/Brain.Core.Tests
git commit -m "feat(corev2): validate explicit module manifests"
~~~

### Task 6: Add Workspace Policy and Deterministic Endpoint Resolution

**Files:**
- Create: src/CoreV2/Brain.Abstractions/Policy/WorkspacePolicyContracts.cs
- Create: src/CoreV2/Brain.Core/Policy/WorkspacePolicyEvaluator.cs
- Create: src/CoreV2/Brain.Core/Endpoints/EndpointResolver.cs
- Create: tests/CoreV2/Brain.Core.Tests/EndpointResolverTests.cs
- Create: tests/CoreV2/Brain.Core.Tests/WorkspacePolicyEvaluatorTests.cs

**Interfaces:**
- Consumes: ModuleSet, WorkspaceContext, OperationDescriptor, NeuronRoleId.
- Produces: IWorkspacePolicyEvaluator, PolicyDecision, EndpointAddress, IEndpointResolver, and role scope validation.

- [ ] **Step 1: Write failing endpoint tests**

~~~csharp
[Fact]
public void principal_scoped_role_derives_distinct_endpoints_for_two_principals()
{
    var role = new NeuronRoleDescriptor(
        new NeuronRoleId("proof.entry"),
        NeuronScope.Principal,
        new ModuleId("proof"));

    var alice = _resolver.Resolve(role, ContextFactory.For("workspace/sales", "principal/alice"));
    var bob = _resolver.Resolve(role, ContextFactory.For("workspace/sales", "principal/bob"));

    Assert.NotEqual(alice, bob);
    Assert.Equal(alice.Role, bob.Role);
}

[Fact]
public void caller_cannot_supply_an_endpoint_address_to_invoke_an_operation()
{
    var invocation = InvocationFactory.WithEndpointAttempt();

    Assert.Throws<ArgumentException>(() => invocation.Validate());
}
~~~

- [ ] **Step 2: Implement policy contracts**

Policy decisions have exactly Allowed, Refused, and ConfirmationRequired outcomes. The proof implements Allowed and Refused only; ConfirmationRequired remains a contract shape for later work. Policy receives WorkspaceContext, a requested OperationId or graph action, and immutable descriptor metadata. It never receives a client endpoint, raw model prompt, provider token, or Entity state.

Use:

~~~csharp
public interface IWorkspacePolicyEvaluator
{
    PolicyDecision AuthorizeOperation(
        WorkspaceContext caller,
        OperationDescriptor operation);

    PolicyDecision AuthorizeGraphChange(
        ActivityContext context,
        GraphChangeRequest request);
}
~~~

- [ ] **Step 3: Implement deterministic endpoint derivation**

An EndpointAddress is a CoreV2-internal value composed from WorkspaceId, role ModuleId, NeuronRoleId, and either a fixed Workspace scope token or the verified PrincipalId. EndpointAddress must not be serializable by an Operation contract and must not appear on ActivityView.

- [ ] **Step 4: Run policy and endpoint tests**

Run:

~~~powershell
dotnet test tests/CoreV2/Brain.Core.Tests/Brain.Core.Tests.csproj --filter "FullyQualifiedName~EndpointResolverTests|FullyQualifiedName~WorkspacePolicyEvaluatorTests"
~~~

Expected: PASS.

- [ ] **Step 5: Commit**

~~~powershell
git add src/CoreV2/Brain.Abstractions src/CoreV2/Brain.Core tests/CoreV2/Brain.Core.Tests
git commit -m "feat(corev2): authorize operations and derive endpoints"
~~~

### Task 7: Implement BrainActivity Invocation, Idempotency, and Projection

**Files:**
- Create: src/CoreV2/Brain.Core/Activities/BrainActivityState.cs
- Create: src/CoreV2/Brain.Core/Activities/BrainActivityGrain.cs
- Create: src/CoreV2/Brain.Core/Activities/OperationGateway.cs
- Create: src/CoreV2/Brain.Core/Activities/ActivityProjectionService.cs
- Create: tests/CoreV2/Brain.Core.Tests/OperationGatewayTests.cs
- Create: tests/CoreV2/Brain.Core.Tests/ActivityProjectionTests.cs

**Interfaces:**
- Consumes: IWorkspacePolicyEvaluator, IEndpointResolver, IModuleRegistry, OperationDescriptor.
- Produces: durable BrainActivity lifecycle, IOperationGateway, OperationAccepted, ActivityView, parent/child activity linkage, and idempotency matching.

- [ ] **Step 1: Write failing invocation tests**

~~~csharp
[Fact]
public async Task same_workspace_principal_and_key_return_the_same_activity()
{
    var caller = ContextFactory.For("workspace/sales", "principal/alice");
    var key = new IdempotencyKey("request/42");

    var first = await _gateway.InvokeAsync<ProofInput, ProofResult>(
        ProofContracts.Run, new ProofInput("alpha"), caller, key, TestContext.Current.CancellationToken);
    var retry = await _gateway.InvokeAsync<ProofInput, ProofResult>(
        ProofContracts.Run, new ProofInput("alpha"), caller, key, TestContext.Current.CancellationToken);

    Assert.Equal(first.Activity, retry.Activity);
}

[Fact]
public async Task reused_key_for_another_operation_is_refused()
{
    var caller = ContextFactory.For("workspace/sales", "principal/alice");
    var key = new IdempotencyKey("request/42");

    await _gateway.InvokeAsync<ProofInput, ProofResult>(
        ProofContracts.Run, new ProofInput("alpha"), caller, key, TestContext.Current.CancellationToken);

    await Assert.ThrowsAsync<IdempotencyConflictException>(() =>
        _gateway.InvokeAsync<CorrectionInput, CorrectionResult>(
            ProofContracts.Correct, new CorrectionInput("assessment"), caller, key, TestContext.Current.CancellationToken));
}
~~~

- [ ] **Step 2: Implement the state machine**

Use ActivityStatus values Accepted, Running, AwaitingConfirmation, Completed, Refused, Failed, and Cancelled. The proof reaches Accepted, Running, Completed, and Refused. A valid authenticated invocation creates an Accepted activity before dispatch. A policy refusal creates and immediately settles a Refused activity; malformed or unregistered Operation input fails before activity creation.

Persist operation ID, caller context, caller idempotency key, correlation, optional parent Activity ID, terminal-result descriptor, current redacted progress reference, terminal-result reference, and problem reference. Do not persist raw journal or Entity state in the activity projection state.

- [ ] **Step 3: Implement direct entry dispatch**

After activity creation, OperationGateway resolves the entry role through IEndpointResolver and sends the typed Operation input to that endpoint. It must not resolve BrainGraph, discover a handler, accept an endpoint argument, or emit a DomainEvent itself.

- [ ] **Step 4: Implement parent and child activity linkage**

Add an internal StartChildAsync method that derives a child idempotency key from the parent activity plus the child operation, intersects the parent delegation with child policy, and records the parent Activity ID. Public MCP and Flutter adapters will not call this method directly.

- [ ] **Step 5: Run Activity tests**

Run:

~~~powershell
dotnet test tests/CoreV2/Brain.Core.Tests/Brain.Core.Tests.csproj --filter "FullyQualifiedName~OperationGatewayTests|FullyQualifiedName~ActivityProjectionTests"
~~~

Expected: PASS.

- [ ] **Step 6: Commit**

~~~powershell
git add src/CoreV2/Brain.Core tests/CoreV2/Brain.Core.Tests
git commit -m "feat(corev2): add durable operation activities"
~~~

### Task 8: Implement the Neuron Turn, Journal, and Outbox Contracts

**Files:**
- Create: src/CoreV2/Brain.Core/Neurons/BrainNeuron.cs
- Create: src/CoreV2/Brain.Core/Neurons/NeuronTurn.cs
- Create: src/CoreV2/Brain.Core/Outbox/OutboxEntry.cs
- Create: src/CoreV2/Brain.Core/Outbox/IOutboxStore.cs
- Create: src/CoreV2/Brain.Core/Outbox/InMemoryOutboxStore.cs
- Create: tests/CoreV2/Brain.Core.Tests/NeuronTurnTests.cs

**Interfaces:**
- Consumes: ActivityContext, EndpointAddress, DomainEvent contracts.
- Produces: BrainNeuron<TState>, NeuronTurn, staged OutboxEntry, journal entry metadata, direct Send, and graph-routed Emit.

- [ ] **Step 1: Write failing atomic-turn tests**

~~~csharp
[Fact]
public async Task emit_commits_state_journal_and_route_snapshot_before_dispatch()
{
    var neuron = _host.GetRequiredGrain<IProofSourceNeuron>(_sourceEndpoint);

    await neuron.RunAsync(_activity);

    var snapshot = await _host.Outbox.SingleAsync(_activity.Activity);
    Assert.Equal("proof/produced@1", snapshot.EventContract.Value);
    Assert.NotEmpty(snapshot.Deliveries);
    Assert.True(await _host.Journal.ContainsAsync(_sourceEndpoint, snapshot.EventId));
}

[Fact]
public async Task direct_send_does_not_query_the_brain_graph()
{
    await _host.ResetGraphResolutionCountAsync();

    await _host.SendToEntryAsync(_activity);

    Assert.Equal(0, await _host.GraphResolutionCountAsync());
}
~~~

- [ ] **Step 2: Define journal and outbox data**

Every journal entry contains event firing ID, typed event ContractId, ActivityContext, cause firing ID when present, source endpoint, and server timestamp. Every OutboxEntry contains the same firing ID plus an immutable array of delivery snapshots. A delivery snapshot contains DeliveryId, target endpoint, source SynapseKey, Synapse revision number, input contract, output contract, and optional ReshapeId.

Do not store an unresolved graph query in an outbox entry. Do not write a provider response, JSON tree, object, raw operation payload, or presentation text to these generic runtime records.

- [ ] **Step 3: Implement BrainNeuron turn discipline**

BrainNeuron<TState> must expose protected SendAsync and EmitAsync methods. SendAsync stages a directed message without graph resolution. EmitAsync invokes the authoritative graph resolver, receives all live delivery snapshots, appends journal/state/outbox changes to one turn, and returns an EmissionOutcome with DeliveryCount. It must not await the delivery dispatcher.

- [ ] **Step 4: Run turn tests**

Run:

~~~powershell
dotnet test tests/CoreV2/Brain.Core.Tests/Brain.Core.Tests.csproj --filter "FullyQualifiedName~NeuronTurnTests"
~~~

Expected: PASS.

- [ ] **Step 5: Commit**

~~~powershell
git add src/CoreV2/Brain.Core tests/CoreV2/Brain.Core.Tests
git commit -m "feat(corev2): add neuron journal and route-snapshot outbox"
~~~

### Task 9: Implement Sharded BrainGraph and Immutable Synapse Revisions

**Files:**
- Create: src/CoreV2/Brain.Abstractions/Graph/SynapseContracts.cs
- Create: src/CoreV2/Brain.Core/Graph/BrainGraphShardGrain.cs
- Create: src/CoreV2/Brain.Core/Graph/BrainGraphShardState.cs
- Create: src/CoreV2/Brain.Core/Graph/GraphShardResolver.cs
- Create: src/CoreV2/Brain.Core/Graph/SynapseRevisionValidator.cs
- Create: tests/CoreV2/Brain.Core.Tests/BrainGraphShardTests.cs

**Interfaces:**
- Consumes: EndpointAddress, EventDescriptor, ReshapeDescriptor, Workspace policy.
- Produces: SynapseDefinition, SynapseRevision, SynapseKey, GraphChangeRequest, GraphResolution, and deterministic shard mapping.

- [ ] **Step 1: Write failing graph tests**

~~~csharp
[Fact]
public async Task replace_preserves_key_source_contract_scope_and_slot()
{
    var installed = await _graph.InstallAsync(_request);
    var replacement = _request with
    {
        Target = _assessmentTarget,
        Reshape = ProofContracts.ToAssessmentReshape,
    };

    var revised = await _graph.ReplaceAsync(installed.Key, replacement);

    Assert.Equal(installed.Key, revised.Key);
    Assert.Equal(installed.Source, revised.Source);
    Assert.Equal(installed.Contract, revised.Contract);
    Assert.Equal(installed.WiringSlot, revised.WiringSlot);
    Assert.NotEqual(installed.Target, revised.Target);
    Assert.Equal(2, revised.Revision);
}

[Fact]
public async Task retired_synapse_does_not_resolve_but_its_history_remains()
{
    var installed = await _graph.InstallAsync(_request);
    await _graph.RetireAsync(installed.Key, GraphReason.ManualRetire);

    var resolution = await _graph.ResolveAsync(_request.Source, _request.Contract);

    Assert.Empty(resolution.Deliveries);
    Assert.Equal(2, (await _graph.HistoryAsync(installed.Key)).Count);
}
~~~

- [ ] **Step 2: Define graph contracts**

SynapseDefinition contains only opaque SynapseKey, source EndpointAddress, input ContractId, target EndpointAddress, optional ReshapeId, scope, WiringSlotId, provenance ActivityContext, and revision number. It has no expiry, weight, client-readable name, token, Entity value, or mutable provider field.

GraphChangeRequest has Install, Replace, and Retire forms. Replace validation rejects source, input contract, scope, or wiring-slot changes. Retire creates a non-live revision with reason and provenance. Reinstalling the same stable route creates a new live revision under the existing SynapseKey.

- [ ] **Step 3: Implement deterministic sharding**

GraphShardResolver maps every outbound source EndpointAddress to one BrainGraphShard grain key. A SynapseKey is allocated by that shard and never moves. Resolve reads the authoritative shard; any future cache must include a graph revision fence and is out of scope for this proof.

- [ ] **Step 4: Enforce installed contract and reshape validity**

Install and Replace require an event descriptor produced by the source module, a target that accepts the input event or registered reshape output, a Published event for cross-module delivery, a pure registered reshape when input and target types differ, and an Allowed graph-policy decision. BrainGraph rejects all invalid requests before writing any revision.

- [ ] **Step 5: Run graph tests**

Run:

~~~powershell
dotnet test tests/CoreV2/Brain.Core.Tests/Brain.Core.Tests.csproj --filter "FullyQualifiedName~BrainGraphShardTests"
~~~

Expected: PASS.

- [ ] **Step 6: Commit**

~~~powershell
git add src/CoreV2/Brain.Abstractions src/CoreV2/Brain.Core tests/CoreV2/Brain.Core.Tests
git commit -m "feat(corev2): add sharded immutable synapse graph"
~~~

### Task 10: Dispatch Snapshot Deliveries, Apply Reshapes, and Deduplicate Receivers

**Files:**
- Create: src/CoreV2/Brain.Core/Delivery/DeliveryDispatcher.cs
- Create: src/CoreV2/Brain.Core/Delivery/DeliveryDeduplicator.cs
- Create: src/CoreV2/Brain.Core/Reshapes/ReshapeRegistry.cs
- Create: src/CoreV2/Brain.Abstractions/Reshapes/ReshapeContracts.cs
- Create: tests/CoreV2/Brain.Core.Tests/DeliveryDispatcherTests.cs
- Create: tests/CoreV2/Brain.Core.Tests/ReshapeRegistryTests.cs

**Interfaces:**
- Consumes: immutable OutboxEntry delivery snapshots and registered ReshapeDescriptor values.
- Produces: IReshape<TFrom, TTo>, IReshapeRegistry, DeliveryDispatcher, receiver DeliveryId deduplication, and zero-route EmissionOutcome.

- [ ] **Step 1: Write failing delivery tests**

~~~csharp
[Fact]
public async Task duplicate_dispatch_of_the_same_delivery_commits_receiver_effect_once()
{
    var entry = await _host.Outbox.SingleAsync(_activity.Activity);

    await _dispatcher.DispatchAsync(entry);
    await _dispatcher.DispatchAsync(entry);

    Assert.Equal(1, await _host.ReceiverCommitCountAsync(_summaryTarget));
}

[Fact]
public async Task rewire_after_emit_does_not_reroute_the_already_staged_delivery()
{
    var entry = await _host.Outbox.SingleAsync(_activity.Activity);
    await _graph.ReplaceAsync(_synapseKey, _assessmentReplacement);

    await _dispatcher.DispatchAsync(entry);

    Assert.Equal(1, await _host.ReceiverCommitCountAsync(_summaryTarget));
    Assert.Equal(0, await _host.ReceiverCommitCountAsync(_assessmentTarget));
}

[Fact]
public async Task zero_route_is_visible_to_the_emitting_neuron_without_a_synthetic_refusal()
{
    var outcome = await _host.EmitWithoutRouteAsync(_activity);

    Assert.Equal(0, outcome.DeliveryCount);
    Assert.False(outcome.CreatedRefusal);
}
~~~

- [ ] **Step 2: Implement pure reshape contracts**

Use:

~~~csharp
public interface IReshape<TFrom, TTo>
    where TFrom : IDomainEvent
    where TTo : IDomainEvent
{
    TTo Transform(TFrom source);
}
~~~

ReshapeRegistry validates that a manifest-owned reshape declares a Published input/output pair when crossing modules. It invokes only Transform. A reshape may not accept ActivityContext, IServiceProvider, an Entity reader, a Capability broker, a cancellation callback, or a network client.

- [ ] **Step 3: Implement dispatcher and receiver dedup**

DeliveryDispatcher executes the exact target and Reshape captured in OutboxEntry. The receiver records DeliveryId in durable state before applying the event. A duplicate DeliveryId returns success without applying state or emitting again. Distinct Synapse revisions produce distinct DeliveryId values even when source event payloads compare equal.

- [ ] **Step 4: Run delivery tests**

Run:

~~~powershell
dotnet test tests/CoreV2/Brain.Core.Tests/Brain.Core.Tests.csproj --filter "FullyQualifiedName~DeliveryDispatcherTests|FullyQualifiedName~ReshapeRegistryTests"
~~~

Expected: PASS.

- [ ] **Step 5: Commit**

~~~powershell
git add src/CoreV2/Brain.Abstractions src/CoreV2/Brain.Core tests/CoreV2/Brain.Core.Tests
git commit -m "feat(corev2): dispatch deterministic deduplicated deliveries"
~~~

### Task 11: Add Immutable Wiring, Staging, and Atomic Activation

**Files:**
- Create: src/CoreV2/Brain.Abstractions/Wiring/WiringContracts.cs
- Create: src/CoreV2/Brain.Core/Wiring/WiringGrain.cs
- Create: src/CoreV2/Brain.Core/Wiring/WiringActivationGrain.cs
- Create: src/CoreV2/Brain.Core/Wiring/WiringApplicabilityEvaluator.cs
- Create: tests/CoreV2/Brain.Core.Tests/WiringActivationTests.cs
- Create: tests/CoreV2/Brain.Core.Tests/WiringApplicabilityTests.cs

**Interfaces:**
- Consumes: OperationDescriptor, ModuleSet, role IDs, Capability descriptors, graph changes, Workspace policy.
- Produces: WiringProposal, WiringVersion, WiringApplicability, WiringActivation, staged graph changes, and semantic readiness states.

- [ ] **Step 1: Write failing Wiring tests**

~~~csharp
[Fact]
public async Task incomplete_multi_shard_activation_is_not_visible_to_graph_resolution()
{
    var activation = await _wiring.StartApplyAsync(_wiringVersion, _bobContext);
    await _wiring.StageOneShardAsync(activation);

    var resolution = await _host.ResolveForBobAsync(ProofContracts.Produced);

    Assert.Empty(resolution.Deliveries);
    Assert.Equal(WiringActivationStatus.Staging, await _wiring.StatusAsync(activation));
}

[Fact]
public async Task completed_activation_exposes_all_staged_routes_together()
{
    var activation = await _wiring.ApplyAsync(_wiringVersion, _bobContext);

    Assert.Equal(WiringActivationStatus.Active, await _wiring.StatusAsync(activation));
    Assert.Equal(2, (await _host.ResolveAllForBobAsync()).Count);
}

[Fact]
public void applicability_uses_only_declarative_framework_facts()
{
    var applicability = _evaluator.Evaluate(_wiringVersion, _bobContext, _moduleSet);

    Assert.Equal(WiringReadiness.Ready, applicability.Readiness);
    Assert.DoesNotContain("prompt", applicability.Explanation, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("entity", applicability.Explanation, StringComparison.OrdinalIgnoreCase);
}
~~~

- [ ] **Step 2: Define Wiring data**

A WiringVersion contains WiringId, append-only version number, optional parent version, cause Activity ID, one OperationId and major version, role slots, public event contracts, registered reshape IDs, required capabilities, and declarative policy prerequisites. It never contains endpoint IDs, Entity data, transcript text, prompt text, token material, usage counts, payload predicates, or executable code.

WiringReadiness has exactly Ready, NeedsSetup, NeedsAuthorization, and Unavailable values. Find-capabilities later reports these semantic states, not live Synapse keys or graph targets.

- [ ] **Step 3: Implement proposal, publish, and apply**

The proof creates a proposal from an Activity but publishes it through Workspace policy. Publish is immutable and append-only. Apply resolves verified endpoints from role scope, asks every required shard to stage inactive revisions, and writes one activation record only after all stage acknowledgements succeed. Graph resolution ignores staged revisions until activation is Active.

- [ ] **Step 4: Implement failures and retry**

Store each stage acknowledgement by activation ID. Retrying ApplyAsync resumes only missing stages. A failed stage leaves the activation Staging or Failed; it never exposes a partial route. Retire or replace of a live wiring-created Synapse continues to produce graph history independently of the immutable WiringVersion.

- [ ] **Step 5: Run Wiring tests**

Run:

~~~powershell
dotnet test tests/CoreV2/Brain.Core.Tests/Brain.Core.Tests.csproj --filter "FullyQualifiedName~WiringActivationTests|FullyQualifiedName~WiringApplicabilityTests"
~~~

Expected: PASS.

- [ ] **Step 6: Commit**

~~~powershell
git add src/CoreV2/Brain.Abstractions src/CoreV2/Brain.Core tests/CoreV2/Brain.Core.Tests
git commit -m "feat(corev2): stage and activate immutable wirings"
~~~

### Task 12: Implement Capability Resolution with Activity Context and Delegation

**Files:**
- Create: src/CoreV2/Brain.Core/Capabilities/CapabilityBroker.cs
- Create: src/CoreV2/Brain.Core/Capabilities/CapabilityBindingResolver.cs
- Create: src/CoreV2/Brain.Core/Capabilities/CapabilityUseState.cs
- Create: src/CoreV2/Brain.Testing/Fakes/DeterministicCapability.cs
- Create: tests/CoreV2/Brain.Core.Tests/CapabilityBrokerTests.cs

**Interfaces:**
- Consumes: ICapabilityBroker, CapabilityDescriptor, ModuleSet, ActivityContext, Delegation.
- Produces: deterministic capability binding, per-activity use key, context validation, and typed fake capability response.

- [ ] **Step 1: Write failing capability tests**

~~~csharp
[Fact]
public async Task capability_use_requires_an_activity_context()
{
    await Assert.ThrowsAsync<MissingActivityContextException>(() =>
        _broker.UseAsync<ProofCapabilityInput, ProofCapabilityResult>(
            ProofContracts.Classifier,
            new ProofCapabilityInput("alpha"),
            ActivityContextFactory.Missing(),
            TestContext.Current.CancellationToken));
}

[Fact]
public async Task capability_use_is_refused_when_delegation_omits_the_capability()
{
    var context = _activity with
    {
        Delegation = Delegation.None,
    };

    await Assert.ThrowsAsync<CapabilityNotDelegatedException>(() =>
        _broker.UseAsync<ProofCapabilityInput, ProofCapabilityResult>(
            ProofContracts.Classifier,
            new ProofCapabilityInput("alpha"),
            context,
            TestContext.Current.CancellationToken));
}

[Fact]
public async Task retry_of_the_same_capability_use_returns_the_recorded_result()
{
    var first = await _host.ClassifyAsync(_activity, "alpha");
    var retry = await _host.ClassifyAsync(_activity, "alpha");

    Assert.Equal(first, retry);
    Assert.Equal(1, _host.FakeClassifierCallCount);
}
~~~

- [ ] **Step 2: Implement the capability boundary**

CapabilityBroker verifies an active ActivityContext, the caller’s Workspace module set, an Allowed policy result, and Delegation membership before resolving a provider binding. Its durable use key is derived from Activity ID, CapabilityId, and a caller-provided stable use name. It records completion before returning a retry result.

The proof binds exactly one deterministic classifier capability. It accepts a sealed ProofCapabilityInput and returns a sealed ProofCapabilityResult. Do not introduce IAgent, ChatMessage, ChatResponse, streaming, provider SDK references, service-provider lookup, external network clients, or model-specific configuration.

- [ ] **Step 3: Run capability tests**

Run:

~~~powershell
dotnet test tests/CoreV2/Brain.Core.Tests/Brain.Core.Tests.csproj --filter "FullyQualifiedName~CapabilityBrokerTests"
~~~

Expected: PASS.

- [ ] **Step 4: Commit**

~~~powershell
git add src/CoreV2/Brain.Core src/CoreV2/Brain.Testing tests/CoreV2/Brain.Core.Tests
git commit -m "feat(corev2): resolve capabilities through activity context"
~~~

### Task 13: Build the Deterministic Test Host and Proof Module

**Files:**
- Create: src/CoreV2/Brain.Testing/BrainTestHost.cs
- Create: src/CoreV2/Brain.Testing/Fixtures/WorkspaceFixture.cs
- Create: src/CoreV2/Brain.Testing/Fixtures/DeterministicTimeProvider.cs
- Create: src/CoreV2/Modules/Proof.Contracts/ProofContracts.cs
- Create: src/CoreV2/Modules/Proof.Contracts/ProofOperationContracts.cs
- Create: src/CoreV2/Modules/Proof.Contracts/ProofEventContracts.cs
- Create: src/CoreV2/Modules/Proof/ProofManifest.cs
- Create: src/CoreV2/Modules/Proof/ProofEntryNeuron.cs
- Create: src/CoreV2/Modules/Proof/ProofSourceNeuron.cs
- Create: src/CoreV2/Modules/Proof/ProofReceiverNeuron.cs
- Create: src/CoreV2/Modules/Proof/ProofToAssessmentReshape.cs
- Create: tests/CoreV2/Brain.Proof.Tests/ProofOperationAcceptanceTests.cs

**Interfaces:**
- Consumes: Tasks 3 through 12.
- Produces: BrainTestHost, deterministic caller and policy fixtures, Proof.Run@1, Proof.Correct@1, ProofInput, ProofResult, ProofProduced, ProofAssessed, proof role IDs, and a complete non-provider vertical slice.

- [ ] **Step 1: Write the end-to-end failing test**

~~~csharp
[Fact]
public async Task caller_invokes_public_operation_and_observes_only_the_terminal_result()
{
    await using var host = await BrainTestHost.StartAsync();
    var caller = host.Caller("workspace/proof", "principal/alice");

    var accepted = await host.Operations.InvokeAsync<ProofInput, ProofResult>(
        ProofContracts.Run,
        new ProofInput("alpha"),
        caller,
        new IdempotencyKey("proof/1"),
        TestContext.Current.CancellationToken);

    var view = await host.Operations.ObserveAsync(
        accepted.Activity,
        caller,
        TestContext.Current.CancellationToken);
    var result = await host.ReadResultAsync<ProofResult>(view, caller);

    Assert.Equal(ActivityStatus.Completed, view.Status);
    Assert.Equal("summary", result.Route);
    Assert.DoesNotContain(
        typeof(ActivityView).GetProperties(),
        property => property.Name.Contains("Journal", StringComparison.Ordinal));
}
~~~

- [ ] **Step 2: Define proof contracts and manifest**

Proof.Contracts owns:

~~~csharp
public sealed record ProofInput(string Value);
public sealed record ProofResult(string Route);
public sealed record ProofProgress(string Phase);
public sealed record CorrectionInput(string RequestedRoute);
public sealed record CorrectionResult(string AppliedRoute);
public sealed record ProofProduced(string Value) : IDomainEvent;
public sealed record ProofAssessed(string Value, string Assessment) : IDomainEvent;
public sealed record ProofCapabilityInput(string Value);
public sealed record ProofCapabilityResult(string Route);
~~~

ProofManifest declares principal-scoped proof.entry and proof.source roles, workspace-scoped proof.summary and proof.assessment roles, Proof.Run@1 and Proof.Correct@1 Operations, published ProofProduced and ProofAssessed events, one pure ProofProduced-to-ProofAssessed Reshape, and the deterministic classifier Capability requirement. No proof event is an Operation input or output merely because it is convenient.

- [ ] **Step 3: Implement behavior with Neuron ownership**

ProofEntryNeuron accepts ProofInput through direct Send, optionally uses the classifier Capability through the supplied ActivityContext, and sends work to ProofSourceNeuron. ProofSourceNeuron emits ProofProduced. ProofReceiverNeuron records either summary or assessment state and writes the terminal ProofResult through the Activity service. The correction Operation is owned by a dedicated proof entry path that records Rewire evidence and requests an authorized graph replacement; it never accepts a SynapseKey from its input.

- [ ] **Step 4: Implement test-host controls**

BrainTestHost starts one Orleans TestCluster in-process CoreV2 composition with only the proof module and deterministic capability. It exposes Operations, graph inspection methods used only by tests, deterministic time, and helpers for building verified caller contexts. Its public helper surface must not include raw event injection or endpoint selection.

- [ ] **Step 5: Run the proof acceptance test**

Run:

~~~powershell
dotnet test tests/CoreV2/Brain.Proof.Tests/Brain.Proof.Tests.csproj --filter "FullyQualifiedName~ProofOperationAcceptanceTests"
~~~

Expected: PASS.

- [ ] **Step 6: Commit**

~~~powershell
git add src/CoreV2 tests/CoreV2
git commit -m "feat(corev2): add deterministic proof module"
~~~

### Task 14: Add Framework Acceptance Tests for Every Approved Invariant

**Files:**
- Create: tests/CoreV2/Brain.Proof.Tests/PrivacyBoundaryAcceptanceTests.cs
- Create: tests/CoreV2/Brain.Proof.Tests/RewireAndRetireAcceptanceTests.cs
- Create: tests/CoreV2/Brain.Proof.Tests/WiringAcceptanceTests.cs
- Create: tests/CoreV2/Brain.Proof.Tests/CapabilityAndChildActivityAcceptanceTests.cs
- Create: tests/CoreV2/Brain.Proof.Tests/RetryAndPolicyAcceptanceTests.cs

**Interfaces:**
- Consumes: complete proof host and contracts.
- Produces: executable acceptance specification for CoreV2 proof exit.

- [ ] **Step 1: Add public-boundary tests**

Verify each of these assertions in PrivacyBoundaryAcceptanceTests:

~~~text
An ActivityView never contains EndpointAddress, SynapseKey, graph history, Entity state, raw journal, event payload, capability request, or capability result.
An attempt to invoke an unregistered Operation is rejected before a BrainActivity is created.
An attempt to call a private event from a consumer module fails manifest validation.
An Activity observed by another Principal is refused unless Workspace policy explicitly permits it.
~~~

- [ ] **Step 2: Add graph-history tests**

Verify each of these assertions in RewireAndRetireAcceptanceTests:

~~~text
The initial proof route completes through summary.
Correction creates a new Synapse revision with the same opaque key and a different target or Reshape.
An outbox item staged before correction completes through the old captured route.
A later emission completes through assessment.
Retire prevents a later emission from resolving.
Retired and replaced revisions remain inspectable only through test-only graph inspection.
~~~

- [ ] **Step 3: Add Wiring tests**

Verify each of these assertions in WiringAcceptanceTests:

~~~text
A Wiring proposal has one Operation major version and only declarative role, event, reshape, capability, and policy data.
Applying the Wiring for Bob resolves Bob endpoints, not Alice endpoints.
No Activity, Entity, raw payload, endpoint, or secret-like data is copied from Alice to Bob.
A multi-shard staged Wiring is invisible until active.
~~~

- [ ] **Step 4: Add Capability and composition tests**

Verify each of these assertions in CapabilityAndChildActivityAcceptanceTests:

~~~text
A capability call without ActivityContext is refused.
A capability call outside delegated authority is refused.
A retry returns the durable typed result without invoking the fake capability twice.
A child Activity preserves Workspace and Principal, records its parent, and has no authority beyond the parent delegation intersected with child policy.
~~~

- [ ] **Step 5: Add retry and policy tests**

Verify each of these assertions in RetryAndPolicyAcceptanceTests:

~~~text
The same caller idempotency key returns the same Activity.
The same caller idempotency key used for another Operation is refused.
A policy refusal is a settled ActivityView with no graph delivery.
Many unrelated idempotency keys can run concurrently inside one Workspace fixture.
~~~

- [ ] **Step 6: Run all CoreV2 tests**

Run:

~~~powershell
dotnet test tests/CoreV2/Brain.Abstractions.Tests/Brain.Abstractions.Tests.csproj --no-restore
dotnet test tests/CoreV2/Brain.Core.Tests/Brain.Core.Tests.csproj --no-restore
dotnet test tests/CoreV2/Brain.Proof.Tests/Brain.Proof.Tests.csproj --no-restore
~~~

Expected: all PASS with no legacy V1 test required for the CoreV2 proof.

- [ ] **Step 7: Commit**

~~~powershell
git add tests/CoreV2
git commit -m "test(corev2): prove framework invariants"
~~~

### Task 15: Enforce Build, Test, and Documentation Handoff

**Files:**
- Modify: .github/workflows/ci.yml
- Modify: README.md
- Modify: plans/COREV2.md

**Interfaces:**
- Consumes: tested CoreV2 solution and direction documents.
- Produces: repository-visible proof boundary, CI verification, and a clear non-migration statement.

- [ ] **Step 1: Add CoreV2 test execution to CI**

After the current source-build step, add a test step:

~~~yaml
      - name: test CoreV2 proof
        run: |
          dotnet test tests/CoreV2/Brain.Abstractions.Tests/Brain.Abstractions.Tests.csproj -c Release --no-build --nologo
          dotnet test tests/CoreV2/Brain.Core.Tests/Brain.Core.Tests.csproj -c Release --no-build --nologo
          dotnet test tests/CoreV2/Brain.Proof.Tests/Brain.Proof.Tests.csproj -c Release --no-build --nologo
        timeout-minutes: 12
~~~

- [ ] **Step 2: Document the parallel bounded context**

Add this README section immediately after the current kernel/modules description:

~~~text
## CoreV2 proof

CoreV2 is an isolated, non-migrating framework proof under src/CoreV2. It does not reference the running DigitalBrain V1 kernel or modules. Its public contract is discover, invoke, observe; graph topology, DomainEvents, endpoint identities, journals, and provider data are internal. See plans/COREV2.md and plans/COREV2-IMPLEMENTATION.md before adding an adapter or module.
~~~

- [ ] **Step 3: Run final verification**

Run:

~~~powershell
dotnet build DigitalBrain.slnx -c Release -warnaserror --nologo
dotnet test tests/CoreV2/Brain.Abstractions.Tests/Brain.Abstractions.Tests.csproj --no-build
dotnet test tests/CoreV2/Brain.Core.Tests/Brain.Core.Tests.csproj --no-build
dotnet test tests/CoreV2/Brain.Proof.Tests/Brain.Proof.Tests.csproj --no-build
git diff --check
~~~

Expected: all commands exit 0 and git diff --check produces no output.

- [ ] **Step 4: Commit**

~~~powershell
git add .github/workflows/ci.yml README.md plans/COREV2.md
git commit -m "ci: verify CoreV2 proof"
~~~

## Explicit Deferrals After the Proof

Do not add these items while implementing this plan. Each requires a separate specification and implementation plan:

1. The production MCP adapter that maps authenticated protocol requests to discover, invoke, and observe.
2. Flutter integration over the same Operation and Activity contracts.
3. Conversation as an optional unstructured-text Operation and child-Activity orchestration.
4. AI module migration from V1 IAgent and Microsoft.Extensions.AI provider contracts to a typed CoreV2 Capability.
5. Adapter modules for Salesforce, Google, or any external MCP server.
6. Provider authorization broker, consent records, opaque secret handles, and external scope grants.
7. Confirmation and cancellation execution states beyond their CoreV2 contract shapes.
8. Workspace module enablement, quiesce, disable, migration, and upgrade orchestration.
9. Payload classification, retention execution, cryptographic erasure, and audit-envelope storage.
10. Resource quotas, billing, price calculation, reservations, and payment integration.
11. Cross-Workspace templates, a module registry/marketplace, sandboxed modules, and customer-uploaded code.
12. Graph caching, production shard-placement policy, telemetry, disaster recovery, and storage-provider selection.

## Self-Review

### Specification coverage

| Approved decision | Implementing tasks |
| --- | --- |
| Isolated new CoreV2 bounded context | 1, 2, 15 |
| Operation versus DomainEvent versus Capability | 1, 4, 7, 12 |
| Explicit trusted module manifests and contracts-only dependencies | 2, 5, 13 |
| Product boundary is discover, invoke, observe | 1, 4, 7, 13, 15 |
| Policy-derived caller context and endpoints | 3, 6, 7 |
| Durable Activity, idempotency, child composition, redacted view | 4, 7, 14 |
| Neuron journal, route snapshot, direct send versus emit | 8, 10 |
| Sharded graph, immutable install/replace/retire, no expiry | 9, 10, 14 |
| Pure registered Reshape | 5, 9, 10 |
| Immutable declarative Wiring with staged activation | 11, 14 |
| Capability context and delegated authority | 4, 12, 14 |
| Test host instead of public event injection | 13, 14 |
| No provider, UI, MCP transport, or billing in the proof | Global Constraints, Scope and Delivery Shape, Explicit Deferrals |

### Placeholder scan

The plan defines every project, file group, fixed type name, required test, command, and deferred boundary. It contains no deferred implementation markers inside a task; the explicit deferral list is outside the proof scope.

### Type consistency

- WorkspaceContext and ActivityContext originate in Task 3 and are consumed by Tasks 4, 6, 7, 8, and 12.
- OperationDescriptor, EventDescriptor, CapabilityDescriptor, and IOperationGateway originate in Task 4 and are consumed by Tasks 5 through 15.
- ModuleManifest and ModuleSet originate in Task 5 and are consumed by policy, wiring, capability, and proof tasks.
- EndpointAddress originates in Task 6 and is internal to Tasks 7 through 10.
- BrainActivity lifecycle originates in Task 7; Neuron and Capability work always receive its ActivityContext.
- SynapseDefinition and immutable delivery snapshots originate in Tasks 8 and 9; Task 10 dispatches only those snapshots.
- WiringVersion originates in Task 11; Task 14 tests it through BrainTestHost.

