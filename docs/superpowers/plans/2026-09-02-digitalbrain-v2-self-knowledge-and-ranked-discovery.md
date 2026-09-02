# DigitalBrain v2 Self-Knowledge and Ranked Discovery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give DigitalBrain a canonical typed self-catalog and owner-aware `discover`/`inspect` surface which returns compatible ranked candidates using exact, lexical, and dense retrieval without allowing vector search to invoke anything.

**Architecture:** Configured modules contribute immutable canonical descriptors through a separate contributor interface; later durable definitions implement the same source/resolution contract. A metadata/lexical projection and a versioned Qdrant dense projection are rebuildable from those authorities. Discovery hard-filters compatibility, hydrates every hit against its exact source revision/fingerprint, fuses lexical and semantic rank positions with RRF, and returns candidates only; the owner-scoped catalog grain, optional catalog client, and assistant tools contain no ranking or authorization logic.

**Tech Stack:** .NET 11, C# latest, Orleans 10.2.2, Microsoft.Extensions.AI 10.9.0, System.Text.Json JSON-schema exporter, Qdrant.Client 1.19.0, Aspire 13.5.2, xUnit v3, Microsoft Testing Platform, PowerShell, XML solution (`DigitalBrain.slnx`).

**Spec:** [`docs/superpowers/specs/2026-09-02-digitalbrain-v2-self-knowledge-and-ranked-discovery-design.md`](../specs/2026-09-02-digitalbrain-v2-self-knowledge-and-ranked-discovery-design.md)

## Global Constraints

- `discover` returns compatible ranked candidates. It never invokes the top result, delivers a signal, creates or strengthens a synapse, creates a run, or grants a capability.
- Discovery candidates are provisional. The assistant or routing policy inspects one or more exact
  references, then chooses; catalog inspection resolves the requested source revision and
  fingerprint and never follows a newer active pointer silently.
- Keep canonical catalog authority in configured module contributions and, later, journaled definition aggregates. Exact, lexical, and Qdrant state are rebuildable projections.
- Keep `VectorMemoryNeuron`, `IVectorMemoryStore`, the `digitalbrain_vector_memory` collection, and public vector-memory namespaces unchanged. User memory and self-knowledge must not cross-read or cross-write.
- Keep `SignalRouter` and `SignalHandlerIndex` behavior unchanged in this slice. Catalog metadata must not alter Tier-1/Tier-2 routing, and automatic Tier-3 similarity routing remains deferred.
- Keep `IModule.Configure(ISiloBuilder)` unchanged. Add the separate `ICatalogContributor` interface so configuration and description remain separate responsibilities.
- Keep discovery off `IBrainNeuron`: Catalog.Contracts references only kernel contracts, and the
  kernel runtime does not reference Catalog. Core registers its configured hook instances through
  `ConfiguredModuleHooks`; Catalog consumes that neutral set. The owner-keyed `ICatalogDirectory`
  grain and `IDigitalBrainCatalog` client form a separate optional module surface; `IDigitalBrain`
  remains unchanged. Catalog runtime references AI.Contracts only for the temporary
  `IAgentToolSource` adapter and selected embedding-profile identity; AI does not reference Catalog.
- Instantiate only module types selected by `ModuleManifest`; loaded-but-unconfigured assemblies and test types must never enter the static catalog.
- A model-supplied query never contains `OwnerId`. The owner comes from the catalog grain key/client context. Search includes platform entries and that owner's overlay only.
- Apply visibility, lifecycle, scope, kind, capability/version, signal/schema, input/output schema,
  tag, and invocability compatibility before rank can affect the result.
- A canonical descriptor contains declared/configured/disabled state. Timestamped connectivity and health are a non-authoritative availability overlay, excluded from descriptor fingerprints and vectors, resolved before final ranking.
- `Capability` is a discoverable definition/grouping. `CapabilityLease` remains runtime authority; a catalog capability result never grants permission.
- Capability candidates are not invocable. Their stable capability/version can constrain a follow-up
  operation discovery; only an exact operation handle can later be passed to `invoke`.
- Exact descriptor ID, operation/capability ID, name, or declared alias matches form a dominant bucket. Fuse the remaining one-based lexical and semantic ranks with `1 / (60 + rank)`; do not add raw cosine and lexical scores.
- Retrieval uses `CandidatePoolLimit = min(512, max(64, requestedLimit * 8))`. Reassign contiguous
  one-based lane ranks only after every compatibility, hydration, visibility, and requested
  availability filter; filtered provider hits never consume RRF rank.
- Expose the final one-based order as `FinalRank` and the fused diagnostic value as `RrfScore`, never as confidence. Preserve semantic similarity only as diagnostic evidence.
- Semantic rank alone never authorizes automatic selection. A later execution must persist the
  final exact handle and a `SelectionDecision` after inspection; this slice only returns and
  inspects candidates.
- Embed only safe discovery fields in a fixed documented order. Never embed credentials, protected payloads, raw script source, user memory, arbitrary entity state, provider results, journals, or signal occurrences.
- `CatalogEmbeddingProfile` includes provider/model ID, operator-controlled model revision, dimensions, preprocessing version, and discovery-document version. Validate every emitted vector width before writing.
- Give each profile a deterministic generation ID and physical collection. Populate and catch up a new generation before atomically moving the stable Qdrant alias; never infer identity from the first vector's width.
- Bind cursors to a semantic snapshot token covering active generation, deployment epoch/manifest,
  physical-collection incarnation, projected metadata watermark, and metadata-snapshot fingerprint. Same-generation
  catch-up, static replacement, and loss/recovery must invalidate old cursors; a semantically
  degraded response emits no next cursor.
- Require the ready semantic snapshot's projected watermark/fingerprint to equal the exact metadata
  snapshot used for ranking before retaining semantic evidence or a cursor. Bind the cursor to an
  availability snapshot token whose registry incarnation changes on process restart, never only to
  repeatable numeric watermarks.
- Serialize every Qdrant build/cutover through a coordinator keyed by connection plus active alias,
  never by generation. Its request pins deployment epoch, selected profile, exact metadata snapshot
  identity, and per-partition checkpoints. Lower epochs and regressing/incomparable snapshots cannot
  replace active state; an implementation running on a silo whose local immutable snapshot/profile
  differs from the request must refuse before writing.
- If embeddings or Qdrant fail, exact and lexical discovery still return and the result reports semantic degradation. Exact `inspect` must not depend on Qdrant.
- Do not add `Microsoft.Extensions.VectorData.Abstractions`; this slice uses the already-pinned MEAI and Qdrant packages plus an application-owned lexical index and RRF.
- Keep package versions in `Directory.Packages.props`; never put `Version` on a `PackageReference`.
- Before each commit, inspect `git status --short` and `git diff --cached --name-only`; stage only
  the task's listed files. Never sweep unrelated native-branch edits into a task commit.
- Every new Orleans wire type carries `[GenerateSerializer]`, a stable `[Alias("db.catalog.…")]`, and explicit `[Id]` members. Value objects validate non-empty input and preserve ordinal identity semantics.
- Keep `net11.0`, nullable analysis, warnings-as-errors, preview analyzers, and code-style enforcement green.
- After every task, run the focused tests, then:

  ```powershell
  dotnet build DigitalBrain.slnx -c Release
  dotnet test DigitalBrain.slnx -c Release --no-build --no-restore --verbosity minimal --max-parallel-test-modules 1
  ```

---

## File Map

### Create — catalog contracts

- `src/Modules/Catalog/Contracts/DigitalBrain.Modules.Catalog.Contracts.csproj` — wire-safe catalog and discovery vocabulary; references kernel contracts only.
- `src/Modules/Catalog/Contracts/CatalogIdentity.cs` — `CatalogEntryId`, `CatalogScope`, `CatalogSourceReference`, and exact `CatalogReference`.
- `src/Modules/Catalog/Contracts/CatalogDescriptor.cs` — descriptor header, lifecycle, canonical configuration state, safe discovery text, and typed resource reference.
- `src/Modules/Catalog/Contracts/CatalogOperationDescriptor.cs` — operation/capability versions, exact input/output schemas, recovery semantics, and binding reference.
- `src/Modules/Catalog/Contracts/CatalogDiscovery.cs` — `DiscoveryQuery`, typed compatibility constraints, evidence, candidate, cursor, result, and degradation status.
- `src/Modules/Catalog/Contracts/CatalogInspection.cs` — exact inspection outcome and status.
- `src/Modules/Catalog/Contracts/Inspection.cs` — stable discriminated assistant inspection
  references/results for catalog descriptors, neurons, synapses, entities, and durable resources.
- `src/Modules/Catalog/Contracts/CatalogProjection.cs` — idempotent upsert/tombstone mutation vocabulary and ordered source epoch/sequence position.
- `src/Modules/Catalog/Contracts/CatalogContribution.cs` — immutable configured-module contribution with stable module type name and descriptors.
- `src/Modules/Catalog/Contracts/ICatalogContributor.cs` — separate static module-description responsibility.
- `src/Modules/Catalog/Contracts/ICatalogSource.cs` — enumerate and exact-resolve contract used by static and future durable sources.
- `src/Modules/Catalog/Contracts/ICatalogDirectory.cs` — owner-keyed Orleans `discover`/`inspect` application port.
- `src/Modules/Catalog/Contracts/IInspectionProvider.cs` — local extension port keyed by general
  inspection kind plus durable resource kind where applicable.

### Create — catalog authoring SDK

- `src/Modules/Catalog/Sdk/DigitalBrain.Modules.Catalog.Sdk.csproj` — module-authoring helpers; references Catalog.Contracts and kernel contracts.
- `src/Modules/Catalog/Sdk/CatalogContributionBuilder.cs` — strongly typed module/neuron/signal/operation declaration builder.
- `src/Modules/Catalog/Sdk/CatalogSchemaFingerprint.cs` — System.Text.Json schema export, canonical JSON ordering, and SHA-256 hashing.
- `src/Modules/Catalog/Sdk/CatalogDescriptorFingerprint.cs` — canonical descriptor hashing which excludes the self-referential fingerprint slot and all projection data.

### Create — optional catalog client

- `src/Modules/Catalog/Client/DigitalBrain.Modules.Catalog.Client.csproj` — owner-bound public client; references Catalog.Contracts only.
- `src/Modules/Catalog/Client/IDigitalBrainCatalog.cs` — optional discover/inspect facade independent of `IDigitalBrain`.
- `src/Modules/Catalog/Client/DigitalBrainCatalogClient.cs` — logic-free `IGrainFactory` forwarding to the owner-keyed directory grain.

### Create — catalog runtime module

- `src/Modules/Catalog/Catalog/DigitalBrain.Modules.Catalog.csproj` — catalog runtime, ranking, Qdrant adapter, grain, and assistant adapter; references Catalog.Contracts/SDK, kernel runtime, AI.Contracts, MEAI, and Qdrant, never Memory.
- `src/Modules/Catalog/Catalog/CatalogModule.cs` — provider selection and DI composition.
- `src/Modules/Catalog/Catalog/CatalogRuntimeOptions.cs` — provider/profile/collection configuration
  plus the positive monotonic deployment epoch and validated options.
- `src/Modules/Catalog/Catalog/Sources/StaticCatalogSource.cs` — authoritative configured-module descriptors.
- `src/Modules/Catalog/Catalog/Sources/CatalogSourceRegistry.cs` — source enumeration and exact hydration by source kind/revision.
- `src/Modules/Catalog/Catalog/Projection/CatalogDocument.cs` — safe projection document and deterministic document key.
- `src/Modules/Catalog/Catalog/Projection/CatalogMetadataProjection.cs` — owner/global current-revision gate, mutation ordering, tombstones, and watermark.
- `src/Modules/Catalog/Catalog/Projection/CatalogProjectionCoordinator.cs` — enumerate, canonicalize, embed, upsert, catch up, and publish generation readiness.
- `src/Modules/Catalog/Catalog/Initialization/CatalogReadiness.cs` — phase/readiness state and atomic immutable snapshot publication.
- `src/Modules/Catalog/Catalog/Initialization/CatalogInitializationHostedService.cs` — startup rebuild plus persistent cancellation-aware semantic reconciliation with bounded backoff.
- `src/Modules/Catalog/Catalog/Initialization/CatalogSemanticRecoverySignal.cs` — coalesced wake-up for missing semantic collections/aliases plus periodic reconciliation.
- `src/Modules/Catalog/Catalog/Availability/ICatalogAvailabilitySource.cs` — live, timestamped health/connectivity observation port.
- `src/Modules/Catalog/Catalog/Availability/CatalogAvailabilityRegistry.cs` — owner-safe availability batching, precedence, and monotonic observation watermark.
- `src/Modules/Catalog/Catalog/Search/CatalogTokenizer.cs` — invariant lexical normalization.
- `src/Modules/Catalog/Catalog/Search/ILexicalCatalogIndex.cs` — provider-neutral lexical port.
- `src/Modules/Catalog/Catalog/Search/InMemoryLexicalCatalogIndex.cs` — deterministic inverted index rebuilt from metadata.
- `src/Modules/Catalog/Catalog/Search/ISemanticCatalogIndex.cs` — dense-index
  generation/upsert/delete/partition-enumeration/coverage/search port.
- `src/Modules/Catalog/Catalog/Search/InMemorySemanticCatalogIndex.cs` — deterministic simulation provider retaining scores/revisions.
- `src/Modules/Catalog/Catalog/Search/CatalogEmbeddingProfile.cs` — complete index-generation identity.
- `src/Modules/Catalog/Catalog/Search/SemanticCatalogSnapshot.cs` — active generation,
  deployment epoch, collection incarnation, projection epoch, projected watermark/snapshot
  fingerprint, readiness, and deterministic cursor token.
- `src/Modules/Catalog/Catalog/Search/CatalogEmbeddingService.cs` — safe document formatting and width-checked MEAI generation.
- `src/Modules/Catalog/Catalog/Search/CatalogCompatibility.cs` — structural hard filters.
- `src/Modules/Catalog/Catalog/Search/ReciprocalRankFusion.cs` — RRF with `k = 60`.
- `src/Modules/Catalog/Catalog/Search/DiscoveryCursorCodec.cs` — query/watermark-bound cursor encoding and validation.
- `src/Modules/Catalog/Catalog/Search/CatalogDiscoveryService.cs` — exact/lexical/semantic retrieval, hydration, ranking, paging, and degradation.
- `src/Modules/Catalog/Catalog/Grains/CatalogDirectoryGrain.cs` — owner derivation plus logic-free service delegation.
- `src/Modules/Catalog/Catalog/Grains/ICatalogDeploymentCoordinator.cs` — connection/alias-keyed,
  cluster-serialized reconciliation contract carrying exact deployment and snapshot intent.
- `src/Modules/Catalog/Catalog/Grains/CatalogDeploymentCoordinatorGrain.cs` — idempotent multi-silo
  Qdrant build/cutover coordinator with monotonic deployment fencing.
- `src/Modules/Catalog/Catalog/Grains/CatalogDeploymentPolicy.cs` — pure epoch/profile/manifest
  transition rules shared by tests and the coordinator.
- `src/Modules/Catalog/Catalog/Grains/CatalogDeploymentReconciler.cs` — provider-facing rebuild and
  cutover application service kept outside the Orleans grain shell.
- `src/Modules/Catalog/Catalog/Qdrant/IQdrantCatalogClient.cs` — narrow fakeable adapter boundary.
- `src/Modules/Catalog/Catalog/Qdrant/QdrantCatalogClient.cs` — Qdrant.Client translation and payload-index/alias operations.
- `src/Modules/Catalog/Catalog/Qdrant/QdrantCatalogIndex.cs` — `ISemanticCatalogIndex` implementation with scored results.
- `src/Modules/Catalog/Catalog/Qdrant/QdrantCatalogRegistration.cs` — connection-string/client construction and deterministic collection names.
- `src/Modules/Catalog/Catalog/Tools/CatalogToolSource.cs` — exactly the `discover` and `inspect` AIFunction adapters.
- `src/Modules/Catalog/Catalog/Inspection/{InspectionRouter,CatalogDescriptorInspectionProvider}.cs` — one extensible owner-bound inspection dispatch path; this slice implements only the catalog variant.

### Create — neutral kernel hook inventory

- `src/Kernel/DigitalBrain/Hosting/ConfiguredModuleHooks.cs` — immutable set of the exact hook
  instances selected by `ModuleManifest`; contains no Catalog vocabulary.

### Create — module manifests

- `src/Modules/Catalog/Catalog/CatalogContributionManifest.cs`.
- `src/Modules/AI/AI/AICatalogContribution.cs`.
- `src/Modules/Memory/Memory/MemoryCatalogContribution.cs`.
- `src/Modules/Time/Time/TimeCatalogContribution.cs`.
- `src/Modules/Execution/Execution/ExecutionCatalogContribution.cs`.
- `src/Modules/Google/Google/GoogleCatalogContribution.cs`.
- `src/Modules/Salesforce/Salesforce/SalesforceCatalogContribution.cs`.
- `src/Modules/UI/DigitalBrain.Modules.UI/UICatalogContribution.cs`.
- `src/Modules/Time/Contracts/Operations/{StartTimerOperationRequest,StartTimerOperationResult,CancelTimerOperationRequest,CancelTimerOperationResult}.cs` — transport-free operation schemas.
- `src/Modules/Memory/Contracts/Operations/{StoreVectorMemoryOperationRequest,StoreVectorMemoryOperationResult,SearchVectorMemoryOperationRequest,SearchVectorMemoryOperationResult,RemoveVectorMemoryOperationRequest,RemoveVectorMemoryOperationResult}.cs` — transport-free operation schemas.

Every current module contributes its module descriptor and explicitly describes every public neuron
contract and handled signal it owns. Time and Memory additionally contribute the first complete
capability and operation manifests, proving every descriptor kind needed by the next scripting
slice without misrepresenting unrelated legacy tool delegates as the future capability ABI.

### Create — AI embedding identity

- `src/Modules/AI/Contracts/LLM/SelectedEmbeddingProfile.cs` — selected provider/model/dimensions made explicit beside the generator.
- `src/Modules/AI/AI/Clients/EmbeddingModelSelection.cs` — one resolver shared by the generator factory and catalog profile.

### Create — Aspire integration

- `src/Aspire/DigitalBrain.Aspire.Hosting/Brain/QdrantHostingState.cs` — one shared Qdrant server projection per brain.
- `src/Aspire/DigitalBrain.Aspire.Hosting/Brain/DigitalBrainQdrantHostingExtensions.cs` — idempotent shared-resource request.
- `src/Modules/Catalog/Aspire.Hosting/DigitalBrain.Modules.Catalog.Aspire.Hosting.csproj`.
- `src/Modules/Catalog/Aspire.Hosting/CatalogHostingExtensions.cs` — catalog provider/profile environment projection.

### Create — tests

- `tests/DigitalBrain.Catalog.Tests/DigitalBrain.Catalog.Tests.csproj`.
- `tests/DigitalBrain.Catalog.Tests/CatalogFixtures.cs` — shared strongly typed test-object builders,
  extended task-by-task without production test hooks.
- `tests/DigitalBrain.Catalog.Tests/ContractShapeTests.cs`.
- `tests/DigitalBrain.Catalog.Tests/CatalogSchemaFingerprintTests.cs`.
- `tests/DigitalBrain.Catalog.Tests/Golden/schema-v1.canonical.json` — checked-in canonical schema
  hash fixture copied to test output.
