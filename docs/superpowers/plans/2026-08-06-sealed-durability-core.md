# Sealed-Durability Core Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make DigitalBrain Core a pure module-author programming model, with a Hosting-owned Orleans adapter that preserves one recorded turn and post-recording delivery.

**Architecture:** `DigitalBrain.Abstractions` retains the shared vocabulary; `DigitalBrain.Core` becomes the behavior facade and public journal language; new `DigitalBrain.Access` exposes trusted publication and query capabilities; `DigitalBrain.Hosting` owns every Orleans type, native host, journal, routing, serialization, and delivery concern. A private `NeuronHost` is keyed by the encoded logical `NeuronId`, so behavior kinds remain catalog data rather than Orleans grain types.

**Tech Stack:** .NET 11, C#, Orleans 10, Orleans Journaling, System.Text.Json, xUnit v3.

## Global Constraints

- Preserve the durable invariant: one successful turn records received synapse, produced synapses, optional touched state, and deduplication watermark in one durable write; delivery begins afterward.
- Sealed durability is structural: Core, Access, and installed modules must not directly reference `Orleans.*`; only Hosting owns Orleans runtime code. Test-host infrastructure may reference Orleans solely to start an in-process cluster.
- A module references only Abstractions and Core. It never receives a grain base, factory, timer, lifecycle service, durable state manager, generic journal capability, or direct delivery control.
- Keep `Neuron` / `Neuron<TState>` / `INeuron<TSynapse>` as the behavior seam. `Id`, `Emit`, and `State` work only during one bound handler turn.
- `SynapseKind` is the canonical C# full type name. Behavior identity is an explicitly registered logical kind; modules carry no `[GrainType]` attribute.
- Public journal language is `Received` / `Produced`, `SynapseOrigin`, `SynapseReference`, `JournalRecord`, `JournalPage`, and `JournalHistoryUnavailable`. Journal reads expose raw JSON only.
- `SynapsePublisher.PublishAsync` records a source-produced synapse; it never promises downstream delivery. `JournalReader` is passive: it must not create behavior, record, resume, or start delivery.
- Use one private native `NeuronHost : DurableGrain` and the reserved `digitalbrain.synapse-source` logical source identity. Do not create a source-host grain or a Brain god-grain.
- Retire cleanly: no Edge, Brain, EdgeSession, Ingress, Fact, `JournalFact`, `NeuronReading`, `SynapseMetadata`, `SynapseRef`, `[GrainType]` module contract, or hidden legacy identity mapping in production source.
- Do not add product modules, scenario expansion, topology, scheduling, Ask/reply, polling, global history, typed public journal rehydration, learning/export product APIs, retention/compaction behavior, or authorization abstractions.
- Split each top-level type into its own file unless it is a tightly private nested implementation. Every edit must remove complexity or add a necessary verified mechanic.

## File Structure

| Area | Files and responsibility |
| --- | --- |
| `DigitalBrain.Abstractions` | Keep `Synapse`, `INeuron<TSynapse>`, and a pure `NeuronId`; remove type-derived kind discovery. |
| `DigitalBrain.Core` | `Neuron`, `Neuron<TState>`, internal `ITurnBinding`, and one-file public journal value/outcome types. No Orleans references or packages. |
| `DigitalBrain.Access` | `SynapseSource`, `SynapsePublisher`, and `JournalReader` capability contracts. It references Core. |
| `DigitalBrain.Hosting/Composition` | `DigitalBrainComposition`, explicit catalog, registration validation, serializer wiring, and module-reference gate. |
| `DigitalBrain.Hosting/Runtime` | Private `NeuronHost`, durable journal/storage model, routing, source transport, outbox, wakeup, envelope carrier, and key encoder. |
| `DigitalBrain.Testing` | Cluster fixture and raw-record test helpers. It is test infrastructure, not a module surface. |
| `DigitalBrain.Mocks` | A clean installed-module proof: Core/Abstractions only, explicit logical registration supplied by its test. |

---

### Task 1: Establish the pure public vocabulary and behavior facade

