# V2 Scope and Directed Dispatch Implementation Plan

> **For agentic workers:** use superpowers:subagent-driven-development where a fresh task can be dispatched; otherwise preserve the same test-first and review gates inline. Do not begin a production edit until its described failing test has been observed.

**Goal:** Make every durable host identity workspace-scoped and make an emitted synapse capable of durable, same-workspace directed delivery while preserving current broadcast behavior.

**Architecture:** Public modules continue to see only relative NeuronId values. Hosting maps an internal ScopeKey plus NeuronId to every Orleans host and wakeup key. Access issues scope-bound workspace channels; a publisher binds both a workspace and source before PublishAsync receives a synapse, and a reader binds the same workspace before ReadAsync receives a relative NeuronId. Core records only an output’s broadcast/direct intent. Hosting validates and snapshots the receiver list before the existing journal/outbox records the source output.

**Tech stack:** .NET 11, C#, Orleans 10, Orleans Journaling, System.Text.Json, xUnit v3.

## Guardrails

- Scope is never added to NeuronId, Synapse, SynapseOrigin, SynapseReference, JournalRecord, DeliveryEnvelope, product state, or serialized product JSON.
- There must be no public API that accepts a workspace string on PublishAsync, ReadAsync, or an ActionRef. A trusted edge obtains an opaque workspace channel once; policy/roles for issuing it are later work.
- Do not retain the global source-plus-synapse publisher or global journal reader alongside the new scope-bound Access shape.
- A scoped key is a migration boundary. Existing unscoped journals must not be silently claimed by a workspace; this first V2 work targets a clean V2 namespace.
- Keep the current atomic turn record and post-record outbox delivery intact. A direct output is not an RPC, request/reply primitive, subscription, scheduler, workflow engine, or dynamic catalog.
- Preserve targetless Emit(synapse) as broadcast. Emit(synapse, Dispatch.Direct(target)) must either snapshot that exact valid receiver or reject before a source output is recorded.
- Core, Access, and product modules remain Orleans-free. Modules cannot use scope, grain factories, raw journal keys, or physical receiver addresses.
- Tests assert durable visible facts/journals and isolation; they do not assert private method calls.

## Task 1 — Make durable hosts physically workspace-scoped

**Files:**

- Create: src/DigitalBrain.Hosting/Access/ScopeKey.cs
- Create: src/DigitalBrain.Hosting/Runtime/ScopedNeuronAddress.cs
- Create: src/DigitalBrain.Hosting/Runtime/ScopedNeuronAddressCodec.cs
- Modify: src/DigitalBrain.Hosting/Runtime/NeuronHost.cs
- Modify: src/DigitalBrain.Hosting/Runtime/Outbox.cs
- Modify: src/DigitalBrain.Hosting/Runtime/OutboxWakeup.cs
- Modify: src/DigitalBrain.Hosting/Runtime/NeuronKey.cs only if it still owns a logical-key concern
- Modify: src/DigitalBrain.Testing/Fixture/ComposedFixture.cs
- Modify: src/DigitalBrain.Testing/Journal/RecordingJournalStorageProvider.cs
- Test: src/DigitalBrain.Core.Tests/Mechanics/RouterTests.cs
- Test: a new scoped-host integration test in src/DigitalBrain.Core.Tests/Mechanics

### Step 1: Write the failing tests

Add an internal mechanical test proving that the same relative NeuronId produces different physical host addresses for two ScopeKey values, and round-trips delimiter-bearing scope and relative identity without ambiguity. Add a composed-host proof with two workspace handles using the same source and receiver names:

1. each source journal starts at position 1;
2. publishing in workspace A does not produce or deliver a record in workspace B;
3. an A reader cannot see B’s journal by supplying B’s same relative NeuronId;
4. module-visible Id and journal fact JSON contain no scope.

### Step 2: Run the focused tests and observe RED

Run:

    dotnet test src/DigitalBrain.Core.Tests/DigitalBrain.Core.Tests.csproj --no-restore --filter-class DigitalBrain.ScopedHostAddressTests

Expected: compile/test failure because all present host addresses are derived from NeuronId alone and Access has no workspace handle.

### Step 3: Implement the smallest physical scope seam

Create a Hosting-only ScopeKey and a ScopedNeuronAddress that pairs it with a relative NeuronId. Its codec owns native string-key encoding; keep NeuronKey only for journal-local logical watermark keys if that separation reduces ambiguity. Change NeuronHost.AddressOf and activation decode to consume the scoped address while exposing only relative Id to the behavior binding. Carry the owner scope through Outbox receiver addresses and OutboxWakeup reminder keys. Update test fault, force-deactivation, and drain utilities to use the same physical scope.