- `tests/DigitalBrain.Catalog.Tests/CatalogContributionTests.cs`.
- `tests/DigitalBrain.Catalog.Tests/StaticCatalogSourceTests.cs`.
- `tests/DigitalBrain.Catalog.Tests/CatalogMetadataProjectionTests.cs`.
- `tests/DigitalBrain.Catalog.Tests/CatalogAvailabilityRegistryTests.cs`.
- `tests/DigitalBrain.Catalog.Tests/InMemoryLexicalCatalogIndexTests.cs`.
- `tests/DigitalBrain.Catalog.Tests/CatalogEmbeddingTests.cs`.
- `tests/DigitalBrain.Catalog.Tests/ReciprocalRankFusionTests.cs`.
- `tests/DigitalBrain.Catalog.Tests/CatalogDiscoveryServiceTests.cs`.
- `tests/DigitalBrain.Catalog.Tests/QdrantCatalogIndexTests.cs`.
- `tests/DigitalBrain.Catalog.Tests/CatalogDeploymentCoordinatorTests.cs`.
- `tests/DigitalBrain.Simulation.Tests/CatalogDeploymentCoordinatorClusterTests.cs`.
- `tests/DigitalBrain.Catalog.Tests/CatalogProjectionCoordinatorTests.cs`.
- `tests/DigitalBrain.Catalog.Tests/CatalogInitializationTests.cs`.
- `tests/DigitalBrain.Simulation.Tests/CatalogDiscoveryTests.cs`.
- `tests/DigitalBrain.Simulation.Tests/CatalogToolTests.cs`.
- `tests/DigitalBrain.Aspire.Tests/CatalogHostingTests.cs`.
- `tests/DigitalBrain.E2E.Tests/CatalogQdrantIntegrationTests.cs`.

### Modify

- `DigitalBrain.slnx` — add Catalog Contracts, SDK, Client, runtime, Aspire hosting, and tests projects.
- `src/Kernel/DigitalBrain/Hosting/DigitalBrainRuntime.cs` — instantiate configured hooks once and register `ModuleManifest` plus `ConfiguredModuleHooks`; it never references Catalog.
- `src/Modules/AI/{Contracts,AI}/*.csproj`, `AIClients.cs`, `AITestingClients.cs`, `AIModule.cs`, and `Assistant.cs` — selected embedding profile, module descriptor, and catalog-tool guidance.
- `src/Modules/AI/AI/{DigitalBrain.Modules.AI.csproj,AIModule.cs}`, `src/Modules/Memory/Memory/{DigitalBrain.Modules.Memory.csproj,MemoryModule.cs}`, `src/Modules/Time/Time/{DigitalBrain.Modules.Time.csproj,TimeModule.cs}`, `src/Modules/Execution/Execution/{DigitalBrain.Modules.Execution.csproj,ExecutionModule.cs}`, `src/Modules/Google/Google/{DigitalBrain.Modules.Google.csproj,GoogleModule.cs}`, `src/Modules/Salesforce/Salesforce/{DigitalBrain.Modules.Salesforce.csproj,SalesforceModule.cs}`, and `src/Modules/UI/DigitalBrain.Modules.UI/{DigitalBrain.Modules.UI.csproj,UIModule.cs}` — reference the SDK and implement `ICatalogContributor` through the focused manifest files above.
- `src/Modules/Memory/Aspire.Hosting/MemoryHostingExtensions.cs` — request the shared Qdrant resource instead of creating a private duplicate.
- `src/Modules/Memory/Memory/Qdrant/{QdrantVectorMemoryRegistration,QdrantVectorMemoryProvider}.cs` — expose one canonical public default collection-name constant for hosting configuration while preserving the existing value.
- `src/Aspire/DigitalBrain.Aspire.Hosting/DigitalBrain.Aspire.Hosting.csproj` — add existing centrally pinned `Aspire.Hosting.Qdrant`.
- `src/Aspire/DigitalBrain.AppHost/{DigitalBrain.AppHost.csproj,AppHost.cs}` — add Catalog module and hosting projection after AI.
- `src/Kernel/DigitalBrain.Silo/{DigitalBrain.Silo.csproj,Dockerfile,Properties/PublishProfiles/Container.pubxml}` — include Catalog assemblies and release manifest entry.
- `tests/DigitalBrain.Simulation.Tests/{DigitalBrain.Simulation.Tests.csproj,SimulationCollection.cs}` — include Catalog with deterministic in-memory indexes.
- `tests/DigitalBrain.Aspire.Tests/{DigitalBrain.Aspire.Tests.csproj,ReleaseModuleManifestConformanceTests.cs}` — include Catalog and shared-Qdrant conformance.
- `tests/DigitalBrain.E2E.Tests/DigitalBrain.E2E.Tests.csproj` — reference Catalog client/runtime/contracts for the real-provider vertical.
- `tests/DigitalBrain.E2E.Tests/E2ECollection.cs` — configure one fixture-unique Catalog Qdrant
  alias/prefix and expose those names to the serial integration test.
- `src/Testing/DigitalBrain.Testing.E2E/BrainAppHostFixture.cs` — add a generic optional-client
  connection seam over its already-connected `IGrainFactory`.

---

### Task 1: Add the Wire-Safe Catalog and Discovery Contracts

**Files:**

- Create: `src/Modules/Catalog/Contracts/DigitalBrain.Modules.Catalog.Contracts.csproj`
- Create: `src/Modules/Catalog/Contracts/{CatalogIdentity,CatalogDescriptor,CatalogOperationDescriptor,CatalogDiscovery,CatalogInspection,Inspection,CatalogProjection,CatalogContribution,ICatalogContributor,ICatalogSource,ICatalogDirectory,IInspectionProvider}.cs`
- Create: `tests/DigitalBrain.Catalog.Tests/DigitalBrain.Catalog.Tests.csproj`
- Create: `tests/DigitalBrain.Catalog.Tests/CatalogFixtures.cs`
- Create: `tests/DigitalBrain.Catalog.Tests/ContractShapeTests.cs`
- Modify: `DigitalBrain.slnx`

**Interfaces:**

- Consumes: `OwnerId`, Orleans serializer attributes, and `IGrainWithStringKey` from `DigitalBrain.Contracts`.
- Produces: exact catalog identities/descriptors, a stable multi-kind assistant inspection envelope,
  plus `ICatalogDirectory.Discover` and catalog-specific `Inspect`; every later task compiles against
  these names.

- [ ] **Step 1: Scaffold the contracts and test projects, add them to `DigitalBrain.slnx`, and write the failing shape test.**

  Use these project references:

  ```xml
  <!-- DigitalBrain.Modules.Catalog.Contracts.csproj -->
  <Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
      <IsPackable>true</IsPackable>
      <Description>Typed DigitalBrain self-knowledge, discovery, and inspection contracts.</Description>
      <RootNamespace>DigitalBrain.Catalog</RootNamespace>
    </PropertyGroup>
    <ItemGroup>
      <ProjectReference Include="../../../Kernel/DigitalBrain.Contracts/DigitalBrain.Contracts.csproj" />
    </ItemGroup>
  </Project>
  ```

  ```xml
  <!-- DigitalBrain.Catalog.Tests.csproj -->
  <Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
      <OutputType>Exe</OutputType>
      <IsPackable>false</IsPackable>
      <IsTestProject>true</IsTestProject>
      <UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>
      <TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport>
    </PropertyGroup>
    <ItemGroup>
      <PackageReference Include="xunit.v3.mtp-v2" />
    </ItemGroup>
    <ItemGroup>
      <ProjectReference Include="../../src/Modules/Catalog/Contracts/DigitalBrain.Modules.Catalog.Contracts.csproj" />
    </ItemGroup>
  </Project>
  ```

  Add assertions which require the intended safe shape:

  ```csharp
  [Fact]
  public void DiscoveryQueryCannotSupplyAnOwnerOrAnExecutableDelegate()
  {
      var names = typeof(DiscoveryQuery).GetProperties().Select(static property => property.Name).ToArray();

      Assert.DoesNotContain(nameof(OwnerId), names);
      Assert.DoesNotContain(names, static name => name.Contains("Delegate", StringComparison.Ordinal));
      Assert.DoesNotContain(names, static name => name.Contains("Handler", StringComparison.Ordinal));
  }

  [Fact]
  public void CandidateCarriesAnExactRevisionAndFingerprint()
  {
      var reference = new CatalogReference(
          CatalogScope.Platform,
          new CatalogSourceReference("platform-module", "module.time"),
          new CatalogEntryId("operation.time.timer.start"),
          "time@0.1.0",
          new CatalogFingerprint(new string('a', 64)));

      Assert.Equal(CatalogScopeKind.Platform, reference.Scope.Kind);
      Assert.Equal("platform-module", reference.Source.Kind);
      Assert.Equal("time@0.1.0", reference.SourceRevision);
      Assert.Equal(64, reference.Fingerprint.Value.Length);
  }

  [Fact]
  public void DirectoryExposesOnlyDiscoveryAndInspection()
      => Assert.Equal(
          [nameof(ICatalogDirectory.Discover), nameof(ICatalogDirectory.Inspect)],
          typeof(ICatalogDirectory).GetMethods().Select(static method => method.Name).Order().ToArray());

  [Fact]
  public void GeneralInspectionReferenceIsDiscriminatedAndOwnerIsNotAModelField()
  {
      var reference = InspectionReference.ForSynapse(
          CatalogFixtures.Neuron("source"), CatalogFixtures.Neuron("target"), "db.signal.note");

      Assert.Equal(InspectionReferenceKind.Synapse, reference.Kind);
      Assert.NotNull(reference.Synapse);
      Assert.Null(reference.Catalog);
      Assert.DoesNotContain(
          typeof(InspectionReference).GetProperties(),
          static property => property.PropertyType == typeof(OwnerId));
  }
  ```

- [ ] **Step 2: Run the focused test and confirm RED.**

  ```powershell
  dotnet restore DigitalBrain.slnx
  dotnet test tests/DigitalBrain.Catalog.Tests/DigitalBrain.Catalog.Tests.csproj -c Release --no-restore
  ```

  Expected: compilation fails because `DiscoveryQuery`, `CatalogReference`, and `ICatalogDirectory` do not exist.

- [ ] **Step 3: Implement the validated identity and descriptor contracts.**

  Use stable value objects and these exact principal records:

  ```csharp
  [GenerateSerializer]
  [Alias("db.catalog.entry-id")]
  public readonly record struct CatalogEntryId
  {
      [JsonConstructor]
      public CatalogEntryId(string value)
          => Value = string.IsNullOrWhiteSpace(value)
              ? throw new ArgumentException("A catalog entry id is required.", nameof(value))
              : value.Trim();

      [Id(0)]
      public string Value { get; }

      public override string ToString() => Value;
  }

  [GenerateSerializer]
  [Alias("db.catalog.reference")]
  public sealed record CatalogReference
  {
      [JsonConstructor]
      public CatalogReference(
          CatalogScope scope,
          CatalogSourceReference source,
          CatalogEntryId id,
          string sourceRevision,
          CatalogFingerprint fingerprint)
      {
          Scope = scope;
          Source = source;
          Id = id;
          SourceRevision = string.IsNullOrWhiteSpace(sourceRevision)
              ? throw new ArgumentException("A source revision is required.", nameof(sourceRevision))
              : sourceRevision.Trim();
          Fingerprint = fingerprint;
      }

      [Id(0)]
      public CatalogScope Scope { get; }

      [Id(1)]
      public CatalogSourceReference Source { get; }

      [Id(2)]
      public CatalogEntryId Id { get; }

      [Id(3)]
      public string SourceRevision { get; }

      [Id(4)]
      public CatalogFingerprint Fingerprint { get; }
  }

  [GenerateSerializer]
  [Alias("db.catalog.capability")]
  public sealed record CatalogCapabilityDescriptor(
      [property: Id(0)] string CapabilityId,
      [property: Id(1)] string Version);

  [GenerateSerializer]
  [Alias("db.catalog.schema")]
  public sealed record CatalogSchemaReference(
      [property: Id(0)] string SchemaId,
      [property: Id(1)] string Sha256,
      [property: Id(2)] string CanonicalJson,
      [property: Id(3)] int FormatVersion);

  [GenerateSerializer]
  [Alias("db.catalog.operation")]
  public sealed record CatalogOperationDescriptor(
      [property: Id(0)] string OperationId,
      [property: Id(1)] string Version,
      [property: Id(2)] string CapabilityId,
      [property: Id(3)] string CapabilityVersion,
      [property: Id(4)] CatalogSchemaReference Input,
      [property: Id(5)] CatalogSchemaReference Output,
      [property: Id(6)] CatalogRecoverySemantics Recovery,
      [property: Id(7)] string BindingId,
      [property: Id(8)] string BindingRevision,
      [property: Id(9)] IReadOnlyList<string> RequiredScopes);

  [GenerateSerializer]
  [Alias("db.catalog.signal-contract")]
  public sealed record CatalogSignalDescriptor(
      [property: Id(0)] string Alias,
      [property: Id(1)] CatalogSchemaReference Schema);

  [GenerateSerializer]
  [Alias("db.catalog.signal-reference")]
  public sealed record CatalogSignalReference(
      [property: Id(0)] string Alias,
      [property: Id(1)] string SchemaHash);

  [GenerateSerializer]
  [Alias("db.catalog.neuron")]
  public sealed record CatalogNeuronDescriptor(
      [property: Id(0)] string ContractAlias,
      [property: Id(1)] string GrainType,
      [property: Id(2)] IReadOnlyList<CatalogSignalReference> HandledSignals);

  [GenerateSerializer]
  [Alias("db.catalog.target-kind")]
  public enum CatalogTargetKind
  {
      Module,
      Capability,
      NeuronType,
      NeuronInstance,
      SignalContract,
      Operation,
      Script,
      Automation,
      AgentDefinition,
      Entity,
      Activity,
  }

  [GenerateSerializer]
  [Alias("db.catalog.typed-reference")]
  public sealed record CatalogTypedReference(
      [property: Id(0)] CatalogTargetKind Kind,
      [property: Id(1)] string? StableId,
      [property: Id(2)] NeuronId? Neuron,
      [property: Id(3)] EntityId? Entity,
      [property: Id(4)] DurableInspectionReference? Durable);

  [GenerateSerializer]
  [Alias("db.catalog.source-position")]
  public readonly record struct CatalogSourcePosition
  {
      [JsonConstructor]
      public CatalogSourcePosition(long epoch, long sequence)
      {
          ArgumentOutOfRangeException.ThrowIfNegative(epoch);
          ArgumentOutOfRangeException.ThrowIfNegative(sequence);
          Epoch = epoch;
          Sequence = sequence;
      }

      [Id(0)]
      public long Epoch { get; }

      [Id(1)]
      public long Sequence { get; }
  }

  [GenerateSerializer]
  [Alias("db.catalog.source-partition")]
  public sealed record CatalogSourcePartition(
      [property: Id(0)] string SourceKind,
      [property: Id(1)] string PartitionId,
      [property: Id(2)] CatalogScope Scope);

  [GenerateSerializer]
  [Alias("db.catalog.source-snapshot")]
  public sealed record CatalogSourceSnapshot(
      [property: Id(0)] CatalogSourcePartition Partition,
      [property: Id(1)] string SnapshotToken,
      [property: Id(2)] CatalogSourcePosition HighWatermark);

  [GenerateSerializer]
  [Alias("db.catalog.source-snapshot-item")]
  public sealed record CatalogSourceSnapshotItem(
      [property: Id(0)] CatalogSourcePosition Position,
      [property: Id(1)] CatalogDescriptor Descriptor);

  [GenerateSerializer]
  [Alias("db.catalog.source-snapshot-page")]
  public sealed record CatalogSourceSnapshotPage(
      [property: Id(0)] string SnapshotToken,
      [property: Id(1)] CatalogSourcePosition HighWatermark,
      [property: Id(2)] IReadOnlyList<CatalogSourceSnapshotItem> Items,
      [property: Id(3)] string? ContinuationToken);

  [GenerateSerializer]
  [Alias("db.catalog.mutation")]
  public sealed record CatalogMutation(
      [property: Id(0)] Guid MutationId,
      [property: Id(1)] CatalogSourcePartition Partition,
      [property: Id(2)] CatalogSourcePosition Position,
      [property: Id(3)] CatalogReference Reference,
      [property: Id(4)] CatalogMutationKind Kind,
      [property: Id(5)] CatalogDescriptor? Descriptor);

  [GenerateSerializer]
  [Alias("db.catalog.contribution")]
  public sealed record CatalogContribution(
      [property: Id(0)] string ModuleTypeName,
      [property: Id(1)] IReadOnlyList<CatalogDescriptor> Descriptors);

  public interface ICatalogContributor
  {
      CatalogContribution DescribeCatalog();
  }
  ```

  Validate non-negative source positions and non-empty snapshot/partition tokens.
  `CatalogSourcePosition.Origin == (0, 0)` is pre-history, never a mutation position; the first
  mutation is `(0, 1)`. A successor is either `(epoch, sequence + 1)` or
  `(epoch + 1, 1)` when a durable source starts a new epoch—sequence never returns to zero.
  A partition's
  scope and source kind must equal every descriptor/mutation it contains. Define
  `Upsert(Guid, CatalogSourcePartition, CatalogDescriptor, CatalogSourcePosition)` so the reference
  is derived from the descriptor, and
  `Tombstone(Guid, CatalogSourcePartition, CatalogReference, CatalogSourcePosition)` with no
  descriptor payload. Mutation IDs provide delivery idempotency while source position provides
  ordering. `CatalogSourceSnapshotPage.ContinuationToken == null` is the only successful terminal
  page; all pages must repeat both the token and high watermark returned by `BeginSnapshotAsync`.
  `CatalogScope.Platform` carries no owner; `CatalogScope.ForOwner(OwnerId)` requires one.
  `CatalogReference` validates non-empty revision and a lowercase 64-hex-character fingerprint.
  `CatalogEntryKind` must include `Module`, `Capability`, `NeuronType`, `NeuronInstance`,
  `SignalContract`, `Operation`, `Script`, `Automation`, `AgentDefinition`, `Entity`, and `Activity`.
  `CatalogDescriptor` must contain `CatalogReference` (whose handle includes scope and source),
  lifecycle, `CatalogVisibility`, canonical `CatalogConfigurationState`, name, summary, safe
  discovery text, a validated non-executable `CatalogTypedReference`, and optional neuron, signal,
  capability, and operation metadata.
  A signal-contract descriptor has `CatalogSignalDescriptor` with its stable alias and exact schema;
  a neuron descriptor has `CatalogNeuronDescriptor` with the stable contract/grain types and sorted
  handled-signal alias/hash references. Validating factories enforce exactly one target payload:
  stable platform ID for module/capability/neuron-type/signal/operation, `NeuronId` for a neuron
  instance, `EntityId` for an entity, or owner-local `DurableInspectionReference` for
  script/automation/agent/activity. The target kind must agree with the descriptor kind; a
  `NeuronId`/`EntityId` owner must agree with descriptor scope, while a durable resource's owner is
  intentionally implied by its trusted descriptor scope rather than repeated in model-visible data.
  A capability descriptor has `CatalogCapabilityDescriptor`; an operation has both capability and
  operation metadata. Exact schema documents live in operation metadata so `inspect` and later
  wrapper generation do not need to rediscover a CLR type. It must not contain observed
  availability, a vector, lease, credential, executable delegate, or provider client.