**Files:**
- Create: `src/DigitalBrain.Core/Neuron.cs`
- Create: `src/DigitalBrain.Core/NeuronOfState.cs`
- Create: `src/DigitalBrain.Core/Internal/ITurnBinding.cs`
- Create: `src/DigitalBrain.Core/JournalRecordDirection.cs`
- Create: `src/DigitalBrain.Core/SynapseOrigin.cs`
- Create: `src/DigitalBrain.Core/SynapseReference.cs`
- Create: `src/DigitalBrain.Core/JournalRecord.cs`
- Create: `src/DigitalBrain.Core/JournalRead.cs`
- Create: `src/DigitalBrain.Core/JournalPage.cs`
- Create: `src/DigitalBrain.Core/JournalHistoryUnavailable.cs`
- Create: `src/DigitalBrain.Core/DeliveryFailed.cs`
- Modify: `src/DigitalBrain.Abstractions/NeuronId.cs`
- Modify: `src/DigitalBrain.Core/DigitalBrain.Core.csproj`
- Delete: `src/DigitalBrain.Core/Outcomes.cs`
- Delete after Hosting takes ownership: `src/DigitalBrain.Core/Runtime/Neuron.cs`, `src/DigitalBrain.Core/Runtime/NeuronOfState.cs`
- Test: `src/DigitalBrain.Core.Tests/Mechanics/BehaviorFacadeTests.cs`
- Test: `src/DigitalBrain.Core.Tests/Mechanics/PublicSurfaceTests.cs`

**Consumes:** `NeuronId`, `Synapse`, and `INeuron<TSynapse>` from Abstractions.

**Produces:** A Core with no Orleans package or symbol dependency and this public shape:

```csharp
public abstract class Neuron
{
    protected NeuronId Id { get; }
    protected void Emit(Synapse synapse);
}

public abstract class Neuron<TState> : Neuron
    where TState : class, new()
{
    protected TState State { get; set; }
}

public sealed record JournalRecord(
    long Position,
    JournalRecordDirection Direction,
    string SynapseKind,
    SynapseOrigin Origin,
    SynapseReference? CausedBy,
    IReadOnlyList<NeuronId> DeliveryTargets,
    JsonElement Serialization);
```

- [ ] **Step 1: Write failing behavior-facade tests**

Add tests using an internal fake `ITurnBinding` that prove `Emit` stages a supplied synapse, `Id` is supplied by the bound turn, state is lazy and mutable, and all three throw after unbinding. Add a reflection test that Core’s assembly references no assembly whose name contains `Orleans`.

- [ ] **Step 2: Run the focused tests and verify RED**

Run: `dotnet test src/DigitalBrain.Core.Tests/DigitalBrain.Core.Tests.csproj --no-restore --filter-class DigitalBrain.BehaviorFacadeTests`

Expected: FAIL because the pure facade and binding do not exist.

- [ ] **Step 3: Write the minimal pure facade and value types**

Implement an internal binding with only `NeuronId Id`, `Stage(Synapse)`, `GetState<TState>()`, and `SetState<TState>(TState)`. Bind it internally before a handler and clear it afterward. Delete `NeuronId.KindOf(Type)`. Split the old outcomes into the approved one-file-per-type value model; make `DeliveryFailed` use `SynapseReference Synapse` rather than a Fact-named member. Remove all Orleans package references from Core.

- [ ] **Step 4: Run focused tests and build GREEN**

Run:

```powershell
dotnet test src/DigitalBrain.Core.Tests/DigitalBrain.Core.Tests.csproj --no-restore
dotnet build src/DigitalBrain.Core/DigitalBrain.Core.csproj --no-restore
```

Expected: facade tests pass and Core compiles without Orleans packages.

- [ ] **Step 5: Refactor only duplicate binding checks**

Keep `Neuron` as the sole owner of binding lifetime. Do not add public host APIs or state serialization concerns.

### Task 2: Add the trusted Access capability contracts

**Files:**
- Create: `src/DigitalBrain.Access/DigitalBrain.Access.csproj`
- Create: `src/DigitalBrain.Access/SynapseSource.cs`
- Create: `src/DigitalBrain.Access/SynapsePublisher.cs`
- Create: `src/DigitalBrain.Access/JournalReader.cs`
- Modify: `DigitalBrain.slnx`
- Test: `src/DigitalBrain.Core.Tests/Mechanics/AccessContractTests.cs`

**Consumes:** Core’s journal outcome model and Abstractions’ `Synapse` / `NeuronId`.