Do not put scope in the delivery envelope: a receiver’s physical address selects the workspace, and provenance stays relative within that workspace.

### Step 4: Run the focused tests and build GREEN

Run:

    dotnet test src/DigitalBrain.Core.Tests/DigitalBrain.Core.Tests.csproj --no-restore --filter-class DigitalBrain.ScopedHostAddressTests
    dotnet build DigitalBrain.slnx --no-restore

Expected: identical relative identities are isolated by physical address and all existing unscoped test compositions are updated to an explicit trusted test scope.

## Task 2 — Replace ambient Access with scope-bound workspace channels

**Files:**

- Modify: src/DigitalBrain.Access/SynapsePublisher.cs
- Modify: src/DigitalBrain.Access/JournalReader.cs
- Create: src/DigitalBrain.Access/WorkspaceChannel.cs
- Create: an Access-owned opaque channel/issuer contract if required
- Modify: src/DigitalBrain.Hosting/Access/OrleansSynapsePublisher.cs or replace with a channel implementation
- Modify: src/DigitalBrain.Hosting/Access/OrleansJournalReader.cs or replace with a channel implementation
- Modify: src/DigitalBrain.Hosting/DigitalBrainSiloExtensions.cs
- Modify: src/DigitalBrain.Testing/Fixture/DigitalBrainTest.cs
- Modify: src/DigitalBrain.Testing/Fixture/ComposedFixture.cs
- Test: a new Access scope-bound contract/mechanics test

### Step 1: Write the failing tests

Write a test that opens two opaque workspace channels through a trusted test issuer. A channel creates a source-bound publisher whose PublishAsync accepts only a Synapse. The channel’s reader accepts only a relative NeuronId. Assert reflection/API shape has no DI-resolvable raw SynapsePublisher or raw JournalReader and no PublishAsync or ReadAsync overload that accepts a workspace identifier.

### Step 2: Run the focused tests and observe RED

Run:

    dotnet test src/DigitalBrain.Core.Tests/DigitalBrain.Core.Tests.csproj --no-restore --filter-class DigitalBrain.WorkspaceAccessTests

Expected: failure because the current process-global publisher accepts any source and the reader is unscoped.

### Step 3: Implement the capability seam

Define WorkspaceChannel as an opaque Access capability. It binds a trusted internal ScopeKey. It can issue a source-bound publisher and its own journal reader. Keep issuance behind a Hosting/Access trusted seam; do not solve roles/permissions in this task and do not expose scope strings to client code. The Orleans implementation maps every source and reader request through the captured scope and the scoped host address. Update the test harness to obtain channels through a testing-only trusted issuer.

### Step 4: Run focused tests and build GREEN

Run:

    dotnet test src/DigitalBrain.Core.Tests/DigitalBrain.Core.Tests.csproj --no-restore --filter-class DigitalBrain.WorkspaceAccessTests
    dotnet test DigitalBrain.slnx --no-restore

Expected: ingress and reads are scope-bound capability operations, with no ambient global route remaining.

## Task 3 — Add Core output intent without Hosting behavior change yet

**Files:**

- Create: src/DigitalBrain.Core/Dispatch.cs
- Modify: src/DigitalBrain.Core/Neuron.cs
- Modify: src/DigitalBrain.Core/Internal/ITurnBinding.cs
- Modify: src/DigitalBrain.Hosting/Runtime/TurnBinding.cs
- Test: src/DigitalBrain.Core.Tests/Mechanics/BehaviorFacadeTests.cs

### Step 1: Write the failing test

Add a bound-behavior test that emits one broadcast synapse and one Dispatch.Direct(receiver) synapse. It must observe two staged values carrying their distinct intent and reject an invalid direct target at the public Dispatch construction boundary. Existing one-argument Emit must stage broadcast.

### Step 2: Run focused tests and observe RED

Run:

    dotnet test src/DigitalBrain.Core.Tests/DigitalBrain.Core.Tests.csproj --no-restore --filter-class DigitalBrain.BehaviorFacadeTests

Expected: failure because staging currently stores only Synapse values.

### Step 3: Implement the minimal pure vocabulary

Add a value-type Dispatch with Broadcast and Direct(NeuronId). Make the internal turn binding stage a private output pair of synapse plus dispatch intent. Preserve the targetless behavior as the default and do not add Ask, correlation, callbacks, cancellation, timeouts, or a runtime router to Core.

### Step 4: Run the focused test and build GREEN

Run:

    dotnet test src/DigitalBrain.Core.Tests/DigitalBrain.Core.Tests.csproj --no-restore --filter-class DigitalBrain.BehaviorFacadeTests
    dotnet build src/DigitalBrain.Core/DigitalBrain.Core.csproj --no-restore