- [ ] **Step 4: Implement query, candidate, evidence, cursor, result, and exact inspection contracts.**

  Keep the request owner-free and make compatibility explicit:

  ```csharp
  [GenerateSerializer]
  [Alias("db.catalog.discovery-query")]
  public sealed record DiscoveryQuery(
      [property: Id(0)] string Text,
      [property: Id(1)] IReadOnlyList<CatalogEntryKind>? Kinds,
      [property: Id(2)] IReadOnlyList<string>? RequiredTags,
      [property: Id(3)] DiscoveryCompatibility? Compatibility,
      [property: Id(4)] CatalogAvailabilityRequirement Availability,
      [property: Id(5)] int Limit,
      [property: Id(6)] string? Cursor);

  [GenerateSerializer]
  [Alias("db.catalog.discovery-exact-match-kind")]
  public enum DiscoveryExactMatchKind
  {
      None,
      NameOrAlias,
      OperationOrCapabilityId,
      DescriptorId,
  }

  [Flags]
  [GenerateSerializer]
  [Alias("db.catalog.compatibility-evidence")]
  public enum DiscoveryCompatibilityEvidence
  {
      None = 0,
      Kind = 1 << 0,
      RequiredTag = 1 << 1,
      OperationOrCapability = 1 << 2,
      Signal = 1 << 3,
      InputSchema = 1 << 4,
      OutputSchema = 1 << 5,
      Lifecycle = 1 << 6,
      Invocability = 1 << 7,
      Configuration = 1 << 8,
  }

  [GenerateSerializer]
  [Alias("db.catalog.discovery-evidence")]
  public sealed record DiscoveryEvidence(
      [property: Id(0)] DiscoveryExactMatchKind ExactMatch,
      [property: Id(1)] DiscoveryCompatibilityEvidence Compatibility,
      [property: Id(2)] int? LexicalRank,
      [property: Id(3)] int? SemanticRank,
      [property: Id(4)] float? SemanticSimilarity,
      [property: Id(5)] IReadOnlyList<string> MatchedFields,
      [property: Id(6)] IReadOnlyList<string> RankReasons);

  [GenerateSerializer]
  [Alias("db.catalog.discovery-candidate")]
  public sealed record DiscoveryCandidate(
      [property: Id(0)] CatalogReference Reference,
      [property: Id(1)] CatalogEntryKind Kind,
      [property: Id(2)] string Name,
      [property: Id(3)] string Summary,
      [property: Id(4)] CatalogTypedReference Target,
      [property: Id(5)] CatalogLifecycle Lifecycle,
      [property: Id(6)] CatalogConfigurationState ConfigurationState,
      [property: Id(7)] CatalogAvailabilitySnapshot Availability,
      [property: Id(8)] int FinalRank,
      [property: Id(9)] double RrfScore,
      [property: Id(10)] DiscoveryEvidence Evidence);

  [Alias("db.catalog.directory")]
  public interface ICatalogDirectory : IGrainWithStringKey
  {
      [Alias(nameof(Discover))]
      Task<DiscoveryResult> Discover(
          DiscoveryQuery query,
          CancellationToken cancellationToken = default);

      [Alias(nameof(Inspect))]
      Task<CatalogInspection> Inspect(
          CatalogReference reference,
          CancellationToken cancellationToken = default);
  }
  ```

  `CatalogAvailabilityStatus` is `Unknown`, `Available`, `Degraded`, or `Unavailable`.
  `CatalogAvailabilitySnapshot` contains that status, observation time, and an optional bounded
  reason; it is response data rather than part of `CatalogDescriptor`.
  `CatalogAvailabilityRequirement` is `Any` or `CurrentlyAvailable`; only an explicit requirement
  hard-filters the observation, and `CurrentlyAvailable` accepts only `Available`.
  `CatalogVisibility` is `Discoverable` or `InspectOnly`: discovery excludes `InspectOnly` before
  every lane, while exact owner-visible inspection may return it. The authoritative current-entry
  view retains both, but its immutable discovery snapshot and exact-discovery view contain only
  `Discoverable` entries; lexical and semantic projections consume only that discovery snapshot.
  Truly secret/internal resources are not catalogued at all.
  Define catalog inspection statuses exactly as `Found`, `StaleDescriptor`, `Retired`, and
  `NotFound`. Define discovery statuses exactly as `Ready`, `SemanticDegraded`, `Initializing`, and
  `StaleCursor`. `DiscoveryDiagnostics` carries metadata and availability watermarks plus their
  snapshot tokens, semantic generation ID, nullable semantic snapshot token, candidate-pool
  truncation, and a bounded degradation reason. `DiscoveryResult.NextCursor` must be null unless
  status is `Ready` and the result carries matching metadata/semantic snapshot tokens plus a current
  availability snapshot token.
  Evidence lists are bounded, ordinal-sorted, and contain only stable field/reason codes. Enum
  numeric order is not the ranking implementation: `CatalogDiscoveryService` maps the explicit
  preference order given in Task 6. Structural specificity is the popcount of only the flags whose
  corresponding constraint was explicitly supplied and matched.

  Define the assistant-facing envelope in `Inspection.cs` without widening `ICatalogDirectory`:

  ```csharp
  [GenerateSerializer]
  [Alias("db.catalog.inspection-reference-kind")]
  public enum InspectionReferenceKind
  {
      CatalogDescriptor,
      Neuron,
      Synapse,
      Entity,
      DurableResource,
  }

  [GenerateSerializer]
  [Alias("db.catalog.synapse-reference")]
  public sealed record SynapseReference(
      [property: Id(0)] NeuronId Source,
      [property: Id(1)] NeuronId Target,
      [property: Id(2)] string SignalType);

  [GenerateSerializer]
  [Alias("db.catalog.durable-inspection-reference")]
  public sealed record DurableInspectionReference(
      [property: Id(0)] string ResourceKind,
      [property: Id(1)] string ResourceId,
      [property: Id(2)] string? Revision);

  [GenerateSerializer]
  [Alias("db.catalog.inspection-reference")]
  public sealed record InspectionReference(
      [property: Id(0)] InspectionReferenceKind Kind,
      [property: Id(1)] CatalogReference? Catalog,
      [property: Id(2)] NeuronId? Neuron,
      [property: Id(3)] SynapseReference? Synapse,
      [property: Id(4)] EntityId? Entity,
      [property: Id(5)] DurableInspectionReference? Durable);

  [GenerateSerializer]
  [Alias("db.catalog.inspection-status")]
  public enum InspectionStatus
  {
      Found,
      StaleReference,
      Retired,
      NotFound,
      UnsupportedReference,
  }

  [GenerateSerializer]
  [Alias("db.catalog.inspection-result")]
  public sealed record InspectionResult(
      [property: Id(0)] InspectionReference Reference,
      [property: Id(1)] InspectionStatus Status,
      [property: Id(2)] CatalogInspection? Catalog,
      [property: Id(3)] string? Reason);

  public readonly record struct InspectionProviderKey(
      InspectionReferenceKind Kind,
      string? DurableResourceKind);

  public interface IInspectionProvider
  {
      InspectionProviderKey Key { get; }

      Task<InspectionResult> InspectAsync(
          OwnerId owner,
          InspectionReference reference,
          CancellationToken cancellationToken);
  }
  ```

  `InspectionProviderKey.For(kind)` requires a non-durable kind and a null resource kind;
  `InspectionProviderKey.ForDurable(resourceKind)` trims and invariant-lowercases a required resource kind such
  as `script`, `automation`, `agent`, `run`, or `activity`. The router derives the same composite key
  from the validated reference; `DurableInspectionReference` applies that same normalization. This
  permits independent durable modules to register providers
  without competing for one catch-all `DurableResource` slot. `InspectionProviderKey` is a local DI
  key rather than an Orleans wire type, so it needs value validation but no serializer metadata.

  Use validating static factories (`ForCatalog`, `ForNeuron`, `ForSynapse`, `ForEntity`, and
  `ForDurableResource`) and a JSON constructor which requires exactly one non-null payload matching
  `Kind`. Validate non-empty signal/resource fields. The result reserves the established IDs;
  future typed neuron/synapse/entity/durable payload members are appended with new IDs and do not
  rename this tool contract. `CatalogInspectionStatus.StaleDescriptor` maps to general
  `InspectionStatus.StaleReference`. `UnsupportedReference` belongs only to the general router;
  it is not added to the catalog-specific status enum.

- [ ] **Step 5: Add reflection assertions for every stable alias and field ID, then run GREEN.**

  ```powershell
  dotnet test tests/DigitalBrain.Catalog.Tests/DigitalBrain.Catalog.Tests.csproj -c Release --no-restore
  dotnet build DigitalBrain.slnx -c Release
  dotnet test DigitalBrain.slnx -c Release --no-build --no-restore --verbosity minimal --max-parallel-test-modules 1
  ```

  Expected: all contract tests and the pre-existing solution suite pass.

- [ ] **Step 6: Commit the public vocabulary.**

  ```powershell
  git add DigitalBrain.slnx src/Modules/Catalog/Contracts tests/DigitalBrain.Catalog.Tests
  git commit -m "feat: add self-knowledge discovery contracts"
  ```

---

### Task 2: Build Typed Contributions and Reproducible Schema Fingerprints

**Files:**

- Create: `src/Modules/Catalog/Sdk/DigitalBrain.Modules.Catalog.Sdk.csproj`
- Create: `src/Modules/Catalog/Sdk/{CatalogContributionBuilder,CatalogSchemaFingerprint,CatalogDescriptorFingerprint}.cs`
- Create: `tests/DigitalBrain.Catalog.Tests/{CatalogSchemaFingerprintTests,CatalogContributionTests}.cs`
- Create: `tests/DigitalBrain.Catalog.Tests/Golden/schema-v1.canonical.json`
- Modify: `tests/DigitalBrain.Catalog.Tests/DigitalBrain.Catalog.Tests.csproj`
- Modify: `tests/DigitalBrain.Catalog.Tests/CatalogFixtures.cs`
- Modify: `DigitalBrain.slnx`

**Interfaces:**

- Consumes: Task 1 descriptor contracts and explicitly supplied CLR contract types.
- Produces: `CatalogContributionBuilder.For<TModule>()`, typed `AddCapability`, `AddNeuron`, `AddSignal`, and `AddOperation` methods, deterministic schema/fingerprint helpers, and the immutable contracts-layer `CatalogContribution`.

- [ ] **Step 1: Write failing tests for schema stability, schema change detection, and descriptor projection exclusion.**

  Add `Golden/schema-v1.canonical.json` as `CopyToOutputDirectory="PreserveNewest"` in the test
  project so the approved canonical schema is independent of test working directory.

  ```csharp
  [Fact]
  public void SameContractProducesSameCanonicalSchemaHash()
  {
      var first = CatalogSchemaFingerprint.For(typeof(SchemaV1));
      var second = CatalogSchemaFingerprint.For(typeof(SchemaV1));
      var golden = File.ReadAllText("Golden/schema-v1.canonical.json").TrimEnd('\r', '\n');
      var goldenHash = Convert.ToHexString(
          SHA256.HashData(Encoding.UTF8.GetBytes($"catalog-schema-v1\n{golden}")))
          .ToLowerInvariant();

      Assert.Equal(first, second);
      Assert.Equal(golden, first.CanonicalJson);
      Assert.Equal(goldenHash, first.Sha256);
  }

  [Fact]
  public void ContractShapeChangeChangesSchemaHash()
      => Assert.NotEqual(
          CatalogSchemaFingerprint.For(typeof(SchemaV1)).Sha256,
          CatalogSchemaFingerprint.For(typeof(SchemaV2)).Sha256);

  [Fact]
  public void AuthoritativeDescriptorMutationChangesFingerprint()
  {
      var descriptor = CatalogFixtures.OperationDescriptor();

      Assert.NotEqual(
          CatalogDescriptorFingerprint.Compute(descriptor),
          CatalogDescriptorFingerprint.Compute(descriptor with { Summary = "Changed purpose" }));
  }

  [Fact]
  public void SelfFingerprintAndProjectionFieldsDoNotAffectDescriptorFingerprint()
  {
      var descriptor = CatalogFixtures.OperationDescriptor();
      var changedEmbeddedFingerprint = descriptor with
      {
          Reference = new CatalogReference(
              descriptor.Reference.Scope,
              descriptor.Reference.Source,
              descriptor.Reference.Id,
              descriptor.Reference.SourceRevision,
              new CatalogFingerprint(new string('f', 64))),
      };

      Assert.Equal(
          CatalogDescriptorFingerprint.Compute(descriptor),
          CatalogDescriptorFingerprint.Compute(changedEmbeddedFingerprint));
      Assert.DoesNotContain("embedding", CatalogDescriptorFingerprint.CanonicalJson(descriptor),
          StringComparison.OrdinalIgnoreCase);
  }
  ```

- [ ] **Step 2: Run RED.**

  ```powershell
  dotnet restore DigitalBrain.slnx
  dotnet test tests/DigitalBrain.Catalog.Tests/DigitalBrain.Catalog.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~CatalogSchemaFingerprintTests|FullyQualifiedName~CatalogContributionTests"
  ```

  Expected: compilation fails because the SDK types do not exist.

- [ ] **Step 3: Export and canonicalize System.Text.Json schemas.**

  Use the .NET 11 API directly and recursively sort object keys before hashing:

  ```csharp
  var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
  var schema = options.GetJsonSchemaAsNode(contractType);
  var canonical = CanonicalJson.Write(schema);
  var versioned = $"catalog-schema-v1\n{canonical}";
  var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(versioned)))
      .ToLowerInvariant();
  return new CatalogSchemaReference(contractAlias, hash, canonical, FormatVersion: 1);
  ```

  `CanonicalJson.Write` must sort every `JsonObject` property with `StringComparer.Ordinal`, retain
  array order, emit numbers invariantly, and use compact UTF-8 JSON. Read the stable Orleans
  `[Alias]` from the explicitly supplied contract type and fail if it is absent.

  `CatalogDescriptorFingerprint` canonicalizes every authoritative descriptor field except
  `Reference.Fingerprint` itself. It also excludes all availability observations, indexes, scores,
  and checkpoints. `CatalogContributionBuilder.Build()` computes that hash first and then places it
  in the final `CatalogReference`; validation recomputes it and requires an exact match.

  Prefix canonical schema bytes with `catalog-schema-v1\n` for schema hashes and descriptor bytes
  with `catalog-descriptor-v1\n` for descriptor hashes. For static contributions, derive
  `SourceRevision` first as `static-v1:<sha256>` over the canonical authoritative descriptor payload
  excluding both source revision and fingerprint; then compute the final descriptor fingerprint,
  which includes that derived source revision. Any canonical-format change increments the prefix
  version and therefore creates new exact handles.

- [ ] **Step 4: Implement the fluent contribution builder without assembly scanning.**

  The public use must compile exactly in this shape:

  ```csharp
  public static CatalogContribution Contribution { get; } =
      CatalogContributionBuilder
          .For<TimeModule>(
              "module.time",
              "Time",
              "Durable timers and elapsed-time signals.")
          .AddCapability(
              "capability.time.timer",
              capabilityId: "time.timer",
              version: "1",
              name: "Timer control",
              summary: "Start and cancel durable owner timers.")
          .AddNeuron<ITimer, TimerNeuron>(
              "neuron.time.timer",
              "Timer",
              "Schedules, cancels, and durably recovers a named owner timer.")
          .AddSignal<StartTimer>(
              "signal.time.start-timer",
              "Start timer",
              "Requests a positive-duration named timer.")
          .AddOperation<StartTimerOperationRequest, StartTimerOperationResult>(
              operationId: "time.timer.start",
              version: "1",
              capabilityId: "time.timer",
              capabilityVersion: "1",
              bindingId: "time.timer.start",
              bindingRevision: "1",
              recovery: CatalogRecoverySemantics.Idempotent,
              summary: "Start a durable timer for the owner.",
              aliases: ["set timer", "remind me"],
              tags: ["time", "timer", "schedule"])
          .Build();
  ```

  `AddCapability` describes the stable capability/version grouping but never a lease or grant.
  `AddNeuron<TContract,TImplementation>` must require `TContract : INeuron`, verify the
  implementation is concrete and assignable, and derive the stable grain-type name. `AddSignal<T>`
  must require `T : Signal` and its stable alias. `AddOperation<TInput,TOutput>` must persist exact
  canonical schema JSON/IDs/hashes generated in Step 3 plus non-empty binding ID and binding
  revision. No method scans `AppDomain` or chooses the first matching interface.

- [ ] **Step 5: Reject duplicate IDs and invalid references in builder tests.**

  Assert duplicate descriptor IDs, capability IDs plus versions, operation IDs plus versions,
  blank aliases/tags, a neuron implementation that
  does not implement its declared contract, missing `[Alias]`, and duplicate aliases all throw an
  `InvalidOperationException` naming the conflicting module and value. Assert reordering aliases,
  keywords, tags, or required scopes produces the same descriptor fingerprint: the builder trims,
  ordinal-deduplicates, and ordinal-sorts those set-valued fields before hashing. Preserve the
  declared order only for explicitly ordered routing examples.

- [ ] **Step 6: Run focused/full GREEN and commit.**

  ```powershell
  dotnet test tests/DigitalBrain.Catalog.Tests/DigitalBrain.Catalog.Tests.csproj -c Release --no-restore
  dotnet build DigitalBrain.slnx -c Release
  dotnet test DigitalBrain.slnx -c Release --no-build --no-restore --verbosity minimal --max-parallel-test-modules 1
  git add DigitalBrain.slnx src/Modules/Catalog/Sdk tests/DigitalBrain.Catalog.Tests
  git commit -m "feat: add typed catalog contributions"
  ```

---

### Task 3: Make Configured Modules the Static Catalog Authority

**Files:**

- Create: `src/Modules/Catalog/Catalog/DigitalBrain.Modules.Catalog.csproj`
- Create: `src/Modules/Catalog/Catalog/{CatalogModule,CatalogRuntimeOptions}.cs`
- Create: `src/Modules/Catalog/Catalog/Sources/{StaticCatalogSource,CatalogSourceRegistry}.cs`
- Create: all eight focused `*CatalogContribution.cs` manifest files listed in the File Map.
- Create: `src/Modules/Time/Contracts/Operations/{StartTimerOperationRequest,StartTimerOperationResult,CancelTimerOperationRequest,CancelTimerOperationResult}.cs`
- Create: `src/Modules/Memory/Contracts/Operations/{StoreVectorMemoryOperationRequest,StoreVectorMemoryOperationResult,SearchVectorMemoryOperationRequest,SearchVectorMemoryOperationResult,RemoveVectorMemoryOperationRequest,RemoveVectorMemoryOperationResult}.cs`
- Create: `tests/DigitalBrain.Catalog.Tests/StaticCatalogSourceTests.cs`
- Modify: `tests/DigitalBrain.Catalog.Tests/CatalogFixtures.cs`
- Create: `src/Kernel/DigitalBrain/Hosting/ConfiguredModuleHooks.cs`
- Modify: `src/Kernel/DigitalBrain/Hosting/DigitalBrainRuntime.cs`
- Modify: `src/Modules/AI/AI/{DigitalBrain.Modules.AI.csproj,AIModule.cs}`
- Modify: `src/Modules/Memory/Memory/{DigitalBrain.Modules.Memory.csproj,MemoryModule.cs}`
- Modify: `src/Modules/Time/Time/{DigitalBrain.Modules.Time.csproj,TimeModule.cs}`
- Modify: `src/Modules/Execution/Execution/{DigitalBrain.Modules.Execution.csproj,ExecutionModule.cs}`
- Modify: `src/Modules/Google/Google/{DigitalBrain.Modules.Google.csproj,GoogleModule.cs}`
- Modify: `src/Modules/Salesforce/Salesforce/{DigitalBrain.Modules.Salesforce.csproj,SalesforceModule.cs}`
- Modify: `src/Modules/UI/DigitalBrain.Modules.UI/{DigitalBrain.Modules.UI.csproj,UIModule.cs}`
- Modify: `tests/DigitalBrain.Catalog.Tests/DigitalBrain.Catalog.Tests.csproj`
- Modify: `DigitalBrain.slnx`

**Interfaces:**

- Consumes: `ICatalogContributor`, typed SDK builders, and kernel-owned `ConfiguredModuleHooks`.
- Produces: `ConfiguredCatalogContributions`, `StaticCatalogSource`, and
  `CatalogSourceRegistry.ResolveExactAsync`/`ResolveCurrentAsync` plus partition snapshot/replay.