**Produces:** A separate capability assembly whose only public operations are:

```csharp
public readonly record struct SynapseSource(string Name);

public interface SynapsePublisher
{
    Task PublishAsync(SynapseSource source, Synapse synapse,
        CancellationToken cancellationToken = default);
}

public interface JournalReader
{
    Task<JournalRead> ReadAsync(NeuronId neuron, long afterPosition,
        int maximumRecords, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 1: Write failing project-boundary tests**

Add tests that load the Access assembly and assert it references Core but no Orleans assembly. Assert the source name rejects blank values at its public construction boundary if a constructor/validation implementation is used.

- [ ] **Step 2: Run the focused test and verify RED**

Run: `dotnet test src/DigitalBrain.Core.Tests/DigitalBrain.Core.Tests.csproj --no-restore --filter-class DigitalBrain.AccessContractTests`

Expected: FAIL because `DigitalBrain.Access` is absent.

- [ ] **Step 3: Add the minimum contracts and solution entry**

Create the project with a direct Core reference. Do not add a client implementation, policy type, authorization hook, or typed record deserializer.

- [ ] **Step 4: Run the focused test and build GREEN**

Run:

```powershell
dotnet test src/DigitalBrain.Core.Tests/DigitalBrain.Core.Tests.csproj --no-restore
dotnet build DigitalBrain.slnx --no-restore
```

Expected: the new contracts compile and all existing build targets are restored.

### Task 3: Create explicit Hosting composition and structural admission gates

**Files:**
- Create: `src/DigitalBrain.Hosting/Composition/DigitalBrainComposition.cs`
- Create: `src/DigitalBrain.Hosting/Composition/CompositionCatalog.cs`
- Create: `src/DigitalBrain.Hosting/Composition/ModuleAssemblyBoundary.cs`
- Create: `src/DigitalBrain.Hosting/Composition/SynapseKinds.cs`
- Modify: `src/DigitalBrain.Hosting/Catalog.cs` or replace it with `CompositionCatalog.cs`
- Modify: `src/DigitalBrain.Hosting/BodyCodec.cs`
- Modify: `src/DigitalBrain.Hosting/DigitalBrainSiloExtensions.cs`
- Test: `src/DigitalBrain.Core.Tests/Mechanics/CatalogBootTests.cs`
- Test: `src/DigitalBrain.Core.Tests/Mechanics/ModuleBoundaryTests.cs`

**Consumes:** pure Core behavior types and Access contracts.

**Produces:** `RegisterVocabulary(Assembly)` and `RegisterNeuron<TBehavior>(string kind)` as the only registration model. Logical behavior kinds are explicit; synapse kinds are `Type.FullName`.

- [ ] **Step 1: Rewrite registration tests first**

Replace `[GrainType]` assertions with tests for a successful explicit kind, duplicate kind rejection, unregistered handled synapse rejection, duplicate canonical full-name rejection, and a pure helper that rejects the assembly names `Orleans.Core` and `DigitalBrain.Access`.

- [ ] **Step 2: Run the focused tests and verify RED**

Run: `dotnet test src/DigitalBrain.Core.Tests/DigitalBrain.Core.Tests.csproj --no-restore --filter-class DigitalBrain.CatalogBootTests`

Expected: FAIL because the legacy catalog still derives kinds from grain attributes.

- [ ] **Step 3: Implement sealed composition**

`RegisterVocabulary` scans concrete, sealed `Synapse` types from exactly the supplied assembly. `RegisterNeuron<TBehavior>` records the supplied nonblank logical kind and discovers `INeuron<TSynapse>` contracts. At seal time, validate vocabulary, state materialization, duplicate full names, reserved `digitalbrain.synapse-source`, and direct module references. Keep `DeliveryFailed` as the one built-in Core synapse. Rename private codec methods around serialization and dispatch; public journal reads must never call a decode method.

- [ ] **Step 4: Run registration tests and build GREEN**

Run:

```powershell
dotnet test src/DigitalBrain.Core.Tests/DigitalBrain.Core.Tests.csproj --no-restore
dotnet build src/DigitalBrain.Hosting/DigitalBrain.Hosting.csproj --no-restore
```

Expected: module attributes are neither required nor inspected.

### Task 4: Move the durable runtime behind a single private NeuronHost

**Files:**
- Create: `src/DigitalBrain.Hosting/Runtime/NeuronHost.cs`
- Create: `src/DigitalBrain.Hosting/Runtime/INeuronHost.cs`
- Create: `src/DigitalBrain.Hosting/Runtime/TurnBinding.cs`
- Create: `src/DigitalBrain.Hosting/Runtime/NeuronKey.cs`
- Create: `src/DigitalBrain.Hosting/Runtime/Journal.cs`
- Create: `src/DigitalBrain.Hosting/Runtime/StoredJournalRecord.cs`
- Create: `src/DigitalBrain.Hosting/Runtime/DeliveryEnvelope.cs`
- Create: `src/DigitalBrain.Hosting/Runtime/DeliveryTarget.cs`
- Create: `src/DigitalBrain.Hosting/Runtime/DeliveryProgress.cs`
- Create: `src/DigitalBrain.Hosting/Runtime/WatermarkEntry.cs`
- Create: `src/DigitalBrain.Hosting/Runtime/JournalJsonContext.cs`
- Create: `src/DigitalBrain.Hosting/Runtime/Router.cs`
- Create: `src/DigitalBrain.Hosting/Runtime/ProducedSynapseStager.cs`
- Modify: `src/DigitalBrain.Hosting/DigitalBrainSiloExtensions.cs`
- Modify: `src/DigitalBrain.Hosting/GatedStateManager.cs`
- Modify: `src/DigitalBrain.Hosting/RequestContextEnvelopeCarrier.cs`
- Delete: moved Core runtime types once their Hosting equivalents compile
- Test: `src/DigitalBrain.Core.Tests/Mechanics/RouterTests.cs`
- Test: `src/DigitalBrain.Core.Tests/Mechanics/TurnRecordingTests.cs`

**Consumes:** sealed catalog, body serialization, pure Core turn binding.

**Produces:** One fixed `[GrainType("digitalbrain.neuron-host")]` host keyed by `NeuronKey.Encode(NeuronId)`. It resolves a registered behavior and creates a fresh DI behavior instance for each received synapse.

- [ ] **Step 1: Write failing host-turn tests**

Write integration tests that send a known synapse to a registered logical neuron and prove a behavior receives its bound `Id`, stages produced synapses, preserves a touched state value, and uses a fresh behavior instance on the next input. Add a unit test that the native key round-trips delimiter-bearing logical ids.

- [ ] **Step 2: Run focused tests and verify RED**

Run: `dotnet test src/DigitalBrain.Core.Tests/DigitalBrain.Core.Tests.csproj --no-restore --filter-class DigitalBrain.TurnRecordingTests`

Expected: FAIL because no private host can bind and execute behavior.

- [ ] **Step 3: Port mechanics without changing their transaction order**

Move the durable Journal, key encoder, routing, request envelope, and staging algorithm to Hosting. Store an origin on every record, use `Received` / `Produced`, omit the obsolete one-value `SpeechRole`, retain watermarks/cursor/progress/state, and keep `LastRecorded` as the visibility fence. The host flow is:

```text
delivery envelope -> dedup -> bind fresh behavior -> handler
  -> append received + produced + touched state + watermark
  -> arm durable wakeup if output is pending
  -> one WriteStateAsync -> mark recorded -> unbind -> kick outbox