Expected: Core exposes an honest declarative delivery intent while remaining free of scope and Orleans.

## Task 4 — Snapshot and deliver explicit targets within the sender scope

**Files:**

- Modify: src/DigitalBrain.Hosting/Runtime/Router.cs
- Modify: src/DigitalBrain.Hosting/Runtime/ProducedSynapseStager.cs
- Modify: src/DigitalBrain.Hosting/Runtime/NeuronHost.cs
- Modify: src/DigitalBrain.Hosting/Runtime/Outbox.cs
- Test: src/DigitalBrain.Core.Tests/Mechanics/RouterTests.cs
- Create: src/DigitalBrain.Core.Tests/Mechanics/DirectedDispatchTests.cs

### Step 1: Write failing routing and composed-host tests

Prove all of the following through recorded journals:

1. Broadcast retains the current registered-listener snapshot at the sender name.
2. Direct delivery from a source to receiver@destination records exactly receiver@destination, not sender-name listeners.
3. Direct delivery reaches the correct receiver in the same workspace and the source Produced record precedes its Received record.
4. A known but non-listening target, absent target kind, or source-identity target rejects before the source records a successful Produced record.
5. The same direct target name in a second workspace remains untouched.

### Step 2: Run the focused tests and observe RED

Run:

    dotnet test src/DigitalBrain.Core.Tests/DigitalBrain.Core.Tests.csproj --no-restore --filter-class DigitalBrain.DirectedDispatchTests

Expected: failure because Router only derives listener kinds at the sender’s name and the stager accepts no intent.

### Step 3: Implement snapshot routing

Router resolves broadcast exactly as today. For a direct intent it validates catalog existence, non-source identity, and listener compatibility before returning a singleton relative target. ProducedSynapseStager persists that target set in the existing source journal. NeuronHost threads staged intent into the stager. Outbox addresses the selected receiver with owner.Scope, so no direct output can leave its workspace. There is no direct GrainFactory path from module code.

### Step 4: Run focused tests and build GREEN

Run:

    dotnet test src/DigitalBrain.Core.Tests/DigitalBrain.Core.Tests.csproj --no-restore --filter-class DigitalBrain.DirectedDispatchTests
    dotnet test DigitalBrain.slnx --no-restore

Expected: a directed source output is durable before delivery and remains bound to one validated same-workspace target.

## Task 5 — Preserve scoped directed recovery semantics

**Files:**

- Modify: src/DigitalBrain.Core.Tests/Mechanics/DeliveryRecoveryTests.cs
- Modify: src/DigitalBrain.Core.Tests/Mechanics/RecordedTurnRecoveryTests.cs if a shared helper needs scope support
- Create: src/DigitalBrain.Core.Tests/Mechanics/ScopedDirectedRecoveryTests.cs

### Step 1: Write the failing recovery test

Inject a journal-write failure after a direct output has been staged but before it is recorded. Assert the receiver sees no input before successful redelivery; after recovery it sees exactly the original directed target once; a same-named receiver in another workspace remains empty. Also verify a terminal invalid direct route authors no synthetic DeliveryFailed because no source output was recorded.

### Step 2: Run the focused tests and observe RED

Run:

    dotnet test src/DigitalBrain.Core.Tests/DigitalBrain.Core.Tests.csproj --no-restore --filter-class DigitalBrain.ScopedDirectedRecoveryTests

Expected: failure until the scoped test fixture and direct stager preserve the existing durable write boundary.

### Step 3: Make only recovery-preserving changes

Use the existing poison/deactivate/reload path. Do not introduce a special direct retry protocol or route rewriting. A recorded target snapshot is the recovery source of truth.

### Step 4: Verify the foundation

Run:

    dotnet test DigitalBrain.slnx --no-restore
    git diff --check
    git status --short

Expected: all mechanics stay green, the diff has no whitespace errors, and no unplanned generated artifacts exist.

## Follow-on plans, not implementation in this slice

1. Time plus Approvals: frozen whole-proposal state, deadline facts, exact decision binding, and durable inbox projection.
2. Memory: typed V2 store/search/remove with fake contract tests plus one real Qdrant container suite.
3. Account Enrichment: Gmail webhook/chat ingress, evidence, prepared Salesforce mutation, approval, confirmed/uncertain result.
4. Presentation and Sales Insights: dynamic Base UI Kit surfaces, chat-first approval drawer/inbox, and typed sales chart result.
5. Marketplace evolution: trusted package lifecycle, catalog epochs, compatibility, and drain—not an untrusted in-process sandbox.