- [ ] **Step 1: Write failing configured-only and exact-resolution tests.**

  ```csharp
  [Fact]
  public void LoadedButUnconfiguredModuleIsAbsent()
  {
      var manifest = new ModuleManifest([typeof(CatalogModule), typeof(TimeModule)]);
      IModule[] hooks = [new CatalogModule(), new TimeModule()];
      var configuredHooks = new ConfiguredModuleHooks(manifest, hooks);
      var configured = ConfiguredCatalogContributions.Create(configuredHooks);

      Assert.Contains(configured.Items,
          item => item.ModuleTypeName == typeof(TimeModule).AssemblyQualifiedName);
      Assert.DoesNotContain(configured.Items,
          item => item.ModuleTypeName == typeof(UnconfiguredTestModule).AssemblyQualifiedName);
  }

  [Fact]
  public async Task ResolveRequiresExactRevisionAndFingerprint()
  {
      var source = CatalogFixtures.StaticSource();
      var cancellationToken = TestContext.Current.CancellationToken;
      var partitions = new List<CatalogSourcePartition>();
      await foreach (var partition in source.EnumeratePartitionsAsync(cancellationToken))
      {
          partitions.Add(partition);
      }

      var snapshot = await source.BeginSnapshotAsync(Assert.Single(partitions), cancellationToken);
      var page = await source.ReadSnapshotPageAsync(
          snapshot, continuationToken: null, pageSize: 100, cancellationToken);
      var descriptor = Assert.Single(page.Items).Descriptor;
      Assert.Null(page.ContinuationToken);
      var stale = new CatalogReference(
          descriptor.Reference.Scope,
          descriptor.Reference.Source,
          descriptor.Reference.Id,
          "another-revision",
          descriptor.Reference.Fingerprint);

      Assert.Null(await source.ResolveExactAsync(stale, TestContext.Current.CancellationToken));
      Assert.Equal(descriptor, await source.ResolveExactAsync(descriptor.Reference,
          TestContext.Current.CancellationToken));
      Assert.Equal(descriptor, await source.ResolveCurrentAsync(
          descriptor.Reference.Scope,
          descriptor.Reference.Source,
          descriptor.Reference.Id,
          TestContext.Current.CancellationToken));
  }
  ```

  Add a durable-source fake which retains immutable revision N after current advances to N+1.
  `ResolveExactAsync(N)` may still return the historical artifact, but discovery/inspection must
  first observe N+1 through `ResolveCurrentAsync` and treat N as stale.

- [ ] **Step 2: Run RED.**

  ```powershell
  dotnet restore DigitalBrain.slnx
  dotnet test tests/DigitalBrain.Catalog.Tests/DigitalBrain.Catalog.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~StaticCatalogSourceTests"
  ```

  Expected: compilation fails because the runtime project and configured contribution set do not exist.

- [ ] **Step 3: Instantiate configured hooks once without making Core depend on Catalog.**

  Refactor `DigitalBrainRuntime.Add` to this ordering:

  ```csharp
  var hooks = ModuleHooksOf(modules).ToArray();

  builder.Services.AddSingleton(modules);
  builder.Services.AddSingleton(new ConfiguredModuleHooks(modules, hooks));

  foreach (var hook in hooks)
  {
      hook.Configure(builder);
  }
  ```

  `ConfiguredModuleHooks` is a kernel type containing only `ModuleManifest` plus the instantiated
  `IModule` hooks. It validates one hook per manifest type and exposes no catalog vocabulary.
  `CatalogModule` registers `ConfiguredCatalogContributions.Create(configuredHooks)` and a startup
  validator which eagerly resolves it. That factory requires every configured hook to implement
  `ICatalogContributor`, verifies `contribution.ModuleTypeName` equals the hook's assembly-qualified
  type name, rejects duplicate
  module/descriptor/capability-version/operation-version identities, and never considers any type
  outside `configuredHooks.Manifest.Types`. It also exposes `DeploymentManifestFingerprint`, the
  lowercase SHA-256 of `catalog-deployment-manifest-v1` plus the ordered module type names and exact
  ordered descriptor references/fingerprints. This stable configured-authority identity is distinct
  from the full metadata-snapshot fingerprint, which later also includes dynamic owner partitions.

  Follow the repository's project-file convention by adding
  `<InternalsVisibleTo Include="DigitalBrain.Catalog.Tests" />` and
  `<InternalsVisibleTo Include="DigitalBrain.E2E.Tests" />` to
  `DigitalBrain.Modules.Catalog.csproj`. Do not expose Qdrant/projection implementation seams as
  public API merely so the test assemblies can exercise them.

  The runtime project explicitly references both `DigitalBrain.Modules.Catalog.Contracts` and
  `DigitalBrain.Modules.Catalog.Sdk`; it uses the SDK to author
  `CatalogContributionManifest`. This dependency is one-way—the SDK never references the runtime.

- [ ] **Step 4: Implement `StaticCatalogSource` and the source registry.**

  Use these exact application seams:

  ```csharp
  public interface ICatalogSource
  {
      string SourceKind { get; }

      IAsyncEnumerable<CatalogSourcePartition> EnumeratePartitionsAsync(
          CancellationToken cancellationToken);

      Task<CatalogSourceSnapshot> BeginSnapshotAsync(
          CatalogSourcePartition partition,
          CancellationToken cancellationToken);

      Task<CatalogSourceSnapshotPage> ReadSnapshotPageAsync(
          CatalogSourceSnapshot snapshot,
          string? continuationToken,
          int pageSize,
          CancellationToken cancellationToken);

      Task<CatalogSourcePosition> ReadCurrentPositionAsync(
          CatalogSourcePartition partition,
          CancellationToken cancellationToken);

      IAsyncEnumerable<CatalogMutation> ReadMutationsAsync(
          CatalogSourcePartition partition,
          CatalogSourcePosition afterExclusive,
          CatalogSourcePosition throughInclusive,
          CancellationToken cancellationToken);

      Task<CatalogDescriptor?> ResolveExactAsync(
          CatalogReference reference,
          CancellationToken cancellationToken);

      Task<CatalogDescriptor?> ResolveCurrentAsync(
          CatalogScope scope,
          CatalogSourceReference source,
          CatalogEntryId id,
          CancellationToken cancellationToken);
  }

  internal sealed class CatalogSourceRegistry(IEnumerable<ICatalogSource> sources)
  {
      public IAsyncEnumerable<CatalogSourcePartition> EnumeratePartitionsAsync(
          CancellationToken cancellationToken);

      public Task<CatalogSourceSnapshot> BeginSnapshotAsync(
          CatalogSourcePartition partition,
          CancellationToken cancellationToken);

      public Task<CatalogSourceSnapshotPage> ReadSnapshotPageAsync(
          CatalogSourceSnapshot snapshot,
          string? continuationToken,
          int pageSize,
          CancellationToken cancellationToken);

      public Task<CatalogSourcePosition> ReadCurrentPositionAsync(
          CatalogSourcePartition partition,
          CancellationToken cancellationToken);

      public IAsyncEnumerable<CatalogMutation> ReadMutationsAsync(
          CatalogSourcePartition partition,
          CatalogSourcePosition afterExclusive,
          CatalogSourcePosition throughInclusive,
          CancellationToken cancellationToken);

      public Task<CatalogDescriptor?> ResolveExactAsync(
          CatalogReference reference,
          CancellationToken cancellationToken);

      public Task<CatalogDescriptor?> ResolveCurrentAsync(
          CatalogScope scope,
          CatalogSourceReference source,
          CatalogEntryId id,
          CancellationToken cancellationToken);
  }
  ```

  Register `StaticCatalogSource` as the `platform-module` source with the single partition
  `(platform-module, configured-modules, Platform)`. Its immutable snapshot token is the ordered
  contribution-set fingerprint; it returns deterministic item positions, a terminal page, the same
  current position, and an empty mutation range. The registry can enumerate every platform and
  owner partition for rebuild but routes each snapshot/replay call directly by
  `CatalogSourcePartition.SourceKind`. Exact/current resolution routes by
  `CatalogReference.Source.Kind`; a missing source kind is `NotFound`. Each source verifies its
  stable source ID as well as scope and descriptor ID.

  Across one snapshot, every page must echo the original opaque snapshot token and high watermark;
  only a terminal page (`ContinuationToken == null`) marks the partition complete. Then capture
  `ReadCurrentPositionAsync` and consume the ordered mutation stream after the snapshot watermark
  through that inclusive barrier. Require consecutive source positions and exact partition matches.
  A compacted range, gap, changed token, or expired snapshot throws the typed
  `CatalogSourceSnapshotRequiredException`; callers discard that partition's partial work and begin
  again. Discovery never calls partition enumeration, so the all-owner rebuild seam cannot become a
  model-controlled cross-owner query.
  Add a fake durable source whose empty snapshot ends at `Origin`, then publishes its first mutation
  at `(0, 1)` before `ReadCurrentPositionAsync`; prove catch-up includes it. Also reject `(0, 0)`, a
  skipped sequence, and an epoch transition whose first sequence is not `1`.

- [ ] **Step 5: Add one pure contribution per configured module.**

  Make each module class implement `ICatalogContributor` by forwarding only:

  ```csharp
  public CatalogContribution DescribeCatalog() => TimeCatalogContribution.Value;
  ```

  Use stable module IDs `module.catalog`, `module.ai`, `module.memory`, `module.time`,
  `module.execution`, `module.google`, `module.salesforce`, and `module.ui`. Explicitly contribute:

  ```text
  AI:        IAssistant neuron type, identified as an IAgent contract
  Memory:    IVectorMemory plus StoreVectorMemory, SearchVectorMemory, RemoveVectorMemory
  Time:      ITimer plus StartTimer, CancelTimer
  Execution: IExecution plus StartExecution, CancelExecution
  UI:        IChat and IUIRenderer plus ReadTranscriptRequest, Note, KitCardOffer,
             OpenSurface, ControlActivated
  ```

  Describe only public neuron contracts and signal contracts, not internal helper grains such as
  chat-turn workers or activation bootstraps. Add capability `time.timer@1` with
  `time.timer.start@1`/`time.timer.cancel@1`, and capability `memory.vector@1` with
  `memory.vector.store@1`/`memory.vector.search@1`/`memory.vector.remove@1`. Google and Salesforce
  contribute module descriptors now; their legacy tool delegates are not misrepresented as the
  future durable capability ABI.

  The operation schemas use the new `*OperationRequest`/`*OperationResult` DTOs, never transport
  signals. In particular, operation input excludes `CommandId`; later `invoke` derives command and
  idempotency metadata from its trusted invocation context, then an exact binding adapter constructs
  `StartTimer`/`CancelTimer` or vector-memory signals. Mark these operation manifests `Declared`
  until that binding ships, and give them stable binding IDs/revisions now. Do not advertise a
  legacy `IAgentToolSource` delegate as the operation binding.

- [ ] **Step 6: Add conformance assertions for missing/duplicate module descriptions.**

  Assert the release module types each implement `ICatalogContributor`, each contribution's module
  assembly-qualified type name is exact, descriptor IDs are globally unique, capability and operation IDs plus versions are
  unique, and all descriptor fingerprints remain deterministic across two builds of the
  contribution set and that its `DeploymentManifestFingerprint` changes when a configured module,
  descriptor revision, or descriptor fingerprint changes. Assert the explicit expected set of public neuron/signal aliases above is
  complete. This conformance test may inspect the named module assemblies; production catalog
  construction still performs no assembly scan.

- [ ] **Step 7: Run focused/full GREEN and commit.**

  ```powershell
  dotnet test tests/DigitalBrain.Catalog.Tests/DigitalBrain.Catalog.Tests.csproj -c Release --no-restore
  dotnet build DigitalBrain.slnx -c Release
  dotnet test DigitalBrain.slnx -c Release --no-build --no-restore --verbosity minimal --max-parallel-test-modules 1
  git add -- DigitalBrain.slnx src/Kernel/DigitalBrain/Hosting/ConfiguredModuleHooks.cs src/Kernel/DigitalBrain/Hosting/DigitalBrainRuntime.cs src/Modules/Catalog/Catalog tests/DigitalBrain.Catalog.Tests
  git add -- src/Modules/AI/AI/DigitalBrain.Modules.AI.csproj src/Modules/AI/AI/AIModule.cs src/Modules/AI/AI/AICatalogContribution.cs
  git add -- src/Modules/Memory/Memory/DigitalBrain.Modules.Memory.csproj src/Modules/Memory/Memory/MemoryModule.cs src/Modules/Memory/Memory/MemoryCatalogContribution.cs src/Modules/Memory/Contracts/Operations
  git add -- src/Modules/Time/Time/DigitalBrain.Modules.Time.csproj src/Modules/Time/Time/TimeModule.cs src/Modules/Time/Time/TimeCatalogContribution.cs src/Modules/Time/Contracts/Operations
  git add -- src/Modules/Execution/Execution/DigitalBrain.Modules.Execution.csproj src/Modules/Execution/Execution/ExecutionModule.cs src/Modules/Execution/Execution/ExecutionCatalogContribution.cs
  git add -- src/Modules/Google/Google/DigitalBrain.Modules.Google.csproj src/Modules/Google/Google/GoogleModule.cs src/Modules/Google/Google/GoogleCatalogContribution.cs
  git add -- src/Modules/Salesforce/Salesforce/DigitalBrain.Modules.Salesforce.csproj src/Modules/Salesforce/Salesforce/SalesforceModule.cs src/Modules/Salesforce/Salesforce/SalesforceCatalogContribution.cs
  git add -- src/Modules/UI/DigitalBrain.Modules.UI/DigitalBrain.Modules.UI.csproj src/Modules/UI/DigitalBrain.Modules.UI/UIModule.cs src/Modules/UI/DigitalBrain.Modules.UI/UICatalogContribution.cs
  git diff --cached --check
  git diff --cached --name-only
  git commit -m "feat: describe configured modules in catalog"
  ```

---

### Task 4: Add the Current-Revision Gate and Deterministic Lexical Index

**Files:**

- Create: `src/Modules/Catalog/Catalog/Projection/{CatalogDocument,CatalogMetadataProjection}.cs`
- Create: `src/Modules/Catalog/Catalog/Availability/{ICatalogAvailabilitySource,CatalogAvailabilityRegistry}.cs`
- Create: `src/Modules/Catalog/Catalog/Search/{CatalogTokenizer,ILexicalCatalogIndex,InMemoryLexicalCatalogIndex}.cs`
- Create: `tests/DigitalBrain.Catalog.Tests/{CatalogMetadataProjectionTests,CatalogAvailabilityRegistryTests,InMemoryLexicalCatalogIndexTests}.cs`
- Modify: `tests/DigitalBrain.Catalog.Tests/CatalogFixtures.cs`
- Modify: `src/Modules/Catalog/Catalog/CatalogModule.cs`

**Interfaces:**

- Consumes: canonical descriptors and `CatalogMutation` with lexicographically ordered
  `CatalogSourcePosition(Epoch, Sequence)` comparable only inside its explicit
  `CatalogSourcePartition`.
- Produces: owner-safe current descriptor snapshots, exact lookup, deterministic lexical ranks,
  tombstones, metadata watermark, and a separately versioned live-availability overlay.

- [ ] **Step 1: Write failing projection-ordering and isolation tests.**

  ```csharp
  [Fact]
  public void OlderMutationCannotResurrectATombstone()
  {
      var projection = new CatalogMetadataProjection();
      var descriptor = CatalogFixtures.OwnerOperation("owner-a", revision: "2");
      var older = CatalogFixtures.OwnerOperation("owner-a", revision: "1");
      var partition = CatalogFixtures.OwnerPartition("owner-a", "definitions");

      projection.Apply(CatalogMutation.Upsert(
          Guid.NewGuid(), partition, descriptor,
          new CatalogSourcePosition(epoch: 4, sequence: 2)));
      projection.Apply(CatalogMutation.Tombstone(
          Guid.NewGuid(), partition, descriptor.Reference,
          new CatalogSourcePosition(epoch: 4, sequence: 3)));
      projection.Apply(CatalogMutation.Upsert(
          Guid.NewGuid(), partition, older,
          new CatalogSourcePosition(epoch: 3, sequence: 99)));

      Assert.Null(projection.FindCurrent(new OwnerId("owner-a"), descriptor.Reference.Id));
  }

  [Fact]
  public void OwnerQueryIncludesPlatformAndCurrentOwnerOnly()
  {
      var projection = CatalogFixtures.ProjectionWithPlatformAndTwoOwners();
      var visible = projection.VisibleTo(new OwnerId("owner-a"));

      Assert.Contains(visible, static item => item.Scope.Kind == CatalogScopeKind.Platform);
      Assert.Contains(visible, static item => item.Scope.Owner?.Value == "owner-a");
      Assert.DoesNotContain(visible, static item => item.Scope.Owner?.Value == "owner-b");
  }
  ```

- [ ] **Step 2: Add same-position idempotency/conflict and new-epoch reset tests.**

  Prove a repeated mutation ID is a no-op only when canonical bytes and source position are
  identical; reusing it with different bytes or position throws `CatalogProjectionConflictException`.
  Prove a different payload at the same position faults, a lower epoch cannot overwrite a higher
  epoch, a new epoch begins at sequence one, and positions from different partitions are never
  compared as one stream. A different source/partition claiming an existing scoped descriptor ID
  faults. Persist/rebuild checkpoints keyed by the complete partition identity. For static data,
  build two complete snapshots and atomically replace
  the first with the second; an entry absent from the second disappears even when the second is
  reconstructed into a fresh projection, so restart needs no in-memory tombstone inventory.

- [ ] **Step 3: Write failing availability-overlay tests.**

  Register two fake `ICatalogAvailabilitySource` implementations and assert platform/current-owner
  visibility, deterministic source precedence, one batched observation time, and a monotonic
  availability watermark. Assert changing availability never changes `CatalogDescriptorFingerprint`
  and never writes a metadata mutation. An empty registry returns `Unknown` at watermark zero.

- [ ] **Step 4: Write failing lexical ranking tests.**

  Require exact token matching and stable field weights:

  ```csharp
  [Fact]
  public async Task NameAndAliasRankAheadOfSummaryOnly()
  {
      var index = CatalogFixtures.LexicalIndex(
          CatalogFixtures.Descriptor("a", name: "Timer", aliases: ["remind me"], summary: "time"),
          CatalogFixtures.Descriptor("b", name: "Scheduler", aliases: [], summary: "timer reminder"));

      var hits = await index.SearchAsync(
          new OwnerId("owner-a"),
          new CatalogLexicalQuery("remind me", CandidateLimit: 8),
          TestContext.Current.CancellationToken);

      Assert.Equal("a", hits[0].Key.EntryId.Value);
      Assert.Contains("Aliases", hits[0].MatchedFields);
  }
  ```

- [ ] **Step 5: Run RED.**

  ```powershell
  dotnet test tests/DigitalBrain.Catalog.Tests/DigitalBrain.Catalog.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~CatalogMetadataProjectionTests|FullyQualifiedName~CatalogAvailabilityRegistryTests|FullyQualifiedName~InMemoryLexicalCatalogIndexTests"
  ```

- [ ] **Step 6: Implement idempotent metadata application and tombstones.**

  Key state by `(CatalogScope, CatalogEntryId)`. Store the last `CatalogSourcePosition`, mutation
  ID with canonical mutation hash, exact reference/source, descriptor, and tombstone flag. Compare `(Epoch, Sequence)`
  lexicographically. Equal mutation IDs or byte-identical same-position payloads are successful
  no-ops only when all canonical mutation fields agree; any reuse conflict faults. Older positions
  are ignored. Bind a scoped descriptor ID to one `CatalogSourceReference`; a different claimant is
  an integrity fault. Increment the projection watermark only for a newly applied state change.
  Static platform contributions use `BuildStaticSnapshot` followed by one atomic
  `ReplaceStaticSnapshot`; readers observe the old or new immutable snapshot, never a partial mix.
  The projection retains every current non-tombstoned entry for exact current-pointer lookup and
  inspection. Its separately captured immutable discovery snapshot contains only `Discoverable`
  entries and is the sole input to exact discovery, lexical rebuild, semantic documents, and
  semantic coverage. Every immutable discovery snapshot carries a lowercase SHA-256 fingerprint
  over the prefix `catalog-metadata-snapshot-v1` followed by tuples containing scope, source,
  descriptor ID, source revision, and descriptor fingerprint in ordinal scope-key, descriptor-ID,
  then source order. Tests prove insertion order does not affect the snapshot fingerprint, any
  discoverable authoritative descriptor replacement does, and adding or changing an `InspectOnly`
  entry cannot put it into a discovery lane or semantic coverage.