```

If a handler throws, clear the bound turn without recording. If durable write fails, poison/deactivate the host so it reloads recorded truth.

- [ ] **Step 4: Run focused mechanics tests and build GREEN**

Run:

```powershell
dotnet test src/DigitalBrain.Core.Tests/DigitalBrain.Core.Tests.csproj --no-restore
dotnet build DigitalBrain.slnx --no-restore
```

Expected: behavior code remains Orleans-free while turn recording is hosted.

### Task 5: Port post-recording delivery and recovery to Hosting

**Files:**
- Create: `src/DigitalBrain.Hosting/Runtime/Outbox.cs`
- Create: `src/DigitalBrain.Hosting/Runtime/OutboxWakeup.cs`
- Create: `src/DigitalBrain.Hosting/Runtime/IOutboxWakeup.cs`
- Create: `src/DigitalBrain.Hosting/Runtime/DeliveryPolicy.cs`
- Create: `src/DigitalBrain.Hosting/Runtime/DeliveryResult.cs`
- Modify: `src/DigitalBrain.Hosting/Runtime/NeuronHost.cs`
- Modify: `src/DigitalBrain.Hosting/CoreWireTypeFilter.cs`
- Delete: old Core `Runtime/Outbox.cs` and `Runtime/Delivery.cs`
- Test: `src/DigitalBrain.Core.Tests/Mechanics/OutboxTests.cs`
- Test: `src/DigitalBrain.Core.Tests/Mechanics/PassiveReadTests.cs`

**Consumes:** `NeuronHost`, stored journal records, host transport, `DeliveryFailed`.

**Produces:** Delivery that starts only after recording, routes every target through fixed `NeuronHost` address, persists retry progress, and uses an armed reminder for recovery.

- [ ] **Step 1: Rewrite outbox tests to the new vocabulary**

Replace `heard`/`said`/typed-body assertions with `Received`/`Produced`, canonical kind, raw JSON, origin, cause, and target assertions. Keep the existing mechanical proofs: sender record precedes receiver receipt, cyclic child delivery does not hold the source call, and zero-target production is recorded.

- [ ] **Step 2: Run focused tests and verify RED**

Run: `dotnet test src/DigitalBrain.Core.Tests/DigitalBrain.Core.Tests.csproj --no-restore --filter-class DigitalBrain.OutboxTests`

Expected: FAIL until the hosting outbox addresses private hosts and produces the new record model.

- [ ] **Step 3: Implement the minimal durable outbox**

Before a turn record with pending targets is written, arm its fixed wakeup. After successful recording, kick a local retry timer. The wakeup decodes the logical id and calls only `NeuronHost`. Delete activation-time `ResumeAsync`: a read activation must never start delivery. Preserve at-least-once retry and record any terminal `DeliveryFailed` through the same durable host path.

- [ ] **Step 4: Run focused tests and build GREEN**

Run:

```powershell
dotnet test src/DigitalBrain.Core.Tests/DigitalBrain.Core.Tests.csproj --no-restore
dotnet build DigitalBrain.slnx --no-restore
```

Expected: one-recording/post-recording delivery remains intact without a module grain.

### Task 6: Implement source publication and passive raw journal access

**Files:**
- Create: `src/DigitalBrain.Hosting/Runtime/SynapseSourceIdentity.cs`
- Create: `src/DigitalBrain.Hosting/Access/OrleansSynapsePublisher.cs`
- Create: `src/DigitalBrain.Hosting/Access/OrleansJournalReader.cs`
- Modify: `src/DigitalBrain.Hosting/Runtime/INeuronHost.cs`
- Modify: `src/DigitalBrain.Hosting/Runtime/NeuronHost.cs`
- Modify: `src/DigitalBrain.Hosting/DigitalBrainSiloExtensions.cs`
- Modify: `src/DigitalBrain.Hosting/BodyCodec.cs`
- Test: `src/DigitalBrain.Core.Tests/Mechanics/SourcePublicationTests.cs`
- Test: `src/DigitalBrain.Core.Tests/Mechanics/JournalReaderTests.cs`

**Consumes:** Access contracts and the hosted journal/outbox.

**Produces:** Source publication via `NeuronId("digitalbrain.synapse-source", source.Name)` and passive `JournalReader.ReadAsync` outcomes.

- [ ] **Step 1: Write failing source and reader tests**

Add a test proving publication completion returns after the source’s `Produced` record exists even if its receiver is delayed. Add a reader test that raw serialization is returned for a known record, a page reports exact positions, an unavailable-history request returns `JournalHistoryUnavailable`, and reading a pending target does not invoke behavior or delivery.

- [ ] **Step 2: Run focused tests and verify RED**

Run: `dotnet test src/DigitalBrain.Core.Tests/DigitalBrain.Core.Tests.csproj --no-restore --filter-class DigitalBrain.SourcePublicationTests`

Expected: FAIL because legacy `Ingress` is still the only write endpoint.

- [ ] **Step 3: Implement source and reader adapters**

`SynapsePublisher` maps only the validated `SynapseSource` to the reserved private identity; it records a produced synapse with no received predecessor or cause. The source identity cannot be registered as a behavior or target. `JournalReader` invokes the private host’s read operation, validates `maximumRecords > 0`, maps stored records to immutable public raw records, and never deserializes a journal body or triggers outbox work. A cursor before the available range returns `JournalHistoryUnavailable`; with no retention feature, the range starts at position 1.

- [ ] **Step 4: Run source/reader tests and build GREEN**

Run:

```powershell
dotnet test src/DigitalBrain.Core.Tests/DigitalBrain.Core.Tests.csproj --no-restore
dotnet build DigitalBrain.slnx --no-restore
```

Expected: clients have trusted narrow access without Edge or a second grain type.

### Task 7: Migrate test infrastructure and the clean module proof

**Files:**
- Modify: `src/DigitalBrain.Testing/DigitalBrain.Testing.csproj`
- Modify: `src/DigitalBrain.Testing/Fixture/ComposedFixture.cs`
- Modify: `src/DigitalBrain.Testing/Fixture/DigitalBrainTest.cs`
- Modify: `src/DigitalBrain.Testing/Fixture/DigitalBrainTestBuilder.cs`
- Modify: `src/DigitalBrain.Testing/Fixture/NeuronTest.cs`
- Modify: `src/DigitalBrain.Testing/Journal/JournalAssertions.cs`
- Modify: `src/DigitalBrain.Testing/Journal/RecordingJournalStorageProvider.cs`
- Modify: `src/DigitalBrain.Mocks/DigitalBrain.Mocks.csproj`
- Modify: `src/DigitalBrain.Mocks/**/*.cs`
- Move: `src/DigitalBrain.Mocks/Composition/MockComposition.cs` to `src/DigitalBrain.Mocks.Tests/Support/MockComposition.cs`
- Modify: `src/DigitalBrain.Mocks.Tests/**/*.cs`
- Test: `src/DigitalBrain.Mocks.Tests/Smoke/MockXSmokeTests.cs`

**Consumes:** Access adapters and explicit Hosting composition.

**Produces:** Test fixture helpers that publish/read through real Access interfaces and installed mock modules that contain no Orleans or Testing dependency.

- [ ] **Step 1: Rewrite a mock smoke test first**

Change the smoke test to publish through `SynapsePublisher`, read `JournalPage`, assert `SynapseKind`, raw JSON, origin, cause, and targets, and use registered kinds from its test composition. Remove all module `[GrainType]` attributes.

- [ ] **Step 2: Run the mock test and verify RED**

Run: `dotnet test src/DigitalBrain.Mocks.Tests/DigitalBrain.Mocks.Tests.csproj --no-restore --filter-class DigitalBrain.Mocks.Tests.Smoke.MockXSmokeTests`

Expected: FAIL while the fixture still constructs `Brain` / `EdgeSession`.

- [ ] **Step 3: Migrate the fixture coherently**

Make the test builder record vocabulary assemblies and `(behavior type, logical kind)` registrations. Configure both test silo and client serialization/access through the same composition callback. Retrieve `SynapsePublisher` and `JournalReader` from `IClusterClient.ServiceProvider`. Rewrite deactivation and journal-fault key lookup to `NeuronHost`’s encoded key. Keep typed JSON deserialization, if a test needs it, only as a test helper over `JournalRecord.Serialization`.

- [ ] **Step 4: Make Mocks a boundary proof**

Drop Mocks’ Orleans SDK and Testing references, delete all `[GrainType]` attributes, and move test composition out of the module assembly. Verify the Mocks project directly references only Core (and transitively Abstractions).

- [ ] **Step 5: Run test projects and build GREEN**

Run:

```powershell
dotnet test src/DigitalBrain.Core.Tests/DigitalBrain.Core.Tests.csproj --no-restore
dotnet test src/DigitalBrain.Mocks.Tests/DigitalBrain.Mocks.Tests.csproj --no-restore
dotnet build DigitalBrain.slnx --no-restore
```

Expected: integration tests exercise the real private host through Access, not a test-only transport shortcut.

### Task 8: Retire Edge, Ingress, and legacy vocabulary

**Files:**
- Delete: `src/DigitalBrain.Edge/Brain.cs`
- Delete: `src/DigitalBrain.Edge/EdgeSession.cs`
- Delete: `src/DigitalBrain.Edge/DigitalBrain.Edge.csproj`
- Delete: `src/DigitalBrain.Core/Runtime/Ingress.cs`
- Delete: remaining moved Core runtime files
- Modify: `DigitalBrain.slnx`
- Modify: project references and `InternalsVisibleTo` declarations
- Test: all test projects

**Consumes:** completed Access adapters and migrated test clients.

**Produces:** No legacy application-facing Edge transport or persisted `digitalbrain.ingress` emission path.

- [ ] **Step 1: Write a structural legacy-free test**

Add a reflection/package-reference test that Core, Access, and Mocks have no Orleans reference and a source check in the test command that reports remaining retired production symbols. Do not test exact documentation text.

- [ ] **Step 2: Run the structural test and verify RED**

Run: `dotnet test src/DigitalBrain.Core.Tests/DigitalBrain.Core.Tests.csproj --no-restore --filter-class DigitalBrain.PublicSurfaceTests`

Expected: FAIL while Edge/Ingress and old Core runtime types remain.

- [ ] **Step 3: Delete the retired implementation and repair references**

Remove Edge from the solution and test infrastructure, remove old Core runtime types only after their Hosting counterparts are used, and remove stale friend assemblies. Keep no compatibility alias or hidden mapping for `digitalbrain.ingress`; old data requires explicit export/migration outside this refactor.

- [ ] **Step 4: Verify the structural boundary**

Run:

```powershell
dotnet build DigitalBrain.slnx --no-restore
rg -n "Orleans|GrainType|IGrainFactory|DurableGrain" src/DigitalBrain.Abstractions src/DigitalBrain.Core src/DigitalBrain.Access src/DigitalBrain.Mocks
rg -n "\b(Edge|Brain|EdgeSession|Ingress|JournalFact|NeuronReading|SynapseMetadata|SynapseRef|Fact)\b" src --glob '!**/bin/**' --glob '!**/obj/**'
```

Expected: no production boundary violations; occurrences in intentional migration prose are reviewed manually rather than preserved as compatibility code.

### Task 9: Update the durable language and verify the whole branch

**Files:**
- Modify: `src/DigitalBrain.Core/README.md`
- Modify: `CONTEXT.md`
- Modify: `CORE-ARCHITECTURE.md` only if it still claims the retired public seam
- Test: full solution and targeted architectural scans

**Consumes:** completed code paths and verified behavior.

**Produces:** Documentation that accurately names Core’s role and does not teach retired Edge/fact/commit language.

- [ ] **Step 1: Re-read completed public API and existing docs**

Compare each documented type and invariant to the compiled public surface. Remove stale claims rather than adding parallel terminology.

- [ ] **Step 2: Update Core README and context vocabulary**

Describe Core as a thin module programming model: a received synapse opens a turn, behavior produces synapses/state, Hosting records the turn, Journal is recorded truth, and delivery follows. Document the package boundary and explicit non-goals. State that product-owned learning/export must apply consent/redaction/retention outside Core.

- [ ] **Step 3: Run final verification**

Run:

```powershell
dotnet test DigitalBrain.slnx --no-restore
dotnet build DigitalBrain.slnx --no-restore
git diff --check
git status --short
```

Expected: all tests/build pass, no whitespace errors, and the diff contains only the approved refactor and its necessary docs/tests.

## Self-Review

**Spec coverage:** Tasks 1–2 establish the pure behavior/capability seam; Task 3 makes identity/registration explicit and gates forbidden dependencies; Tasks 4–5 preserve durable recording and delivery; Task 6 adds source publication and passive reads; Task 7 gives real cluster tests and proves module purity; Task 8 removes legacy seams; Task 9 updates the durable language and completes verification. No task introduces a product module, learning exporter, scheduler, global journal, reply protocol, or authorization framework.

**Placeholder scan:** This plan contains no deferred implementation placeholders. Each task lists exact files, behavior, and verification commands.

**Type consistency:** `NeuronId`, `Synapse`, `INeuron<TSynapse>`, `SynapseSource`, `SynapsePublisher`, `JournalReader`, `JournalRead`, `JournalPage`, `JournalHistoryUnavailable`, `SynapseOrigin`, `SynapseReference`, and `JournalRecord` are used consistently throughout.

## Execution Handoff

The user explicitly approved autonomous execution of every phase. Execute the tasks in order with test-first changes and scoped reviews; do not pause for an execution-choice prompt.