- [ ] **Step 7: Implement the non-authoritative availability registry.**

  `CatalogAvailabilityRegistry.ObserveAsync(owner, references, cancellationToken)` batches sources,
  ignores observations outside platform/current-owner scope, and returns
  `CatalogAvailabilityBatch(Watermark, SnapshotToken, ObservedAt, Items)`. The registry creates one
  random incarnation ID at process construction. `SnapshotToken` is lowercase SHA-256 over
  `catalog-availability-snapshot-v1`, that incarnation, the watermark, and effective observations
  ordered by exact catalog reference, status, bounded reason, and source precedence. Source
  precedence is an explicit ordinal registration value; equal-precedence conflicting observations fail startup. The watermark
  advances only when an effective status/reason/precedence value changes—not merely because
  `ObservedAt` advances—and timestamps never participate in sorting. Store no observation in
  `CatalogDescriptor`, metadata, lexical documents, or semantic payloads. A source timeout/failure
  yields bounded `Unknown` observations and never makes exact/lexical discovery fail. Reconstruct
  the same effective observations in a fresh registry, force the same numeric watermark, and prove
  its different incarnation produces a different snapshot token and stales the old cursor.

- [ ] **Step 8: Implement invariant tokenization and the lexical inverted index.**

  Normalize with Unicode Form KC plus `ToLowerInvariant`, split at every non-letter/digit rune,
  discard empty tokens, and keep ordinal distinct terms. Index fields with these integer weights:

  ```csharp
  private const int IdentityWeight = 16;   // descriptor/operation id, name, aliases
  private const int ContractWeight = 8;    // capability ids, schema ids, signal alias
  private const int IntentWeight = 4;      // routing examples, tags, keywords
  private const int SummaryWeight = 1;     // summary and use/not-use text
  ```

  Rank by descending total matched weight, then descending matched distinct query tokens, then
  ordinal canonical scope key and `CatalogEntryId`. Return ordered hits with raw weight and matched
  field names; `CatalogDiscoveryService` assigns the one-based lexical rank after every hard filter.
  Rebuild only from the metadata projection's current immutable `Discoverable` snapshot, never its
  broader current-entry view used by inspection.

- [ ] **Step 9: Prove user vector memory cannot enter this index.**

  Add a reference-boundary test asserting Catalog runtime does not reference
  `DigitalBrain.Modules.Memory` and that no Catalog project type implements or consumes
  `IVectorMemoryStore`. The shared dependency is only MEAI/Qdrant infrastructure.

- [ ] **Step 10: Run focused/full GREEN and commit.**

  ```powershell
  dotnet test tests/DigitalBrain.Catalog.Tests/DigitalBrain.Catalog.Tests.csproj -c Release --no-restore
  dotnet build DigitalBrain.slnx -c Release
  dotnet test DigitalBrain.slnx -c Release --no-build --no-restore --verbosity minimal --max-parallel-test-modules 1
  git add src/Modules/Catalog tests/DigitalBrain.Catalog.Tests
  git commit -m "feat: add catalog metadata and lexical indexes"
  ```

---

### Task 5: Pin Embedding Identity and Add the Semantic Projection Port

**Files:**

- Create: `src/Modules/AI/Contracts/LLM/SelectedEmbeddingProfile.cs`
- Create: `src/Modules/AI/AI/Clients/EmbeddingModelSelection.cs`
- Create: `src/Modules/Catalog/Catalog/Search/{ISemanticCatalogIndex,InMemorySemanticCatalogIndex,CatalogEmbeddingProfile,CatalogEmbeddingService,SemanticCatalogSnapshot}.cs`
- Create: `src/Modules/Catalog/Catalog/Projection/CatalogProjectionCoordinator.cs`
- Create: `src/Modules/Catalog/Catalog/Initialization/{CatalogReadiness,CatalogInitializationHostedService,CatalogSemanticRecoverySignal}.cs`
- Create: `tests/DigitalBrain.Catalog.Tests/{CatalogEmbeddingTests,CatalogProjectionCoordinatorTests,CatalogInitializationTests}.cs`
- Modify: `tests/DigitalBrain.Catalog.Tests/CatalogFixtures.cs`
- Modify: `src/Modules/AI/AI/Clients/AIClients.cs`
- Modify: `src/Modules/AI/AI/Testing/AITestingClients.cs`
- Modify: `src/Modules/Catalog/Catalog/CatalogModule.cs`

**Interfaces:**

- Consumes: the selected MEAI generator, canonical descriptors, metadata watermark, and semantic-index port.
- Produces: explicit model/profile identity, fixed discovery documents, dimension-checked embeddings,
  idempotent semantic records, atomic local snapshots, and explicit rebuild readiness.

- [ ] **Step 1: Write failing profile, width, and document-safety tests.**

  ```csharp
  [Fact]
  public void AnyEmbeddingInputChangeChangesGeneration()
  {
      var original = CatalogEmbeddingProfile.Create("openai", "text-embedding-3-small", "2026-09", 1536, 1, 1);

      Assert.NotEqual(original.GenerationId,
          CatalogEmbeddingProfile.Create("openai", "text-embedding-3-large", "2026-09", 1536, 1, 1).GenerationId);
      Assert.NotEqual(original.GenerationId,
          CatalogEmbeddingProfile.Create("openai", "text-embedding-3-small", "2026-10", 1536, 1, 1).GenerationId);
      Assert.NotEqual(original.GenerationId,
          CatalogEmbeddingProfile.Create("openai", "text-embedding-3-small", "2026-09", 3072, 1, 1).GenerationId);
      Assert.NotEqual(original.GenerationId,
          CatalogEmbeddingProfile.Create("openai", "text-embedding-3-small", "2026-09", 1536, 1, 2).GenerationId);
  }

  [Fact]
  public async Task WrongVectorWidthFailsBeforeIndexWrite()
  {
      var service = CatalogFixtures.EmbeddingService(
          vector: [1f, 2f], expectedDimensions: 3);

      await Assert.ThrowsAsync<InvalidOperationException>(() => service.EmbedAsync(
          CatalogFixtures.OperationDescriptor(), TestContext.Current.CancellationToken));
  }
  ```

  Also assert `CatalogEmbeddingService.FormatDocument` contains safe name/summary/aliases/tags and
  excludes a fixture's protected payload, raw source, binding delegate, and owner credentials. Keep
  the service pure: it formats and generates/validates vectors but does not depend on an index. In
  `CatalogProjectionCoordinatorTests`, compose that failing service with a
  `RecordingSemanticCatalogIndex` and assert the coordinator performs no upsert after the width
  exception.

- [ ] **Step 2: Run RED.**

  ```powershell
  dotnet test tests/DigitalBrain.Catalog.Tests/DigitalBrain.Catalog.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~CatalogEmbeddingTests|FullyQualifiedName~CatalogProjectionCoordinatorTests|FullyQualifiedName~CatalogInitializationTests"
  ```

- [ ] **Step 3: Make AI register the exact selected embedding model beside its generator.**

  Add:

  ```csharp
  public sealed record SelectedEmbeddingProfile(
      string ProviderId,
      string ModelId,
      int Dimensions);
  ```

  Move the current default-marker/fallback choice into `EmbeddingModelSelection.Resolve`. Both
  `AIClients.DefaultEmbeddingGenerator` and the `SelectedEmbeddingProfile` factory must consume the
  same selected `EmbeddingModel`; do not implement the selection twice. Testing mode registers
  `new SelectedEmbeddingProfile("test", "deterministic", 2)` beside its two-dimensional generator.

- [ ] **Step 4: Implement complete catalog generation identity.**

  `CatalogEmbeddingProfile.Create` combines the selected provider/model with configuration key
  `DigitalBrain:Catalog:Embedding:ModelRevision` (default `provider-managed-v1`), dimensions,
  preprocessing version `1`, and document format version `1`. Hash canonical UTF-8 fields to a
  lowercase 16-character generation ID. `CatalogRuntimeOptions` validates
  `DigitalBrain:Catalog:Qdrant:CollectionPrefix` (default `digitalbrain_catalog`) and
  `DigitalBrain:Catalog:Qdrant:ActiveAlias` (default `digitalbrain_catalog_active`), plus positive
  `DigitalBrain:Catalog:DeploymentEpoch` (development/testing default `1`). The epoch is not part of
  embedding identity: all silos in a rollout use the same value, and operators increment it for
  every selected-profile or configured-static-manifest change; rollback uses another higher value.
  Outside Testing mode the key is required—there is no implicit production rollout epoch.
  The physical
  collection is `<prefix>_<generationId>` and reads use the configured stable alias; deployment
  naming does not alter semantic generation identity. Because the deployment coordinator carries
  them across an Orleans call, `CatalogEmbeddingProfile` and `SemanticCatalogSnapshot` use
  `[GenerateSerializer]`, stable `db.catalog.*` aliases, and explicit member IDs even though they
  remain internal implementation contracts. Freeze their wire shape as:

  ```csharp
  [GenerateSerializer]
  [Alias("db.catalog.embedding-profile")]
  internal sealed record CatalogEmbeddingProfile(
      [property: Id(0)] string ProviderId,
      [property: Id(1)] string ModelId,
      [property: Id(2)] string ModelRevision,
      [property: Id(3)] int Dimensions,
      [property: Id(4)] int PreprocessingVersion,
      [property: Id(5)] int DiscoveryDocumentFormatVersion,
      [property: Id(6)] string GenerationId);

  [GenerateSerializer]
  [Alias("db.catalog.semantic-readiness")]
  internal enum SemanticCatalogReadiness { Updating = 0, Ready = 1 }

  [GenerateSerializer]
  [Alias("db.catalog.semantic-snapshot")]
  internal sealed record SemanticCatalogSnapshot(
      [property: Id(0)] string GenerationId,
      [property: Id(1)] long DeploymentEpoch,
      [property: Id(2)] string DeploymentManifestFingerprint,
      [property: Id(3)] string PhysicalCollection,
      [property: Id(4)] string CollectionIncarnation,
      [property: Id(5)] long ProjectionEpoch,
      [property: Id(6)] long ProjectedMetadataWatermark,
      [property: Id(7)] string MetadataSnapshotFingerprint,
      [property: Id(8)] SemanticCatalogReadiness Readiness,
      [property: Id(9)] string Token);
  ```

  Add a serializer-manifest/round-trip test for both records and, in Task 8, for the complete
  `CatalogDeploymentIntent`/`CatalogDeploymentResult` object graph. Every nested custom type in that
  graph must carry serializer metadata; no fallback JSON codec hides a missing Orleans codec.

- [ ] **Step 5: Implement the semantic port and deterministic in-memory provider.**

  ```csharp
  internal interface ISemanticCatalogIndex
  {
      Task<SemanticCatalogUpdate> BeginUpdateAsync(
          CatalogEmbeddingProfile profile, long deploymentEpoch,
          string deploymentManifestFingerprint,
          long targetMetadataWatermark,
          string metadataSnapshotFingerprint,
          CancellationToken cancellationToken);
      Task UpsertAsync(CatalogEmbeddingProfile profile, SemanticCatalogUpdate update,
          SemanticCatalogDocument document,
          ReadOnlyMemory<float> embedding, CancellationToken cancellationToken);
      Task DeleteAsync(CatalogEmbeddingProfile profile, SemanticCatalogUpdate update,
          CatalogDocumentKey key,
          CancellationToken cancellationToken);
      IAsyncEnumerable<SemanticCatalogRecord> EnumeratePartitionAsync(
          CatalogEmbeddingProfile profile, SemanticCatalogUpdate update,
          CatalogSourcePartition partition,
          CancellationToken cancellationToken);
      Task<SemanticCatalogSnapshot> ReadActiveSnapshotAsync(
          CatalogEmbeddingProfile profile, CancellationToken cancellationToken);
      Task<SemanticCatalogSearchResult> SearchAsync(CatalogEmbeddingProfile profile,
          SemanticCatalogQuery query, ReadOnlyMemory<float> queryEmbedding,
          CancellationToken cancellationToken);
      Task<SemanticCatalogSnapshot> CommitUpdateAsync(
          CatalogEmbeddingProfile profile, SemanticCatalogUpdate update,
          CatalogSemanticCoverage expectedCoverage, bool activate,
          CancellationToken cancellationToken);
  }
  ```

  `SemanticCatalogUpdate` identifies a deterministic update ID, positive deployment epoch, exact
  configured-deployment-manifest fingerprint, physical collection, collection incarnation,
  monotonically increasing projection epoch, target metadata watermark, and exact
  metadata-snapshot fingerprint. `BeginUpdateAsync` is idempotent for that
  complete update identity and makes the
  collection's control record `Updating` before any descriptor point changes. A complete validated
  write calls `CommitUpdateAsync`, which publishes a `Ready` `SemanticCatalogSnapshot`; `activate`
  additionally swaps the read alias for a new model generation. An abandoned `Updating` record is
  recoverable work, never a readable snapshot; a retry resumes the same update rather than advancing
  the epoch again.

  `SemanticCatalogSnapshot.Token` is lowercase SHA-256 over version-prefixed canonical fields:
  generation ID, deployment epoch, deployment-manifest fingerprint, physical collection,
  collection incarnation, projection epoch, projected metadata watermark, and
  metadata-snapshot fingerprint. Search reads the control snapshot immediately before
  and after the provider query and returns hits only when both reads identify the same `Ready` token.
  A missing, updating,
  or changed snapshot raises a typed transient semantic-snapshot exception so discovery degrades
  instead of mixing two semantic states.

  `CatalogDocumentKey` is exactly `(CatalogScope Scope, CatalogEntryId EntryId)`; revision,
  fingerprint, source, and partition are payload, never key identity. `SemanticCatalogDocument`
  carries that key plus `CatalogSourcePartition`, indexed `CatalogSourcePosition`, source revision,
  descriptor fingerprint, and the safe discovery text. `SemanticCatalogHit` carries the same stable
  `CatalogDocumentKey`, indexed `CatalogSourcePosition`, source
  revision, descriptor fingerprint, and raw similarity. `SemanticCatalogSearchResult` carries the
  hits plus the exact ready snapshot observed unchanged before and after search. Its hit list
  is normalized provider-neutrally by descending similarity, then canonical scope key and entry ID
  ordinal for exact-score ties; the service assigns contiguous one-based rank only after all hard
  filters. The in-memory implementation uses cosine only for deterministic tests, applies
  platform/current owner plus coarse kind filters, and models the same update/snapshot state machine.

  `SemanticCatalogRecord` contains key, partition, source position, source revision, and descriptor
  fingerprint but no vector. `EnumeratePartitionAsync` pages every `record_type=descriptor` record
  in the update's physical collection for the exact partition and is the only prune/coverage read
  seam. `CatalogSemanticCoverage` is the ordinal list keyed by complete `CatalogSourcePartition`
  identity (`SourceKind`, `PartitionId`, and `Scope`), never opaque `PartitionId` alone, with each
  expected `Discoverable` record count and lowercase SHA-256 over its ordered
  key/revision/fingerprint tuples. `InspectOnly` entries contribute neither a point nor coverage.
  `CommitUpdateAsync` re-enumerates and recomputes those values itself; it refuses `Ready`/activation
  if any actual partition differs. This makes stale-point pruning and the readiness proof part of
  the semantic-port contract instead of Qdrant-specific downcasting.

  `ReadActiveSnapshotAsync` validates the active alias/control record even when a query has no dense
  lane, and requires its `GenerationId` to equal `profile.GenerationId`. `SearchAsync` repeats that
  generation check before submitting the provider query. A different but otherwise ready generation
  raises typed `SemanticGenerationMismatchException`; it is never queried with an embedding from the
  selected profile, even when dimensions match. Thus any resumable exact/lexical page is bound to
  the semantic state which could affect a later recomputation.

- [ ] **Step 6: Implement idempotent static rebuild coordination.**

  Enumerate `CatalogSourceRegistry` partition snapshots into a validated complete catalog snapshot.
  For each partition, consume every page under one snapshot token, capture its current position, and
  replay the gap-free mutations after the snapshot watermark through that inclusive barrier; restart
  only that partition on `CatalogSourceSnapshotRequiredException`. Retain the complete current-entry
  view for exact inspection, then derive a `Discoverable`-only immutable discovery snapshot. Build
  its exact and lexical views off to the side and atomically publish them together. Format and embed
  every descriptor in that discovery snapshot—and no `InspectOnly` descriptor—inside one
  `SemanticCatalogUpdate`. Build the desired stable-point-ID set from discoverable entries for every
  partition whose source snapshot and catch-up completed, scroll that partition's existing
  descriptor records, and delete IDs absent from the desired discoverable set before readiness.
  Never prune a partition which was not completely enumerated and caught up. Verify the remaining
  descriptor point set and `CatalogSemanticCoverage` exactly match the published discoverable
  metadata snapshot fingerprint/watermark, then call
  `CommitUpdateAsync` with the computed `CatalogSemanticCoverage`. Catch embedding/provider
  failures, record semantic degradation, and leave exact/lexical state ready. Never call a provider
  from a source aggregate or grain transition. A recording-provider test proves an `InspectOnly`
  descriptor is retained for exact inspection while causing no lexical document, embedding call,
  semantic upsert, or semantic-coverage entry.

- [ ] **Step 7: Implement startup readiness and atomic replacement.**

  `CatalogInitializationHostedService.StartAsync` enumerates and validates the complete configured
  static catalog, builds metadata/exact/lexical state off to the side, atomically publishes one
  immutable snapshot, then marks local discovery ready. It attempts semantic projection; failure
  records `SemanticDegraded`, completes startup, and a cancellation-aware background loop retries
  with bounded backoff. After success, that loop remains alive at a configurable low-frequency
  reconciliation interval (`DigitalBrain:Catalog:Semantic:ReconciliationInterval`, default one
  minute, validated from five seconds through one hour) and can be woken immediately through a coalescing
  `CatalogSemanticRecoverySignal`. `StopAsync` cancels and awaits the loop; no unobserved retry task
  may survive host shutdown.

  `CatalogReadiness` exposes `Initializing`, `Ready`, and `SemanticDegraded` snapshots. A direct
  grain call before atomic publication returns `DiscoveryStatus.Initializing` with no candidates;
  it never sees a partially built snapshot. Concurrent discovery during a later rebuild sees the
  complete old or complete new snapshot. Tests pause rebuild between construction/publication and
  assert this behavior, then run two local initializer calls and prove idempotent publication.
  With a fake semantic provider, also remove the active generation after readiness, signal
  recovery, and prove the loop reconciles it without republishing or partially exposing metadata.
  Add a cold-restart case whose existing same-generation semantic state at deployment epoch 41
  contains a static descriptor removed from the new complete contribution snapshot at epoch 42;
  prove that point is deleted before the new control snapshot becomes `Ready`. A same-epoch static
  manifest change is configuration conflict, not an implicit replacement.

- [ ] **Step 8: Run focused/full GREEN and commit.**

  ```powershell
  dotnet test tests/DigitalBrain.Catalog.Tests/DigitalBrain.Catalog.Tests.csproj -c Release --no-restore
  dotnet build DigitalBrain.slnx -c Release
  dotnet test DigitalBrain.slnx -c Release --no-build --no-restore --verbosity minimal --max-parallel-test-modules 1
  git add src/Modules/AI src/Modules/Catalog tests/DigitalBrain.Catalog.Tests
  git commit -m "feat: add versioned catalog semantic projection"
  ```

---

### Task 6: Return Compatible Hydrated Candidates with RRF Evidence

**Files:**

- Create: `src/Modules/Catalog/Catalog/Search/{CatalogCompatibility,ReciprocalRankFusion,DiscoveryCursorCodec,CatalogDiscoveryService}.cs`
- Create: `tests/DigitalBrain.Catalog.Tests/{ReciprocalRankFusionTests,CatalogDiscoveryServiceTests}.cs`
- Modify: `tests/DigitalBrain.Catalog.Tests/CatalogFixtures.cs`
- Modify: `src/Modules/Catalog/Catalog/CatalogModule.cs`

**Interfaces:**

- Consumes: one immutable metadata/lexical snapshot, semantic index, embedding service, source
  resolver, one availability batch/snapshot token, query constraints, and the active semantic
  snapshot.
- Produces: owner-safe `DiscoveryResult` and exact `CatalogInspection`; it has no dependency on execution, signal delivery, leases, or synapse mutation.

- [ ] **Step 1: Write failing RRF tests proving raw scores are irrelevant.**

  ```csharp
  [Fact]
  public void FusionUsesRanksNotProviderScores()
  {
      var first = ReciprocalRankFusion.Score(lexicalRank: 1, semanticRank: 2);
      var second = ReciprocalRankFusion.Score(lexicalRank: 2, semanticRank: 1);

      Assert.Equal(first, second, precision: 12);
      Assert.Equal((1d / 61d) + (1d / 62d), first, precision: 12);
  }
  ```

- [ ] **Step 2: Write failing compatibility, exact-dominance, stale-hydration, and no-action tests.**

  ```csharp
  [Fact]
  public async Task ExactAliasWinsButDoesNotExecute()
  {
      var service = CatalogFixtures.DiscoveryService(
          CatalogFixtures.Descriptor("exact", aliases: ["review pull request"]),
          CatalogFixtures.Descriptor("semantic", summary: "review a pull request deeply"));

      var result = await service.DiscoverAsync(
          new OwnerId("owner-a"),
          CatalogFixtures.Query("review pull request"),
          TestContext.Current.CancellationToken);

      Assert.Equal("exact", result.Candidates[0].Reference.Id.Value);
      Assert.Equal(DiscoveryExactMatchKind.NameOrAlias,
          result.Candidates[0].Evidence.ExactMatch);
  }

  [Fact]
  public void DiscoveryServiceDependsOnlyOnCatalogReadPorts()
  {
      var parameters = Assert.Single(typeof(CatalogDiscoveryService).GetConstructors(
              System.Reflection.BindingFlags.Instance |
              System.Reflection.BindingFlags.Public |
              System.Reflection.BindingFlags.NonPublic))
          .GetParameters().Select(static parameter => parameter.ParameterType).ToArray();

      Assert.Equal(
          [typeof(CatalogReadiness), typeof(CatalogMetadataProjection),
           typeof(ILexicalCatalogIndex), typeof(ISemanticCatalogIndex),
           typeof(CatalogEmbeddingService), typeof(CatalogSourceRegistry),
           typeof(CatalogAvailabilityRegistry), typeof(CatalogSemanticRecoverySignal),
           typeof(DiscoveryCursorCodec)],
          parameters);
  }

  [Fact]
  public async Task StaleSemanticHitIsDroppedAfterExactHydration()
  {
      var service = CatalogFixtures.DiscoveryServiceWithStaleSemanticHit();

      var result = await service.DiscoverAsync(
          new OwnerId("owner-a"), CatalogFixtures.Query("timer"),
          TestContext.Current.CancellationToken);

      Assert.DoesNotContain(result.Candidates,
          static candidate => candidate.Reference.SourceRevision == "retired-revision");
  }
  ```

  Add parameterized cases for owner visibility, lifecycle, allowed kinds, required tag,
  capability/version, signal alias/schema hash, input/output schema, configuration state, and an
  explicitly requested `CurrentlyAvailable` filter. Prove `InspectOnly` never enters a discovery
  lane but remains exactly inspectable, and a live availability change does not change the exact
  descriptor handle.
  Seed stale, incompatible, and unavailable semantic hits in positions 1 through N and prove the
  first surviving candidate receives semantic rank 1 and the same RRF score as a clean lane.
  Feed two equal-similarity semantic hits in opposite adapter orders and prove both executions assign
  identical semantic ranks/final ranks using canonical scope key and descriptor ID as the tie-break.

- [ ] **Step 3: Run RED.**

  ```powershell
  dotnet test tests/DigitalBrain.Catalog.Tests/DigitalBrain.Catalog.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~ReciprocalRankFusionTests|FullyQualifiedName~CatalogDiscoveryServiceTests"
  ```

- [ ] **Step 4: Implement the search pipeline in the approved order.**

  Stable descriptor/operation/capability IDs use trimmed ordinal equality. Human names and aliases
  use whole-value Unicode Form KC plus invariant-case equality; substring matches remain lexical,
  never exact.

  `CatalogDiscoveryService.DiscoverAsync` must perform exactly:

  ```text
  validate query/limit/cursor
  -> capture one immutable metadata snapshot and its watermark/fingerprint
  -> obtain discoverable platform + current-owner structurally compatible metadata set from it
  -> exact ID/operation/name/alias lane
  -> lexical lane
  -> read and validate active semantic generation against the selected embedding profile
  -> semantic lane using CandidatePoolLimit, or record degradation
  -> require semantic projected watermark/fingerprint == captured metadata snapshot
  -> intersect every lane with compatible metadata
  -> current-resolve every candidate and compare source revision + fingerprint
  -> exact-resolve details only for candidates whose supplied handle is still current
  -> capture one availability batch and apply any explicit availability requirement
  -> preserve exact/lexical order and normalized semantic score/key order; reassign contiguous one-based ranks to survivors
  -> exact match class first: stable ID, operation/capability ID, name/alias, then none
  -> descending structural match count, lifecycle, configuration, and availability priority
  -> descending RRF(k=60) for lexical/semantic ranks
  -> ordinal canonical scope key, then CatalogEntryId tie-break
  -> assign one-based FinalRank
  -> page/cursor + evidence + metadata/availability/semantic snapshot tokens
  ```

  Similarity is copied to evidence but never used as an authority or added to lexical score.
  `DiscoveryEvidence` records the exact-match class and a bit for each explicitly supplied
  compatibility facet. Structural match count is the popcount of those bits; absent query facets do
  not contribute. `RrfScore` contains only the RRF value. Exact class and priorities are separate
  sort-tuple fields; `FinalRank` is assigned after that deterministic ordering.

  Define the total tuple exactly, with enum values preferred in the listed order: exact class (`DescriptorId`, `OperationOrCapabilityId`,
  `NameOrAlias`, `None`), descending structural-match count, lifecycle (`Active`, `Draft`,
  `Suspended`, `Retired`), configuration (`Configured`, `Declared`, `Disabled`), availability
  (`Available`, `Degraded`, `Unknown`, `Unavailable`), descending RRF score, then
  `CatalogScope.SortKey` and descriptor ID ordinal. Scope sort keys are `0:platform` and
  `1:owner:<owner-id>`. Platform and owner entries with the same descriptor ID coexist; neither
  silently shadows the other, and ambiguity remains visible.

  Compute one fixed `CandidatePoolLimit = min(512, max(64, requestedLimit * 8))` after validating
  `1 <= requestedLimit <= 50`; use it for lexical and semantic lanes. `DiscoveryResult.Diagnostics`
  reports `CandidatePoolTruncated` when a lane fills the bound. This is a bounded candidate search,
  not an assertion that every possible semantic neighbor was exhaustively enumerated.

  Normalize semantic provider hits by descending similarity, then canonical scope key and descriptor
  ID for exact-score ties before assigning lane ranks; never rely on Qdrant's tie order. When dense
  search runs, use the unchanged `SemanticCatalogSnapshot` returned beside its hits. If
  the query intentionally has no dense lane, call `ReadActiveSnapshotAsync` before issuing a cursor.
  Before generating/submitting a dense query, require the active snapshot's generation to equal the
  selected `CatalogEmbeddingProfile.GenerationId`; a mismatch degrades, signals reconciliation, and
  performs no provider query. In both cases, require its projected metadata watermark and snapshot fingerprint to equal the
  captured metadata snapshot. A mismatch discards all semantic evidence, marks the result degraded,
  wakes reconciliation, and suppresses pagination; never reuse a cached startup token as proof of
  current provider state.

- [ ] **Step 5: Implement exact inspection separately from search.**

  `InspectAsync(owner, reference)` checks scope visibility, routes by source kind, then calls
  `CatalogSourceRegistry.ResolveCurrentAsync` for the same scope/source/ID. No current item is
  `NotFound`; a current reference with another revision is `StaleDescriptor` with no replacement
  handle. The same revision with another fingerprint, or any impossible scope/source/ID identity,
  is a tampered/fabricated `NotFound`, never a hint about canonical state. Only when the current
  pointer equals every supplied handle field may it call
  `ResolveExactAsync` to hydrate immutable details and return `Found` with current live availability.
  This ordering prevents a source which retains historical revision N after publishing N+1 from
  reporting N as current. An exact current retired descriptor is `Retired`.
  Live `Unavailable` or `Unknown` remains observation data on `Found`; only `invoke` later decides
  whether execution is usable. Do not call the embedding service, lexical index, or semantic index.

- [ ] **Step 6: Implement query-bound stable cursors.**

  Encode base64url canonical JSON containing query fingerprint, metadata watermark/fingerprint,
  availability watermark/snapshot token, active semantic generation ID, semantic snapshot token,
  the last candidate's global `FinalRank`, and the complete last sort
  tuple: exact-match class, structural specificity, lifecycle priority, configuration priority,
  availability priority, RRF score, and scoped descriptor ID. The snapshot token is the lowercase
  SHA-256 of generation ID, deployment epoch, deployment-manifest fingerprint, physical collection,
  collection incarnation, projection epoch,
  projected metadata watermark, and metadata-snapshot fingerprint. The query fingerprint covers
  the trusted owner plus every normalized query field after applying effective defaults, except the cursor, preventing
  cross-owner reuse. On decode, reject malformed data and recompute the bounded result set; the
  recorded last tuple must exist exactly at the encoded global final rank, so a fabricated/tampered tuple
  becomes `StaleCursor`. If the query fingerprint, either metadata field, either availability field,
  semantic generation, or semantic snapshot token differs, return `DiscoveryStatus.StaleCursor` and no candidates. A valid
  cursor reruns deterministic ranking against those snapshots and resumes after the exact tuple.
  `SemanticDegraded` and `Initializing` results always set `NextCursor = null`; presenting any
  cursor while the semantic snapshot is not ready returns `StaleCursor`.

- [ ] **Step 7: Prove graceful degradation and ambiguity.**

  When a first-page query embedding or semantic search throws, return exact/lexical candidates with
  `DiscoveryStatus.SemanticDegraded`, no next cursor, and a bounded non-sensitive reason. A request
  which supplied a cursor returns `StaleCursor` instead, because its previous semantic snapshot can
  no longer be validated. A typed
  missing-collection or missing-alias failure also sets `CatalogReadiness` to semantic-degraded and
  wakes `CatalogSemanticRecoverySignal`; an arbitrary provider error degrades this request but uses
  ordinary bounded retry rather than being misclassified as data loss. If the top two candidates
  have equal ordering fields through RRF and differ only by scoped ID, return both; do not
  synthesize a winner or invocation.

  Add cursor tests which (a) advance projected metadata within the same embedding generation and
  (b) replace a deleted collection with the same physical name but a new incarnation. In both
  cases, the old cursor must return `StaleCursor` even when its last tuple still happens to occupy
  the same rank. Prove the degraded response has no next cursor and that a cursor created before
  loss never becomes valid again after recovery.
  Pause semantic update after atomically publishing a different exact metadata snapshot with a
  deliberately reused numeric watermark; prove snapshot-fingerprint inequality discards semantic
  hits, returns degradation/no cursor, and wakes reconciliation. Recreate an availability registry
  with different effective state but the same numeric watermark and prove the old availability
  token/cursor is stale.
  Add a page-two case where the prior last tuple still exists but moves to another global rank and
  prove the cursor is stale rather than skipping or duplicating a candidate. Configure an old and a
  new embedding profile with equal dimensions but different generation IDs, point the alias at the
  old ready control record, and prove discovery does not call `QueryAsync`, degrades with no cursor,
  and wakes reconciliation.
  Use the retained-history source from Task 3 for both discovery hydration and inspection: after its
  current pointer advances to N+1, an N descriptor which `ResolveExactAsync` can still load must be
  dropped from discovery and returned as `StaleDescriptor` by inspection.

- [ ] **Step 8: Run focused/full GREEN and commit.**

  ```powershell
  dotnet test tests/DigitalBrain.Catalog.Tests/DigitalBrain.Catalog.Tests.csproj -c Release --no-restore
  dotnet build DigitalBrain.slnx -c Release
  dotnet test DigitalBrain.slnx -c Release --no-build --no-restore --verbosity minimal --max-parallel-test-modules 1
  git add src/Modules/Catalog tests/DigitalBrain.Catalog.Tests
  git commit -m "feat: rank compatible catalog candidates"
  ```

---

### Task 7: Expose Owner-Scoped Discover and Inspect to Clients and the Assistant

**Files:**

- Create: `src/Modules/Catalog/Catalog/Grains/CatalogDirectoryGrain.cs`
- Create: `src/Modules/Catalog/Catalog/Tools/CatalogToolSource.cs`
- Create: `src/Modules/Catalog/Catalog/Inspection/{InspectionRouter,CatalogDescriptorInspectionProvider}.cs`
- Create: `src/Modules/Catalog/Client/{DigitalBrain.Modules.Catalog.Client.csproj,IDigitalBrainCatalog.cs,DigitalBrainCatalogClient.cs}`
- Create: `tests/DigitalBrain.Catalog.Tests/{CatalogToolSourceTests,InspectionRouterTests}.cs`
- Create: `tests/DigitalBrain.Simulation.Tests/{CatalogDiscoveryTests,CatalogToolTests,SimulationOwnerCatalogSource}.cs`
- Modify: `src/Modules/Catalog/Catalog/{DigitalBrain.Modules.Catalog.csproj,CatalogModule.cs}`
- Modify: `src/Modules/AI/AI/Assistant.cs`
- Modify: `tests/DigitalBrain.Simulation.Tests/{DigitalBrain.Simulation.Tests.csproj,SimulationCollection.cs}`
- Modify: `DigitalBrain.slnx`

**Interfaces:**

- Consumes: `CatalogDiscoveryService`, `ICatalogDirectory`, composite-keyed `IInspectionProvider`, `IGrainFactory`,
  and `IAgentToolSource`.
- Produces: optional `IDigitalBrainCatalog.DiscoverAsync`/`InspectAsync` and assistant functions named
  exactly `discover` and `inspect`; the client remains catalog-specific while the assistant's
  `inspect` request is multi-kind and forward-compatible. Kernel `IDigitalBrain` is unchanged.

- [ ] **Step 1: Write failing simulation tests for implicit owner scope and logic-free inspection.**

  ```csharp
  [Fact]
  public async Task FacadeSearchesPlatformPlusItsOwnOverlayOnly()
  {
      var ownerA = DigitalBrainCatalogClient.Connect(
          fixture.Sim.Grains, fixture.OwnerA.Value);
      var ownerB = DigitalBrainCatalogClient.Connect(
          fixture.Sim.Grains, fixture.OwnerB.Value);
      var cancellationToken = TestContext.Current.CancellationToken;
      var query = new DiscoveryQuery(
          "timer", Kinds: null, RequiredTags: null, Compatibility: null,
          Availability: CatalogAvailabilityRequirement.Any, Limit: 10, Cursor: null);

      var a = await ownerA.DiscoverAsync(query, cancellationToken);
      var b = await ownerB.DiscoverAsync(query, cancellationToken);

      Assert.Contains(a.Candidates, static candidate => candidate.Reference.Id.Value == "neuron.time.timer");
      Assert.Contains(b.Candidates, static candidate => candidate.Reference.Id.Value == "neuron.time.timer");
      Assert.All(a.Candidates, candidate => Assert.True(
          candidate.Reference.Scope.Kind == CatalogScopeKind.Platform ||
          candidate.Reference.Scope.Owner == ownerA.Owner));
      Assert.DoesNotContain(a.Candidates,
          candidate => candidate.Reference.Scope.Owner == ownerB.Owner);
  }

  [Fact]
  public async Task InspectRejectsFabricatedFingerprint()
  {
      var catalog = DigitalBrainCatalogClient.Connect(
          fixture.Sim.Grains, DigitalBrainNames.DefaultOwner);
      var result = await catalog.DiscoverAsync(
          new DiscoveryQuery(
              "timer", Kinds: null, RequiredTags: null, Compatibility: null,
              Availability: CatalogAvailabilityRequirement.Any, Limit: 10, Cursor: null),
          TestContext.Current.CancellationToken);
      var discovered = result.Candidates[0].Reference;
      var reference = new CatalogReference(
          discovered.Scope,
          discovered.Source,
          discovered.Id,
          discovered.SourceRevision,
          new CatalogFingerprint(new string('0', 64)));

      var inspection = await catalog.InspectAsync(reference,
          TestContext.Current.CancellationToken);

      Assert.Equal(CatalogInspectionStatus.NotFound, inspection.Status);
  }
  ```

  `SimulationCollection` defines fixed `OwnerA`/`OwnerB` before cluster startup and uses
  `BrainSimulationOptions.ConfigureSilo` to register `SimulationOwnerCatalogSource`, whose complete
  snapshots contain one descriptor for each owner. Registration occurs before hosted-service
  startup; the fixture also adds `CatalogModule` after `AIModule` and uses the testing in-memory
  semantic provider, so the initial atomic catalog snapshot includes both partitions. The test asserts each
  facade sees its own descriptor plus platform entries and never the other's scope; it does not rely
  on a nonexistent post-start projection hook.
  Add Catalog contracts/client/runtime project references to `DigitalBrain.Simulation.Tests.csproj`.

- [ ] **Step 2: Write failing tool tests proving owner closure and candidate-only behavior.**

  In `DigitalBrain.Catalog.Tests`, compose `CatalogToolSource` with focused fake read ports (the
  Catalog runtime already grants this test assembly internals access), resolve it as
  `IAgentToolSource`, assert its tool names are exactly `discover` and
  `inspect`, invoke `discover`, and assert the serialized response contains revisioned candidates
  only. The exact constructor-boundary test in Task 6 proves the discovery service cannot receive an
  execution/signal/lease/synapse dependency. Assert neither function accepts an `owner` parameter.
  Inspect two provisional catalog candidates before making any
  selection and prove both resolve through the exact catalog provider. Invoke `inspect` with a
  valid neuron reference and assert the stable response is `UnsupportedReference` in this slice,
  rather than a fabricated neuron or another tool. Assert the generated `inspect` schema contains
  every `InspectionReferenceKind` discriminant and that a reference whose embedded neuron/entity
  owner differs from the trusted tool owner is refused before provider dispatch.
  In `InspectionRouterTests`, register `DurableResource/script` and `DurableResource/automation`
  providers together and prove each receives only its resource kind; duplicate `script` registration
  fails startup while the two distinct durable keys do not conflict.

  In simulation `CatalogToolTests`, follow the existing `AgentToolTests` pattern with a capturing
  `IChatClient`: start a dedicated simulation containing UI + AI + Catalog, capture `ChatOptions.Tools`,
  invoke the captured `discover`/`inspect` functions, and verify their owner-scoped results. This is
  the black-box registration check and requires no silo-service-provider escape hatch.

- [ ] **Step 3: Run RED.**

  ```powershell
  dotnet restore DigitalBrain.slnx
  dotnet test tests/DigitalBrain.Catalog.Tests/DigitalBrain.Catalog.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~CatalogToolSourceTests|FullyQualifiedName~InspectionRouterTests"
  dotnet test tests/DigitalBrain.Simulation.Tests/DigitalBrain.Simulation.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~CatalogDiscoveryTests|FullyQualifiedName~CatalogToolTests"
  ```

- [ ] **Step 4: Implement the owner-keyed catalog grain.**

  ```csharp
  [GrainType("catalog-directory")]
  internal sealed class CatalogDirectoryGrain(CatalogDiscoveryService catalog)
      : Grain, ICatalogDirectory
  {
      public Task<DiscoveryResult> Discover(
          DiscoveryQuery query,
          CancellationToken cancellationToken = default)
          => catalog.DiscoverAsync(
              new OwnerId(this.GetPrimaryKeyString()), query, cancellationToken);

      public Task<CatalogInspection> Inspect(
          CatalogReference reference,
          CancellationToken cancellationToken = default)
          => catalog.InspectAsync(
              new OwnerId(this.GetPrimaryKeyString()), reference, cancellationToken);
  }
  ```

  Keep owner derivation exclusively from `GetPrimaryKeyString()` and propagate the explicit
  interface cancellation token unchanged.

- [ ] **Step 5: Add the separate one-line catalog client.**

  Define:

  ```csharp
  public interface IDigitalBrainCatalog
  {
      OwnerId Owner { get; }

      Task<DiscoveryResult> DiscoverAsync(
          DiscoveryQuery query,
          CancellationToken cancellationToken = default);

      Task<CatalogInspection> InspectAsync(
          CatalogReference reference,
          CancellationToken cancellationToken = default);
  }

  public static IDigitalBrainCatalog Connect(IGrainFactory grains, string owner);
  ```

  `DigitalBrainCatalogClient` stores validated `OwnerId`, obtains
  `grains.GetGrain<ICatalogDirectory>(Owner.Value)`, and forwards the explicit cancellation token.
  It contains no ranking, retry, or authorization branch. Do not change `IDigitalBrain`,
  `DigitalBrainClient`, or `src/DigitalBrainConsole/Brain.cs`; Catalog remains genuinely optional.

- [ ] **Step 6: Add the inspection router and register exactly two assistant adapters.**

  `InspectionRouter` materializes the `IInspectionProvider` set once and rejects duplicate exact
  `InspectionProviderKey` values at startup. It validates the envelope and owner-bearing references,
  derives `(reference.Kind, null)` for ordinary variants or
  `(DurableResource, normalized ResourceKind)` for durable variants, then performs dictionary
  dispatch and returns `UnsupportedReference` when no provider exists. It contains no resource-kind
  switch beyond constructing that key. `CatalogDescriptorInspectionProvider` is the only provider in this slice;
  it calls the owner-keyed `ICatalogDirectory.Inspect`, maps the catalog status into the general
  result, and preserves the exact `CatalogInspection` payload. Future modules add providers through
  DI without changing `CatalogToolSource`.

  `CatalogToolSource : IAgentToolSource` closes over the trusted `OwnerId` supplied by
  `ToolsFor(owner)`. `discover` accepts query text, optional kind/tag arrays, compatibility fields,
  limit, and cursor. `inspect` accepts `InspectionReference` and delegates to `InspectionRouter`.
  Both return bounded JSON; neither accepts owner, calls execution, or manufactures a replacement
  handle. Register the source only from `CatalogModule`, so a brain without Catalog does not
  advertise it.

  Update `Assistant.Instructions` with this precise behavior: use `discover` to obtain candidates,
  inspect one or more plausible exact handles, choose only from inspected authoritative results,
  and do not claim a candidate was executed. Ask for clarification when candidates remain
  materially ambiguous. A capability candidate is descriptive;
  use its capability/version as a constraint in a follow-up operation discovery rather than trying
  to invoke it. Keep legacy domain tools registered
  until catalog-backed `invoke` replaces them in the durable scripting slice.

- [ ] **Step 7: Run focused/full GREEN and commit.**

  ```powershell
  dotnet test tests/DigitalBrain.Catalog.Tests/DigitalBrain.Catalog.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~CatalogToolSourceTests|FullyQualifiedName~InspectionRouterTests"
  dotnet test tests/DigitalBrain.Simulation.Tests/DigitalBrain.Simulation.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~CatalogDiscoveryTests|FullyQualifiedName~CatalogToolTests"
  dotnet build DigitalBrain.slnx -c Release
  dotnet test DigitalBrain.slnx -c Release --no-build --no-restore --verbosity minimal --max-parallel-test-modules 1
  git add DigitalBrain.slnx src/Modules/Catalog src/Modules/AI/AI/Assistant.cs tests/DigitalBrain.Catalog.Tests/CatalogToolSourceTests.cs tests/DigitalBrain.Catalog.Tests/InspectionRouterTests.cs tests/DigitalBrain.Simulation.Tests/DigitalBrain.Simulation.Tests.csproj tests/DigitalBrain.Simulation.Tests/SimulationCollection.cs tests/DigitalBrain.Simulation.Tests/CatalogDiscoveryTests.cs tests/DigitalBrain.Simulation.Tests/CatalogToolTests.cs tests/DigitalBrain.Simulation.Tests/SimulationOwnerCatalogSource.cs
  git commit -m "feat: expose catalog discover and inspect"
  ```

---

### Task 8: Implement the Versioned Qdrant Catalog Provider and Safe Cutover

**Files:**

- Create: `src/Modules/Catalog/Catalog/Qdrant/{IQdrantCatalogClient,QdrantCatalogClient,QdrantCatalogIndex,QdrantCatalogRegistration}.cs`
- Create: `src/Modules/Catalog/Catalog/Grains/{ICatalogDeploymentCoordinator,CatalogDeploymentCoordinatorGrain}.cs`
- Create: `src/Modules/Catalog/Catalog/Grains/{CatalogDeploymentPolicy,CatalogDeploymentReconciler}.cs`
- Create: `tests/DigitalBrain.Catalog.Tests/QdrantCatalogIndexTests.cs`
- Create: `tests/DigitalBrain.Catalog.Tests/CatalogDeploymentCoordinatorTests.cs`
- Create: `tests/DigitalBrain.Simulation.Tests/CatalogDeploymentCoordinatorClusterTests.cs`
- Modify: `tests/DigitalBrain.Catalog.Tests/CatalogFixtures.cs`
- Modify: `tests/DigitalBrain.Catalog.Tests/CatalogProjectionCoordinatorTests.cs`
- Modify: `src/Modules/Catalog/Catalog/Projection/CatalogProjectionCoordinator.cs`
- Modify: `src/Modules/Catalog/Catalog/{DigitalBrain.Modules.Catalog.csproj,CatalogModule.cs,CatalogRuntimeOptions.cs}`
- Modify: `src/Modules/Catalog/Catalog/Initialization/{CatalogInitializationHostedService,CatalogSemanticRecoverySignal}.cs`
- Modify: `src/Testing/DigitalBrain.Testing/BrainSimulation.cs`
- Modify: `tests/DigitalBrain.Simulation.Tests/DigitalBrain.Simulation.Tests.csproj`

**Interfaces:**

- Consumes: Task 5 `ISemanticCatalogIndex`, `CatalogEmbeddingProfile`, Qdrant.Client 1.19.0, and projection watermark.
- Produces: catalog-specific collections, keyword payload filters, scored semantic hits, stable point
  IDs, connection/alias-keyed cluster serialization, and monotonic alias activation only after a
  complete rebuild from the exact requested `Discoverable` metadata snapshot.

- [ ] **Step 1: Write failing adapter tests for score preservation, payload isolation, and idempotent identity.**

  ```csharp
  [Fact]
  public async Task SearchPreservesQdrantScoreAndOwnerFilter()
  {
      var qdrant = new RecordingQdrantCatalogClient
      {
          SearchResults = [CatalogFixtures.ScoredPoint("operation.time.timer.start", 0.73f)],
      };
      var index = CatalogFixtures.QdrantIndex(qdrant);

      var search = await index.SearchAsync(
          CatalogFixtures.Profile(),
          new SemanticCatalogQuery(new OwnerId("owner-a"), [CatalogEntryKind.Operation], 32),
          new float[] { 1, 0 },
          TestContext.Current.CancellationToken);

      Assert.Equal(0.73f, Assert.Single(search.Hits).Similarity);
      Assert.Equal(["platform", "owner-a"], qdrant.LastScopeFilter);
      Assert.DoesNotContain("owner-b", qdrant.LastScopeFilter);
  }

  [Fact]
  public void PointIdentityIsStableAcrossRevisionButChangesWithScopeIdOrGeneration()
  {
      var key = CatalogFixtures.DocumentKey();
      var original = CatalogFixtures.SemanticDocument(
          key, sourceRevision: "first",
          descriptorFingerprint: new CatalogFingerprint(new string('a', 64)));
      var revised = CatalogFixtures.SemanticDocument(
          key, sourceRevision: "next",
          descriptorFingerprint: new CatalogFingerprint(new string('b', 64)));

      Assert.NotEqual(
          QdrantCatalogRegistration.PointId(key, "generation-a"),
          QdrantCatalogRegistration.PointId(key, "generation-b"));
      Assert.Equal(
          QdrantCatalogRegistration.PointId(original.Key, "generation-a"),
          QdrantCatalogRegistration.PointId(revised.Key, "generation-a"));
      Assert.NotEqual(
          QdrantCatalogRegistration.PointId(key, "generation-a"),
          QdrantCatalogRegistration.PointId(
              key with { EntryId = new CatalogEntryId("another") }, "generation-a"));
      Assert.NotEqual(
          QdrantCatalogRegistration.PointId(key, "generation-a"),
          QdrantCatalogRegistration.PointId(
              key with { Scope = CatalogScope.ForOwner(new OwnerId("owner-a")) }, "generation-a"));
  }
  ```

  Configure the recording adapter with the same ready control snapshot before and after query.
  Add a case which changes the control epoch between reads and assert no hits are returned and the
  typed semantic-snapshot exception is raised. Assert control/descriptor writes use `wait: true`
  plus strong ordering and all three search reads use all-replica consistency.

- [ ] **Step 2: Write the failing generation cutover test.**

  Start a rebuild at metadata watermark 12, report only 11 projected items, and assert
  `CommitUpdateAsync` was not called. Project item 12, verify every indexed record matches the current
  revision/fingerprint, and assert one alias update moves `digitalbrain_catalog_active` to the new
  physical collection. Duplicate completion must not perform a second conflicting swap. Interrupt
  after physical writes but before alias swap, construct a fresh coordinator, and prove it reconciles
  collection/alias state and completes without duplicating points.

  In `CatalogDeploymentCoordinatorTests`, exercise the pure `CatalogDeploymentPolicy` and the
  application-level `CatalogDeploymentReconciler` with recording state/Qdrant seams. Send intents
  for generation A at deployment epoch 41 and generation B at epoch 42. Assert only B may remain
  active, a delayed epoch-41 retry is `Superseded`, and an equal-epoch/different-generation request
  is `ConfigurationConflict`. Add an equal-dimension profile-change case so generation identity,
  not width, controls the result. Send an intent whose requested metadata snapshot fingerprint does
  not match the coordinator silo's immutable local snapshot and assert `IncompatibleCoordinator`
  occurs before any Qdrant mutation.

- [ ] **Step 3: Run RED.**

  ```powershell
  dotnet restore DigitalBrain.slnx
  dotnet test tests/DigitalBrain.Catalog.Tests/DigitalBrain.Catalog.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~QdrantCatalogIndexTests|FullyQualifiedName~CatalogProjectionCoordinatorTests|FullyQualifiedName~CatalogDeploymentCoordinatorTests"
  ```

- [ ] **Step 4: Implement the narrow Qdrant adapter.**

  `QdrantCatalogClient` is the only file which consumes `Qdrant.Client.Grpc` types. It wraps
  `CollectionExistsAsync`, `GetCollectionInfoAsync`, `ListAliasesAsync`, `CreateCollectionAsync`,
  `CreatePayloadIndexAsync`, `UpsertAsync`, `DeleteAsync`, `QueryAsync`, `ScrollAsync`, `CountAsync`,
  `DeleteCollectionAsync`, and `UpdateAliasesAsync` behind application records. Translate a missing
  collection or active alias into one typed application exception rather than matching provider
  error strings. Create keyword payload indexes for `record_type`, `scope`, `owner`, `entry_id`,
  `source_partition`, `kind`,
  `visibility`, `lifecycle`, `configuration_state`, `capability_id`, `capability_version`, `signal_alias`,
  `signal_schema_hash`, `input_schema_id`, `input_schema_hash`, `output_schema_id`,
  `output_schema_hash`, and `tags`.

  Await every descriptor/control mutation with `wait: true` and `WriteOrderingType.Strong`; use
  `new ReadConsistency { Type = ReadConsistencyType.All }` for every read in the
  before/query/after snapshot sequence. Do not let a fire-and-forget point acknowledgement violate
  the `Updating`/`Ready` fence.

- [ ] **Step 5: Implement catalog-specific records, stable point identity, and hard filters.**

  Store only safe projection payload:

  ```text
  record_type=descriptor, scope, owner, entry_id, source_kind, source_partition, source_id, source_revision,
  source_epoch, source_sequence, descriptor_fingerprint, kind, visibility, lifecycle, configuration_state,
  capability_id/version, signal_alias/schema_hash,
  input_schema_id/hash, output_schema_id/hash, tags
  ```

  Store no credential, source code, arbitrary entity payload, memory text, operation input, or
  protected reference. Live availability is also excluded because it is an observed overlay. Query
  with `record_type == descriptor`, `(scope == platform OR owner == current owner)`, plus every
  payload-representable hard filter and request the service-computed `CandidatePoolLimit`. Map
  `ScoredPoint.Score` to `Similarity`.

  Point identity is deterministic from semantic generation plus canonical scope key plus descriptor
  ID—never revision/fingerprint—so a new revision overwrites the old point instead of accumulating
  stale high-score records. Keep revision/fingerprint/source position in payload for hydration.
  Tombstones delete that stable point. Test repeated revision replacement and tombstone cleanup.

  Reserve one deterministic control-point ID per physical collection. Its zero vector is never a
  candidate because every descriptor query/filter/count requires `record_type=descriptor`; its
  safe payload stores `record_type=control`, generation ID, positive deployment epoch,
  configured-deployment-manifest fingerprint, random collection-incarnation ID, projection epoch,
  projected metadata watermark, metadata-snapshot fingerprint, and
  `Updating|Ready`. Creating a missing
  physical collection creates a fresh incarnation. `BeginUpdateAsync` advances the epoch and writes
  `Updating` before descriptor mutations; `CommitUpdateAsync` writes `Ready` only after exact
  coverage validation. Search reads this control payload before and after `QueryAsync` and rejects
  the result if its ready token changed.

  If the deterministic physical collection exists but its control point is absent, malformed, or
  names another generation, treat it as an incomplete Catalog-owned build. Mark semantic readiness
  degraded, remove that exact physical collection (and only its alias edge, if present), recreate it
  with the profile dimensions and payload indexes, write a fresh incarnation/control point, and
  perform full canonical reconstruction. Never adopt unknown descriptor points merely because the
  collection name matches. A valid `Updating` control point for the same deterministic update ID is
  resumed instead of recreated.

- [ ] **Step 6: Implement deterministic generations and alias cutover.**

  Add these exact internal Orleans contracts (with serializer aliases and member IDs):

  ```csharp
  [GenerateSerializer]
  [Alias("db.catalog.partition-snapshot-identity")]
  internal sealed record CatalogPartitionSnapshotIdentity(
      [property: Id(0)] CatalogSourcePartition Partition,
      [property: Id(1)] string SnapshotToken,
      [property: Id(2)] CatalogSourcePosition HighWatermark);

  [GenerateSerializer]
  [Alias("db.catalog.deployment-intent")]
  internal sealed record CatalogDeploymentIntent(
      [property: Id(0)] long DeploymentEpoch,
      [property: Id(1)] CatalogEmbeddingProfile Profile,
      [property: Id(2)] string DeploymentManifestFingerprint,
      [property: Id(3)] long MetadataWatermark,
      [property: Id(4)] string MetadataSnapshotFingerprint,
      [property: Id(5)] IReadOnlyList<CatalogPartitionSnapshotIdentity> Partitions);

  [GenerateSerializer]
  [Alias("db.catalog.deployment-status")]
  internal enum CatalogDeploymentStatus
  {
      Ready,
      Superseded,
      ConfigurationConflict,
      IncompatibleCoordinator,
      StaleSnapshot,
  }

  [GenerateSerializer]
  [Alias("db.catalog.deployment-result")]
  internal sealed record CatalogDeploymentResult(
      [property: Id(0)] CatalogDeploymentStatus Status,
      [property: Id(1)] SemanticCatalogSnapshot? Snapshot,
      [property: Id(2)] string? Reason);

  [Alias("db.catalog.deployment-coordinator")]
  internal interface ICatalogDeploymentCoordinator : IGrainWithStringKey
  {
      [Alias(nameof(Reconcile))]
      Task<CatalogDeploymentResult> Reconcile(
          CatalogDeploymentIntent intent,
          CancellationToken cancellationToken = default);
  }
  ```

  `CatalogDeploymentKey.For(connectionName, activeAlias)` is lowercase SHA-256 over a
  version-prefixed canonical pair. Every silo uses that key, so generations which share an alias
  cannot build/cut over concurrently. The non-reentrant grain receives
  `[PersistentState("catalog-deployment", DigitalBrainNames.DefaultGrainStorage)]` and durably stores
  the highest accepted deployment epoch, profile/generation, deployment-manifest fingerprint, and
  last ready semantic snapshot identity. Cross-check that fence with any ready Qdrant control record
  on activation; an equal-epoch identity disagreement is an integrity failure. If Qdrant is lost,
  the grain-state fence still rejects an older rollout; if grain state is empty but Qdrant is intact,
  seed it from the ready control record.
  Keep comparison/fencing in the pure `CatalogDeploymentPolicy` and external reconciliation in
  `CatalogDeploymentReconciler`; the grain owns only Orleans identity, serialized turns, persistent
  fence transitions, and delegation. Unit tests use those focused classes rather than attempting to
  construct a grain outside an Orleans runtime.

  Validate the intent against the coordinator silo *before* advancing the fence or writing Qdrant:
  recapture its selected profile, configured contribution-manifest fingerprint, immutable
  `Discoverable` discovery-snapshot watermark/fingerprint, and ordered partition snapshot
  token/high-watermark set. Any mismatch is
  `IncompatibleCoordinator`, calls `DeactivateOnIdle`, and performs no write, allowing bounded retry
  to land on a compatible rolling-upgrade silo. A lower epoch is `Superseded`; at equal epoch, another
  profile/generation or deployment manifest is `ConfigurationConflict`; a higher valid epoch is
  persisted as desired before external writes and is the only way to change those identities.
  Development/testing default to epoch 1. Production rollout configuration must be homogeneous and
  increment the epoch for a new profile/static manifest; deliberate rollback also uses a higher
  epoch, never decrementing the fence. This slice has only the immutable configured platform
  partition. The first durable owner source extends the persisted intent with checkpoint-vector
  dominance: every partition must be equal or advance, while regression/incomparability returns
  `StaleSnapshot` and requires a new unified capture.

  Create the physical collection with the profile's declared dimensions before any upsert. If an
  existing collection has a different vector width, fail the generation. Populate every descriptor
  in the captured immutable `Discoverable` discovery snapshot and no descriptor from the broader
  inspection view. Replay changes through the captured high watermark, verify exact
  revision/fingerprint coverage through `EnumeratePartitionAsync` (implemented as filtered Qdrant
  scrolling) for each completely enumerated source partition, delete stale point IDs absent from
  the desired discoverable set, and count/compare the exact final key set and semantic coverage.
  Mark the control snapshot ready only after that prune/coverage gate, then atomically delete/add the
  active alias in one update request. Search
  always targets the active alias; rebuild writes always target the physical generation. Read
  current aliases and the control record first so retry after a crash is idempotent. Keep the
  previous physical collection; deletion is outside this slice's rollback grace period.
  `CatalogDeploymentCoordinatorGrain` is the only production caller allowed to pass
  `activate: true` to `CommitUpdateAsync`, and the Qdrant adapter is the only component allowed to
  mutate aliases. All other projection calls publish `Ready` physical state with `activate: false`.

  Every silo calls `ICatalogDeploymentCoordinator` with the connection/alias key and its exact
  `CatalogDeploymentIntent`. Its grain turn performs the idempotent reconcile above and treats both
  durable grain fence state and Qdrant control/alias contents as recovery evidence; a
  crash/deactivation resumes the persisted desired intent and never trusts volatile progress or a
  different silo's unverified local descriptors.

  Extend coordinator tests with (a) an existing same-generation collection containing a descriptor
  removed from the next complete static snapshot under a higher deployment epoch and (b) a crash after `CreateCollectionAsync`
  but before the first control-point upsert. The first retry must prune the removed point before
  `Ready`; the second must recreate with a fresh incarnation and complete without adopting partial
  data.

  Once Qdrant is selected, the hosted reconciliation loop from Task 5 calls that coordinator on its
  periodic tick. `QdrantCatalogIndex` signals the same loop immediately when a query observes the
  typed missing-collection/alias condition. The failing request still returns exact/lexical results
  with `SemanticDegraded`; recovery happens off the request path, is serialized by the deployment
  grain, and clears degradation only after coverage and alias validation succeed. Add a fake-client
  test which removes the active collection after successful startup, performs discovery, and proves
  this automatic path recreates it without a manual coordinator call.

  Extend `BrainSimulationOptions` with validated `short SiloCount { get; init; } = 1`, pass it to
  `new InProcessTestClusterBuilder(options.SiloCount)`, and expose a read-only
  `SiloGrainFactories` list built from `cluster.Silos[i].ServiceProvider.GetRequiredService<IGrainFactory>()`.
  Grant `InternalsVisibleTo("DigitalBrain.Simulation.Tests")` from the Catalog runtime. In
  `CatalogDeploymentCoordinatorClusterTests`, start two silos with one thread-safe recording Qdrant
  adapter registered after the module's provider registration and explicitly select Qdrant in test
  configuration. Obtain the same deployment-keyed grain through both silo-local factories, invoke the same
  intent concurrently, and prove one physical reconciliation/alias transition and the same ready
  result for both callers. The competing epoch-41/epoch-42 policy remains a deterministic unit test,
  since an in-process test cluster intentionally has homogeneous silo configuration.

- [ ] **Step 7: Wire explicit provider selection with honest fallback.**

  Use `DigitalBrain:Catalog:Provider=Qdrant` and
  `DigitalBrain:Catalog:Qdrant:ConnectionName=qdrant`, plus the validated collection-prefix and
  active-alias keys from Task 5. Outside the explicit testing composition,
  require a Catalog provider value and fail startup on an unknown provider or missing selected
  provider dependency. Never infer Catalog's provider merely because another module has a Qdrant
  connection. An explicit Catalog provider value always wins over global testing mode; testing mode
  selects the in-memory semantic index only when the Catalog provider key is absent. Add a
  registration test for both precedence cases. This lets the AppHost E2E fixture retain deterministic
  test embeddings while exercising the explicitly projected Qdrant Catalog provider. Runtime search
  failures still degrade per Task 6.

- [ ] **Step 8: Run focused/full GREEN and commit.**

  ```powershell
  dotnet test tests/DigitalBrain.Catalog.Tests/DigitalBrain.Catalog.Tests.csproj -c Release --no-restore
  dotnet test tests/DigitalBrain.Simulation.Tests/DigitalBrain.Simulation.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~CatalogDeploymentCoordinatorClusterTests"
  dotnet build DigitalBrain.slnx -c Release
  dotnet test DigitalBrain.slnx -c Release --no-build --no-restore --verbosity minimal --max-parallel-test-modules 1
  git add src/Modules/Catalog tests/DigitalBrain.Catalog.Tests src/Testing/DigitalBrain.Testing/BrainSimulation.cs tests/DigitalBrain.Simulation.Tests/DigitalBrain.Simulation.Tests.csproj tests/DigitalBrain.Simulation.Tests/CatalogDeploymentCoordinatorClusterTests.cs
  git commit -m "feat: add qdrant catalog generations"
  ```

---

### Task 9: Share Qdrant in Aspire and Prove the Complete Vertical

**Files:**

- Create: `src/Aspire/DigitalBrain.Aspire.Hosting/Brain/{QdrantHostingState,DigitalBrainQdrantHostingExtensions}.cs`
- Create: `src/Modules/Catalog/Aspire.Hosting/DigitalBrain.Modules.Catalog.Aspire.Hosting.csproj`
- Create: `src/Modules/Catalog/Aspire.Hosting/CatalogHostingExtensions.cs`
- Create: `tests/DigitalBrain.Aspire.Tests/CatalogHostingTests.cs`
- Modify: `src/Aspire/DigitalBrain.Aspire.Hosting/DigitalBrain.Aspire.Hosting.csproj`
- Modify: `src/Modules/Memory/Aspire.Hosting/MemoryHostingExtensions.cs`
- Modify: `src/Modules/Memory/Memory/Qdrant/{QdrantVectorMemoryRegistration,QdrantVectorMemoryProvider}.cs`
- Modify: `src/Aspire/DigitalBrain.AppHost/{DigitalBrain.AppHost.csproj,AppHost.cs}`
- Modify: `src/Kernel/DigitalBrain.Silo/{DigitalBrain.Silo.csproj,Dockerfile,Properties/PublishProfiles/Container.pubxml}`
- Modify: `tests/DigitalBrain.Simulation.Tests/{DigitalBrain.Simulation.Tests.csproj,SimulationCollection.cs,CatalogDiscoveryTests,CatalogToolTests}.cs`
- Modify: `tests/DigitalBrain.Aspire.Tests/{DigitalBrain.Aspire.Tests.csproj,ReleaseModuleManifestConformanceTests.cs}`
- Create: `tests/DigitalBrain.E2E.Tests/CatalogQdrantIntegrationTests.cs`
- Modify: `tests/DigitalBrain.E2E.Tests/DigitalBrain.E2E.Tests.csproj`
- Modify: `tests/DigitalBrain.E2E.Tests/E2ECollection.cs`
- Modify: `src/Testing/DigitalBrain.Testing.E2E/BrainAppHostFixture.cs`
- Modify: `DigitalBrain.slnx`

**Interfaces:**

- Consumes: Catalog module/hosting extension, Memory's existing Qdrant opt-in, AI default embedding,
  AppHost module projection, and full test fabric.
- Produces: one operational Qdrant server with isolated memory/catalog collections, release wiring,
  deterministic simulation behavior, a generic fixture seam for optional Orleans clients, and
  end-to-end acceptance evidence.

- [ ] **Step 1: Write the failing Aspire model test.**

  ```csharp
  [Fact]
  public async Task MemoryAndCatalogShareOneQdrantServerButUseSeparateCollections()
  {
      var qdrant = fixture.Model.Resources.OfType<QdrantServerResource>().ToArray();
      var environment = await fixture.Model.RenderedEnvironmentAsync(
          ProductSurfaceResourceNames.Kernel);

      Assert.Single(qdrant);
      Assert.Equal("Qdrant", environment["DigitalBrain__Catalog__Provider"]);
      Assert.Equal("qdrant", environment["DigitalBrain__Memory__Qdrant__ConnectionName"]);
      Assert.Equal("qdrant", environment["DigitalBrain__Catalog__Qdrant__ConnectionName"]);
      Assert.Equal("1", environment["DigitalBrain__Catalog__DeploymentEpoch"]);
      Assert.Equal("digitalbrain_vector_memory",
          environment["DigitalBrain__Memory__Qdrant__CollectionName"]);
      Assert.Equal("digitalbrain_catalog_active",
          environment["DigitalBrain__Catalog__Qdrant__ActiveAlias"]);
      Assert.NotEqual(
          environment["DigitalBrain__Memory__Qdrant__CollectionName"],
          environment["DigitalBrain__Catalog__Qdrant__ActiveAlias"]);
  }
  ```

  Extend release manifest conformance so `CatalogModule` appears after `AIModule` in AppHost,
  Dockerfile, and publish-profile configuration.

- [ ] **Step 2: Run RED.**

  ```powershell
  dotnet restore DigitalBrain.slnx
  dotnet test tests/DigitalBrain.Aspire.Tests/DigitalBrain.Aspire.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~CatalogHostingTests|FullyQualifiedName~ReleaseModuleManifestConformanceTests"
  ```

- [ ] **Step 3: Extract one idempotent shared Qdrant resource projection.**

  `QdrantHostingState` lives in general Aspire hosting, creates resource `qdrant` once, attaches it
  to the brain, projects one connection named `qdrant`, and adds one healthy-start dependency.
  Move the unchanged `digitalbrain_vector_memory` default from the internal provider to public
  `QdrantVectorMemoryRegistration.DefaultCollectionName`; the provider and hosting projection both
  consume that single constant. `MemoryHostingExtensions.WithQdrant()` requests the shared state
  and sets only Memory's provider, connection, and collection keys.
  `CatalogHostingExtensions.WithQdrant(long deploymentEpoch)` requires a positive explicit epoch,
  requests the same state, and sets only Catalog's provider, connection, active-alias,
  collection-prefix, model-revision, and `DigitalBrain__Catalog__DeploymentEpoch` keys. Defaults remain
  `digitalbrain_catalog_active` and `digitalbrain_catalog`.

- [ ] **Step 4: Add Catalog to product and release composition.**

  In `AppHost.cs`, configure:

  ```csharp
  .AddModule<AIModule>(ai =>
  {
      // existing model setup remains unchanged
  })
  .AddModule<CatalogModule>(catalog => catalog.WithQdrant(deploymentEpoch: 1))
  .AddModule<MemoryModule>(memory => memory.WithQdrant())
  ```

  Add all Catalog project references to AppHost/Silo as required. Shift subsequent release module
  environment indices consistently in Dockerfile and Container.pubxml. Add the five Catalog
  projects and Catalog tests to `DigitalBrain.slnx`.

- [ ] **Step 5: Run the end-to-end simulation acceptance scenarios.**

  Retain the CatalogModule placement added to the shared simulation manifest in Task 7. Prove:

  ```text
  "remind me" discovers time.timer.start and the Timer neuron type
  "semantic notes" discovers Vector Memory and memory.vector.search
  kind=Capability returns time.timer and memory.vector definitions, never capability leases
  kind=Module returns modules only
  an exact alias outranks a semantically close description
  a fabricated/stale handle is refused by inspect
  owner A cannot observe owner B's fake owner-source descriptor
  semantic-provider failure returns exact/lexical candidates with SemanticDegraded
  discover and inspect create no signal, synapse, lease, run, or effect
  public VectorMemory writes never appear in catalog results
  ```

- [ ] **Step 6: Run real-Qdrant filter, alias, and loss-recovery tests.**

  In `E2ECollection.cs`, give the one assembly-shared `AppHostFixture` a random eight-character
  suffix at fixture construction. Pass `BrainE2EOptions.ProjectEnvironment` values which explicitly
  select `DigitalBrain__Catalog__Provider=Qdrant`, set
  `DigitalBrain__Catalog__Qdrant__ActiveAlias=digitalbrain_catalog_e2e_<suffix>`, and set
  `DigitalBrain__Catalog__Qdrant__CollectionPrefix=digitalbrain_catalog_e2e_<suffix>`. Expose the
  resulting alias/prefix as read-only fixture properties. Per Task 8, this explicit provider wins
  over the fixture's global Testing mode, while AI still supplies deterministic test embeddings.

  In `CatalogQdrantIntegrationTests`, use that existing
  `BrainAppHostFixture<Projects.DigitalBrain_AppHost>` to obtain the real `qdrant` connection. Give
  raw-adapter cases their own unique aliases/physical collections and delete only those exact test
  collections in `finally` cleanup. Through `QdrantCatalogIndex`, write platform, owner-A, and owner-B
  descriptors and prove a real `QueryAsync` returns platform plus owner A, never owner B, while
  kind/tag/schema filters and `ScoredPoint.Score` survive translation.

  Add this generic seam to `BrainAppHostFixture` so optional module clients can reuse its connected
  Orleans client without making the testing library depend on Catalog:

  ```csharp
  public TClient ConnectClient<TClient>(Func<IGrainFactory, TClient> connect)
  {
      ArgumentNullException.ThrowIfNull(connect);
      return _grains is null
          ? throw new InvalidOperationException(
              $"{nameof(ConnectClient)} was called before {nameof(InitializeAsync)} completed.")
          : connect(_grains);
  }
  ```

  Make `BrainFor` delegate through this seam. The integration test obtains the optional facade with
  `fixture.ConnectClient(grains => DigitalBrainCatalogClient.Connect(grains, owner))`; do not widen
  `IDigitalBrain` or expose the private grain factory directly.

  Then drive the configured runtime through
  `fixture.ConnectClient(grains => DigitalBrainCatalogClient.Connect(grains, owner))`, verify the
  fixture-unique alias and results, and retain a ready first-page cursor. Resolve that alias's exact physical target through
  `ListAliasesAsync`, and delete only that physical collection through `DeleteCollectionAsync`.
  Trigger a discover request, assert exact/lexical candidates return with
  `SemanticDegraded` and no next cursor, and assert the retained cursor is `StaleCursor`. Wait for
  the signalled hosted recovery path—do not call the coordinator manually. Assert the collection,
  payload indexes, control incarnation, points, and alias are restored from canonical descriptors,
  semantic search succeeds, and the pre-loss cursor remains stale after recovery. Restore the fixture-unique runtime generation before
  the test returns so later serial E2E classes see a healthy shared application. Wrap the destructive
  delete-through-recovery scenario in `try/finally`: the `finally` block performs bounded
  best-effort reconciliation, waits for the fixture alias to target a ready healthy collection, and
  reports cleanup failure alongside any test failure. A failed assertion or timeout must not leave
  the assembly-shared fixture poisoned for later serial tests. Add Catalog
  client/runtime/contracts project references to the E2E project; the runtime project already grants
  `InternalsVisibleTo("DigitalBrain.E2E.Tests")` from Task 3. Add no new package because the
  repository already pins Qdrant.Client and Aspire testing.

- [ ] **Step 7: Run repository self-review checks.**

  ```powershell
  rg -n "VectorMemoryNeuron|IVectorMemoryStore|digitalbrain_vector_memory" src/Modules/Catalog
  rg -n "Deliver|SendAsync|EffectBroker|CapabilityLease|SynapseSet" src/Modules/Catalog/Catalog/Search
  rg -n -g '*CatalogContribution.cs' "AppDomain.CurrentDomain.GetAssemblies" src/Modules/Catalog src/Modules
  rg -n "Microsoft.Extensions.VectorData" Directory.Packages.props src/Modules/Catalog
  ```

  Expected: the first two searches have no matches, the catalog path contains no assembly scan, and
  no new VectorData package/reference exists. The existing temporary scan in
  `SignalHandlerIndex.cs` is outside these paths and remains unchanged.

- [ ] **Step 8: Run the complete verification gate.**

  ```powershell
  dotnet restore DigitalBrain.slnx
  dotnet test tests/DigitalBrain.Catalog.Tests/DigitalBrain.Catalog.Tests.csproj -c Release --no-restore
  dotnet test tests/DigitalBrain.Substrate.Tests/DigitalBrain.Substrate.Tests.csproj -c Release --no-restore
  dotnet test tests/DigitalBrain.Simulation.Tests/DigitalBrain.Simulation.Tests.csproj -c Release --no-restore
  dotnet test tests/DigitalBrain.Aspire.Tests/DigitalBrain.Aspire.Tests.csproj -c Release --no-restore
  dotnet test tests/DigitalBrain.E2E.Tests/DigitalBrain.E2E.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~CatalogQdrantIntegrationTests"
  dotnet build DigitalBrain.slnx -c Release --no-restore
  dotnet test DigitalBrain.slnx -c Release --no-build --no-restore --verbosity minimal --max-parallel-test-modules 1
  git diff --check
  ```

  Expected: all new catalog, substrate, simulation, Aspire, and full-solution tests pass with zero
  build warnings; `git diff --check` reports no whitespace errors.

- [ ] **Step 9: Commit the deployable vertical.**

  ```powershell
  git add -- DigitalBrain.slnx src/Aspire/DigitalBrain.Aspire.Hosting/Brain/QdrantHostingState.cs src/Aspire/DigitalBrain.Aspire.Hosting/Brain/DigitalBrainQdrantHostingExtensions.cs src/Aspire/DigitalBrain.Aspire.Hosting/DigitalBrain.Aspire.Hosting.csproj src/Aspire/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj src/Aspire/DigitalBrain.AppHost/AppHost.cs
  git add -- src/Modules/Catalog/Aspire.Hosting src/Modules/Memory/Aspire.Hosting/MemoryHostingExtensions.cs src/Modules/Memory/Memory/Qdrant/QdrantVectorMemoryRegistration.cs src/Modules/Memory/Memory/Qdrant/QdrantVectorMemoryProvider.cs
  git add -- src/Kernel/DigitalBrain.Silo/DigitalBrain.Silo.csproj src/Kernel/DigitalBrain.Silo/Dockerfile src/Kernel/DigitalBrain.Silo/Properties/PublishProfiles/Container.pubxml src/Testing/DigitalBrain.Testing.E2E/BrainAppHostFixture.cs
  git add -- tests/DigitalBrain.Simulation.Tests/DigitalBrain.Simulation.Tests.csproj tests/DigitalBrain.Simulation.Tests/SimulationCollection.cs tests/DigitalBrain.Simulation.Tests/CatalogDiscoveryTests.cs tests/DigitalBrain.Simulation.Tests/CatalogToolTests.cs tests/DigitalBrain.Aspire.Tests/DigitalBrain.Aspire.Tests.csproj tests/DigitalBrain.Aspire.Tests/CatalogHostingTests.cs tests/DigitalBrain.Aspire.Tests/ReleaseModuleManifestConformanceTests.cs tests/DigitalBrain.E2E.Tests/DigitalBrain.E2E.Tests.csproj tests/DigitalBrain.E2E.Tests/E2ECollection.cs tests/DigitalBrain.E2E.Tests/CatalogQdrantIntegrationTests.cs
  git diff --cached --check
  git diff --cached --name-only
  git commit -m "feat: wire ranked self-knowledge discovery"
  ```

---

## Dependent Follow-On Plan Boundary

Do not fold these into this plan. The next durable-scripting implementation plan consumes the exact
catalog contracts and must add, in dependency order:

1. a journaled owner directory and idempotent descriptor outbox/checkpoint;
2. `ScriptDefinition`, `AutomationDefinition`, and reusable `AgentDefinition` catalog sources;
3. source/published-revision upserts and lifecycle tombstones carrying durable source epochs and
   sequences;
4. typed script wrapper generation from exact `CatalogOperationDescriptor` schema hashes;
5. inspection of one or more provisional exact handles, followed by a selection gate where only
   unique exact structural evidence or an explicit owner policy can select automatically—semantic
   rank alone must abstain—and persistence of `SelectionDecision` with the final handle, query
   fingerprint, evidence, policy version, and decision time;
6. exact `invoke` through the durable run/capability/effect boundary, pinning the immutable
   operation-manifest artifact/fingerprint and binding revision into the admitted run;
7. deterministic `IncompatibleDeployment` recovery when the pinned binding revision is absent,
   never silent rebinding to a newer manifest;
8. durable `observe` over run/activity/artifact cursors; and
9. removal of legacy per-domain assistant tools after their operations are available behind
   catalog-backed `invoke`.

Automatic similarity-assisted signal routing is a separate later plan. It can reuse catalog
candidate retrieval only after applying exact signal alias/schema compatibility and may create a
`Discovered` synapse only after a selected delivery returns `DeliveryOutcome.Handled`.
