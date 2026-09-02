# DigitalBrain v2 — Self-Knowledge and Ranked Discovery

**Status:** Ratified; implementation plan prepared
**Date:** 2026-09-02
**Branch:** `native`
**Scope:** The catalog and intent-resolution foundation which precedes durable scripting.

This specification defines how DigitalBrain knows what it contains and what it can do. It refines
the four assistant-facing operations in
[`2026-09-02-digitalbrain-v2-durable-runs-design.md`](./2026-09-02-digitalbrain-v2-durable-runs-design.md)
and provides the operation-manifest prerequisite for
[`2026-09-02-digitalbrain-v2-durable-scripting-design.md`](./2026-09-02-digitalbrain-v2-durable-scripting-design.md).
It is authoritative over the older description-only and top-vector-hit routing examples in
[`2026-09-02-digitalbrain-v2-neuron-substrate-design.md`](./2026-09-02-digitalbrain-v2-neuron-substrate-design.md)
and [`docs/digitalbrain-v2-anatomy.html`](../../digitalbrain-v2-anatomy.html).

The user-approved selection rule is:

> Semantic intent matching returns compatible ranked candidates. It never directly executes the
> top vector result. The assistant or routing policy inspects one or more exact typed references,
> then a durable caller persists the final selection before execution.

---

## 1. Outcome

DigitalBrain has a typed, inspectable description of its installed modules, neuron types, signal
contracts, operations, capabilities, and later its owner-scoped neuron instances, scripts,
automations, reusable agents, entities, and activities. An assistant can search that description
using natural language and structural filters without receiving one permanent tool per capability.

The normal assistant path is:

```text
user intent
    -> discover(text + typed constraints)
    -> compatible ranked candidates with evidence
    -> inspect(one or more exact revisioned handles)
    -> assistant/policy chooses from inspected authoritative state and schemas
    -> durable caller persists the final SelectionDecision
    -> invoke(operation handle + operation id) authorizes and executes
    -> observe(reference + cursor) follows durable progress
```

This slice implements the catalog, ranking, `discover`, and catalog form of `inspect`. It does not
implement automatic invocation, semantic signal delivery, or a second workflow engine.

---

## 2. Decisions and invariants

1. **The catalog is authoritative; its indexes are not.** Canonical descriptors come from
   configured module manifests and durable definition aggregates. Exact, lexical, and semantic
   indexes are disposable projections.
2. **Discovery returns candidates, never actions.** No search result invokes an operation, sends a
   signal, grants a lease, or creates a synapse.
3. **Compatibility precedes ranking.** Owner visibility, lifecycle, kind, exact contract version,
   schema, and requested facets remove invalid candidates before lexical or semantic scores matter.
4. **Exact identity dominates similarity.** A matching stable ID, operation ID, or declared alias
   ranks before a merely similar description.
5. **Lexical and dense scores are not added together.** Candidate lists are fused by rank using
   Reciprocal Rank Fusion, followed by deterministic structural ordering.
6. **Every result is revisioned and explainable.** A candidate carries its exact descriptor handle,
   rank components, matched fields, availability summary, and projection version evidence.
7. **`inspect` re-resolves authority.** A stale, retired, missing, or tampered handle fails
   explicitly. It never silently follows an active pointer to a different revision.
8. **Authorization stays separate.** Availability and lease eligibility may be described, but only
   `invoke` and the provider boundary authorize execution.
9. **User vector memory is a different domain.** `VectorMemoryNeuron` and its public namespaces are
   not the self-knowledge catalog and cannot write its index.
10. **Embeddings never enter aggregate state.** Neurons, definitions, scripts, automations, and
    entities store source data and exact references, not model-specific vectors.
11. **Model identity is part of index identity.** Provider/model ID, operator-pinned model revision,
    dimensions, preprocessing, and discovery-document format determine the index generation.
12. **Failure degrades discovery, not the brain.** If embeddings or Qdrant are unavailable, exact
    and lexical discovery continue and the response reports semantic degradation.
13. **Automatic similarity-assisted signal routing is deferred.** When added, it may choose only
    among exact signal-contract-compatible targets and learns only after `DeliveryOutcome.Handled`.

---

## 3. Vocabulary and boundaries

| Term | Meaning | Not this |
|---|---|---|
| **Self-knowledge catalog** | Typed, revisioned descriptors of system resources and operations. | A vector database or prompt dump. |
| **Canonical descriptor** | Safe metadata produced by the owner of a module or durable definition. | Live provider state, credentials, or an executable delegate. |
| **Semantic index** | Rebuildable dense-vector projection of safe discovery documents. | Source of identity, schemas, lifecycle, or authority. |
| **Lexical index** | Rebuildable exact/token projection used both alone and beside dense search. | A magic substring score mixed with cosine. |
| **Discovery handle** | Stable scope/source/descriptor identity plus exact source revision and fingerprint. | An Orleans proxy or mutable active pointer. |
| **Operation manifest** | Canonical operation/version, schemas, recovery semantics, and binding reference. | A run-scoped lease or search result. |
| **Capability definition** | Discoverable stable capability/version grouping for related operations. | A grant, token, or runtime authority. |
| **Capability lease** | Exact authority held by a run/caller. | A tag, synapse, prompt, similarity score, or provider connection. |
| **Trigger registry** | Exact signal-alias/schema routing for published automations. | The self-knowledge catalog. |
| **Synapse set** | Durable graph edges learned from handled traffic. | A document index or permission list. |
| **User vector memory** | Owner-authored remembered content. | System self-description. |

The same Qdrant server and embedding infrastructure may be shared operationally, but catalog and
memory use different collections, records, writer interfaces, rebuild rules, and public contracts.

---

## 4. Authoritative sources

| Catalog kind | Canonical source |
|---|---|
| Installed module | Explicit contribution made while a configured `IModule` hook runs. |
| Neuron type | Explicit or generated module manifest, validated against the declared CLR contract. |
| Signal contract | Explicit or generated module manifest with stable alias and schema fingerprint. |
| Invocable operation/capability | Module-owned operation manifest with exact version and schema fingerprints. |
| Addressable neuron instance | Owner directory entry; never an arbitrary scan of active Orleans grains. |
| Script | Journaled `ScriptDefinition` and immutable source/published-program revision. |
| Automation | Journaled `AutomationDefinition` and exact published automation revision. |
| Reusable agent | Journaled `AgentDefinition` and immutable revision. |
| One-off delegated agent | Its durable run/activity reference; it is not permanently catalogued by default. |
| Entity or activity | Explicit safe descriptor supplied by its owner projection. |
| Synapse | Source neuron's durable state, queried through graph inspection rather than embedded by default. |
| Signal/event occurrence | Journal/activity stream, queried through `observe`; only the signal *contract* is catalogued. |

Configured modules are the platform allowlist. Loaded assemblies, `AppDomain` scans,
`IAgentToolSource`, `ICapabilityHandler`, MCP live tool lists, and arbitrary grain activations are not
catalog authority. Runtime tool/executor objects are bindings to canonical operation IDs; they are
not descriptors themselves.

The owner directory is the rebuild root for dynamic definitions and named neuron instances. It
stores stable references and revisions, not embeddings and not copies of aggregate state.

---

## 5. Canonical descriptor model

Every descriptor has a common header:

```text
DescriptorId        stable kind-qualified identity
Scope               Platform | Owner(OwnerId)
Kind                Module | Capability | NeuronType | NeuronInstance | SignalContract |
                    Operation | Script | Automation | AgentDefinition |
                    Entity | Activity
Source               source kind + stable source ID
SourceRevision       exact immutable source revision
Fingerprint          SHA-256 of canonical descriptor JSON
Lifecycle            Draft | Active | Suspended | Retired
Visibility           safe discovery visibility
Name                 human-readable name
Summary              concise factual purpose
Aliases              exact alternative names
Keywords             lexical retrieval terms
Tags                 typed/faceted classification
RoutingExamples      short examples of when to use the item
ConfigurationState  canonical declared/configured/disabled state
TypedReference       kind-specific opaque application reference
```

`TypedReference` is a validated discriminated value, not a free-form string: platform definitions
use a stable platform ID, neuron instances use `NeuronId`, entities use `EntityId`, and
script/automation/agent/activity entries use an owner-local durable resource reference. Exactly one
payload is populated and its kind must match the descriptor; `NeuronId`/`EntityId` ownership must
match the descriptor scope. Durable references omit an owner because scope already supplies the
trusted owner boundary.

A signal-contract descriptor contains its stable alias and exact canonical schema reference/hash.
A neuron descriptor contains its stable contract alias, grain type, and the sorted alias/schema-hash
set of signals it handles. This is authoritative descriptive topology; it does not activate a grain,
deliver a signal, or assert that a particular neuron instance is currently reachable.

An operation descriptor additionally contains:

```text
OperationId and semantic Version
CapabilityId and capability Version
InputSchemaId + InputSchemaHash
OutputSchemaId + OutputSchemaHash
Canonical input/output JSON schema documents (or immutable artifact references)
RecoverySemantics    ReplaySafe | Idempotent | Reconcileable | NonRecoverable
BindingReference     stable executor/application binding ID + exact binding revision
RequiredScopes       declarative requirements, not a lease
```

A `Capability` descriptor names and explains the stable capability/version grouping referenced by
one or more operations. It is searchable self-knowledge only. Runtime authority is represented
solely by a `CapabilityLease`, never by the descriptor or its search rank. A caller uses the
capability/version as a structural constraint to discover operations; the capability descriptor
itself is not invocable.

Operation input/output contracts are application DTOs, not transport signals. Fields such as
`CommandId`, run ID, owner, correlation, causation, and lease come from trusted invocation context
and are adapted into signals only at the exact binding. Static operations without that binding are
catalogued as `Declared`, not falsely reported as invocable.

`Visibility` is either `Discoverable` or `InspectOnly`. The authoritative current-descriptor view
retains both so an owner-visible exact handle may inspect either, but the exact-discovery view,
lexical index, semantic documents, semantic coverage, and their candidate pools contain only
`Discoverable` descriptors. `InspectOnly` is therefore absent before every retrieval lane rather
than fetched and discarded after it consumes a bounded lane position. Resources which cannot be
safely inspected are not catalogued at all.

The descriptor fingerprint covers every authoritative descriptor field except its own fingerprint
slot, avoiding a circular hash. It excludes projection data: vectors, vector scores, token positions,
live connectivity/health samples, index generation, and projection checkpoints. Observed provider
availability is a timestamped non-authoritative overlay resolved during discovery/inspection; it
does not stale a canonical handle. Collection records include the descriptor fingerprint so every
hit can be checked against authoritative metadata.

Canonical schema and descriptor bytes are version-prefixed (`catalog-schema-v1` and
`catalog-descriptor-v1`). A static descriptor first derives `SourceRevision` as `static-v1:<sha256>`
over its authoritative payload excluding source revision and fingerprint, then derives the final
fingerprint over the complete descriptor excluding only the fingerprint slot. Golden canonical JSON
fixtures and mutation tests lock this algorithm; a format change requires a new prefix version.

Use one discovery document per discoverable resource or operation. Do not embed one large module
document containing unrelated operations. Discovery text is built only from the safe fields
`Name`, `Summary`, `Aliases`, `Keywords`, `Tags`, `RoutingExamples`, input concepts, output concepts,
and explicit “when not to use” guidance.

Never embed credentials, secrets, protected payloads, arbitrary entity state, user memory, raw
script source, provider responses, journals, or external documents.

---

## 6. Contribution and resolution interfaces

Static module hooks implement a separate `ICatalogContributor` interface. `DigitalBrainRuntime`
instantiates only the hooks selected by `ModuleManifest` and registers that neutral immutable set as
`ConfiguredModuleHooks`; it does not reference Catalog. `CatalogModule` collects contributions from
that exact set, so loaded-but-unconfigured modules cannot enter the catalog. The contribution seam
remains separate from `IModule`, preserving its single configuration responsibility.

Dynamic aggregates publish descriptor mutations after their canonical revision commits. The common
source contract supports:

- enumeration of stable source partitions for a full rebuild;
- a repeatable, paged snapshot per partition with an opaque snapshot token and captured high
  watermark;
- ordered, gap-free mutation replay after that snapshot through an explicitly captured later
  watermark;
- exact resolution by source ID and revision;
- current-lifecycle resolution without following a stale handle; and
- idempotent mutation notification.

`ICatalogSource` implementations own domain translation. The catalog application layer owns
validation, fingerprinting, metadata projection, semantic documents, ranking, and hydration. A
source never receives a query embedding or Qdrant client.

A `CatalogSourcePartition` identifies one source kind, opaque partition ID, and exact platform or
owner scope. A snapshot item carries its descriptor and source position. All pages bearing one
snapshot token and high watermark describe the same point-in-time partition, and the terminal page is the only proof
that enumeration completed; an incomplete or failed page sequence never authorizes pruning. After
the snapshot, the coordinator captures the source's current position and replays mutations from the
snapshot watermark through that inclusive barrier before publishing readiness. Mutation replay is
strictly ordered and gap-free; compaction or a gap returns `SnapshotRequired` and restarts that
partition from a new snapshot rather than guessing. `CatalogMutation` carries the partition key so
checkpointing and pruning never infer partition membership from a descriptor payload.

The configured static source exposes one immutable platform partition. A future durable owner
directory enumerates every registered owner partition through the same internal rebuild contract;
normal owner-scoped discovery still reads only the projection and cannot request another owner's
partition. This makes all-owner rebuild possible without assembly scans or grain activation scans.
`CatalogSourcePosition.Origin = (0, 0)` means pre-history and cannot identify a mutation. The first
mutation is `(0, 1)`; successors advance sequence by one or advance epoch by one and restart at
sequence one. Therefore an empty snapshot captured at `Origin` cannot skip a concurrently published
first mutation when catch-up uses an exclusive lower bound.

---

## 7. Discovery query and result

`DiscoveryQuery` contains natural-language text plus structural constraints:

- allowed catalog kinds;
- stable operation/capability ID and version when known;
- signal alias/schema hash when routing compatibility is required;
- input/output schema identifiers or hashes when known;
- required tags;
- lifecycle and availability requirements;
- result limit and cursor.

The owner is taken from the trusted caller/application context, never from model-supplied query
text. A query searches platform descriptors plus that owner's overlay and no other owner.

Each `DiscoveryCandidate` contains:

- exact `CatalogReference` (`Scope`, `Source`, `DescriptorId`, `SourceRevision`, `Fingerprint`);
- kind, name, summary, typed reference, lifecycle, canonical configuration state, and a timestamped
  observed-availability overlay;
- compatibility evidence and matched exact fields;
- exact, lexical, and semantic rank positions where present;
- one-based `FinalRank` plus diagnostic `RrfScore`, explicitly not called confidence;
- deterministic rank-reason evidence.

The enclosing `DiscoveryResult.Diagnostics`, not every candidate, carries the metadata/availability
watermarks and snapshot tokens, active semantic generation, nullable semantic snapshot token,
candidate-pool truncation, and semantic-degradation status/reason. One result therefore describes
one coherent retrieval snapshot without duplicating global evidence into each candidate.

Empty results and ambiguous top candidates are normal. Discovery never chooses; it yields only a
provisional shortlist. A later selection policy may choose automatically only after one or more
successful authoritative inspections and only when exact unique structural evidence or an explicit
owner policy permits it; semantic rank alone is never sufficient. Otherwise it abstains or asks for
clarification. Any later execution persists a `SelectionDecision` containing the final inspected
handle, query fingerprint, structural evidence, policy version, and decision time before invoking.

---

## 8. Retrieval and ranking pipeline

The application service performs the following stages in order:

1. **Scope and lifecycle filter.** Keep only platform entries and the current owner overlay which
   satisfy visibility and lifecycle policy.
2. **Structural compatibility filter.** Apply requested kind, operation/capability version,
   signal/schema, input/output schema, tag, and invocability constraints.
3. **Exact lane.** Match canonical IDs, operation/capability IDs, names, and declared aliases. Exact candidates
   form a dominant rank group.
4. **Lexical lane.** Search a deterministic normalized-token inverted index and record matched
   fields/rank.
5. **Semantic lane.** Verify the alias generation matches the selected embedding profile, embed the
   query once, retrieve a bounded dense candidate set, and retain the vector rank and raw provider
   score for diagnostics.
6. **Hydration gate.** Resolve each candidate's current canonical pointer first and require an exact
   revision/fingerprint match before loading revision details. Drop stale or missing hits even when
   a source retains the old immutable revision.
7. **Availability gate.** Resolve one timestamped availability batch and apply an explicit
   availability requirement when present.
8. **Lane rerank.** Preserve lexical order; normalize semantic hits by descending similarity then
   canonical scope key and descriptor ID for exact-score ties; assign new contiguous one-based
   ranks. Removed hits never consume rank and provider-specific tie order never affects a cursor.
9. **Rank fusion.** Fuse lexical and semantic *rank positions* using Reciprocal Rank Fusion with
   `k = 60`. Raw lexical and cosine scores are never added.
10. **Deterministic rerank.** Exact group first, then structural specificity, active/available state,
   fused rank, and ordinal canonical scope key plus `DescriptorId` as the final tie-break.
11. **Bound and explain.** Return the requested page with evidence, watermark, and degradation state.

Each lexical/semantic lane uses `min(512, max(64, requestedLimit * 8))` candidates after validating
`1 <= requestedLimit <= 50`. Filling the bound is reported as truncation; discovery is deliberately
bounded and does not claim exhaustive nearest-neighbor enumeration.

The total order uses these preferences: exact descriptor ID, exact operation/capability ID, exact
name/alias, no exact match; descending count of explicitly matched compatibility facets; lifecycle
`Active`, `Draft`, `Suspended`, `Retired`; configuration `Configured`, `Declared`, `Disabled`;
availability `Available`, `Degraded`, `Unknown`, `Unavailable`; descending RRF; then scope sort key
`0:platform` or `1:owner:<owner-id>` and descriptor ID ordinal. Same-ID platform and owner entries
coexist and remain visibly ambiguous; neither silently shadows the other.

A discovery cursor is base64url canonical JSON bound to the trusted owner, normalized query fields,
metadata snapshot token, availability snapshot token, active semantic generation, semantic snapshot
token, the last candidate's global `FinalRank`, and complete final sort tuple. The availability token covers a fresh registry incarnation
plus its effective ordered observations, so a process restart cannot reuse an old numeric watermark
for different availability ordering. The semantic token covers generation, deployment epoch and
manifest fingerprint, active collection incarnation, projected metadata watermark, and exact
metadata-snapshot fingerprint, so same-generation catch-up,
static-content replacement, or delete/recovery invalidates old cursors. Query defaults are applied
before fingerprinting, and the recorded tuple must still exist at that recorded global rank after
deterministic recomputation. Any mismatch or fabricated tuple
returns `StaleCursor` with no candidates. A semantically degraded response emits no resumable
cursor, and a cursor presented while semantic readiness is degraded is stale. A cursor never
silently resumes against a different owner, catalog snapshot, availability ordering, semantic
contents, or embedding generation.

Before semantic evidence or a cursor can be returned, the ready semantic snapshot's projected
metadata watermark and metadata-snapshot fingerprint must equal the exact immutable metadata
snapshot used for the exact/lexical lanes. A mismatch discards semantic hits, reports semantic
degradation, wakes reconciliation, and suppresses the cursor; a supplied cursor is stale.

The initial implementation performs RRF in application code. It uses a small deterministic lexical
index and dense Qdrant search. It does not adopt a provider-specific sparse encoder or pretend the
current .NET vector abstractions provide a portable sparse model. Qdrant remains behind
`ISemanticCatalogIndex`.

Owner preference and learned graph priors may later reorder this already-compatible set within a
strict bound. They can never introduce a candidate removed by stages 1–2 or outrank an exact ID.

---

## 9. Inspect, select, invoke, and replay

The assistant-facing `inspect` request uses one stable discriminated `InspectionReference`
envelope rather than one tool per resource type. Its variants are `CatalogDescriptor`
(`CatalogReference`), `Neuron` (`NeuronId`), `Synapse` (source/target `NeuronId` plus signal type),
`Entity` (`EntityId`), and `DurableResource` (resource kind, stable ID, and optional exact revision).
Construction requires exactly the payload matching the discriminant. The trusted owner context is
not supplied by the model and every provider verifies that an embedded owner matches it.

An inspection router dispatches that envelope by composite provider key: ordinary variants use
`(InspectionReferenceKind, null)`, while durable variants use
`(DurableResource, normalized ResourceKind)`. It rejects duplicate exact keys, so independent
script, automation, agent, run, and activity modules can coexist instead of competing for one
catch-all durable slot. This slice ships only the catalog-descriptor provider; unsupported routes
return `UnsupportedReference` instead of invented data. Later neuron, synapse, entity, and durable
providers extend the router without adding assistant tools or changing existing reference fields.
The result is a versioned discriminated envelope so kind-specific payloads can be appended using new
serializer field IDs without renumbering the established catalog payload.

Catalog `inspect(handle)` performs no vector search. The handle's source reference routes directly
to the source resolver. It first resolves the current item for the same source/scope/descriptor ID
and compares every handle field. Only an exact current-pointer match may load immutable details by
revision. Thus a durable source may retain historical revision N for replay without allowing N to
inspect as current after the pointer advances to N+1. It returns one of:

- exact descriptor details;
- `StaleDescriptor` when the source exists at another revision;
- `Retired` when the exact canonical revision is retired; or
- `NotFound` when the source is gone or the handle was fabricated.

A different current revision is `StaleDescriptor`; the same revision with a different fingerprint
is tampering and returns `NotFound` without disclosing a replacement handle.

Live `Unavailable` or `Unknown` is observation data accompanying an otherwise `Found` inspection;
it does not turn an exact canonical descriptor into a missing one. `invoke` later rechecks whether
the selected operation is usable.

Discovery candidates are provisional. The assistant or routing policy inspects one or more exact
handles, then chooses under the automatic-selection gate in §7 using the inspected authoritative
state. A durable command which will execute work persists a `SelectionDecision`, the chosen exact
handle, operation reference, input, operation ID, and relevant projection watermark before
dispatch. Recovery reuses that selection; it does not run semantic search again and silently choose
a different operation.

`invoke` is intentionally outside this slice. Its later implementation accepts only an exact
operation handle and caller-supplied idempotent operation ID, resolves the canonical operation
manifest, validates input schema, admits a run or command, derives/checks the run-scoped capability
lease, and rechecks authority at the effect/provider boundary.

An admitted run also pins the immutable operation-manifest artifact/fingerprint and binding
revision. A deployment must retain a compatible binding for that pinned revision or recovery stops
with a deterministic `IncompatibleDeployment` result; it must not resolve a newer manifest and
continue silently.

---

## 10. Projection, recovery, and model migration

Dynamic canonical state changes produce idempotent catalog mutations; configured static authority
uses complete snapshot replacement:

```text
MutationId
Scope
DescriptorId
Source
SourceRevision
SourceEpoch
SourceSequence
Fingerprint
Upsert | Tombstone
DescriptorArtifactReference or immutable descriptor payload
```

Publication commits canonical state and an outbox item. Projection is asynchronous and at least
once. The projector:

1. compares `(SourceEpoch, SourceSequence)` lexicographically and rejects an older source position;
2. treats a duplicate mutation ID as success only when its canonical bytes and position match,
   treats an identical payload at the same position as success, and faults any reuse conflict;
3. applies metadata/lexical upsert or tombstone;
4. builds and embeds the safe discovery document outside the definition aggregate turn;
5. writes the semantic record idempotently;
6. advances a durable owner/shard checkpoint and watermark only after projection work is durable;
   and
7. retries from the pending outbox/reminder until acknowledged.

Metadata is the visibility gate. A tombstone immediately removes an entry from exact/lexical
results; a lagging stale vector point is rejected during hydration. Qdrant point identity is
deterministic from index generation, canonical scope key, and descriptor ID. Revision, fingerprint,
and source position live in payload, so a new revision overwrites the old point and a tombstone
deletes it instead of accumulating stale high-score records.

An embedding profile contains:

```text
GenerationId
ProviderId
ModelId
ModelRevision
Dimensions
PreprocessingVersion
DiscoveryDocumentFormatVersion
```

The collection name includes `GenerationId`; a stable alias identifies the active generation.
Every semantic read verifies that the alias control record's generation equals the caller's selected
embedding profile before creating or submitting a query embedding. An old but otherwise `Ready`
alias is a typed generation mismatch: discovery degrades, emits no cursor, and wakes reconciliation;
it is never queried with the new profile, including when both models happen to use the same vector
width. Any provider, model ID, operator-pinned model revision, dimension, preprocessing, or
document-format change creates a new generation. Rebuild
enumerates configured platform contributions and every owner directory, catches up to the source
watermark, validates counts and dimensions, then atomically switches the active alias. The old
generation remains during a rollback grace period.

Qdrant build and alias mutation are serialized by one Orleans coordinator keyed by a canonical hash
of connection name plus active alias—not by generation, because old and new generations share that
alias. `DigitalBrain:Catalog:DeploymentEpoch` is a positive, monotonically increasing operator value
(development/testing default `1`, required explicitly in production) which every silo in one rollout must share. Increase it whenever
the selected embedding profile or configured static contribution manifest changes; an intentional
rollback also uses a new higher epoch. The ready control record persists the epoch and deployment
manifest fingerprint, so restart never relies on volatile coordinator memory. A lower epoch is
`Superseded`; the same epoch with another generation/profile or static manifest is a configuration
conflict; only a higher epoch may replace it after full validation.

Each reconciliation intent pins that epoch, the complete embedding profile, configured-manifest
fingerprint, discoverable-metadata watermark/fingerprint, and ordered partition snapshot
token/high-watermark set. Before any Qdrant mutation, the coordinator captures its local immutable
discovery metadata snapshot and profile and requires exact equality with the intent. A grain activated on an old rolling-upgrade
silo therefore returns `IncompatibleCoordinator`, requests deactivation, and writes nothing; the
caller retries with bounded backoff until a compatible silo services it. This first production slice
has only the immutable platform partition. When durable owner partitions ship, the activation state
adds their checkpoint vector and accepts same-epoch catch-up only when every source position is
equal or advances; regression/incomparability requires a new unified snapshot.

If the semantic index is lost, full enumeration rebuilds it. The initializer keeps a low-frequency,
cancellation-aware reconciliation loop after startup, and a missing-collection/alias result from a
semantic query wakes that loop immediately. During recovery, exact and lexical discovery remain
available with semantic degradation; the connection/alias-keyed deployment coordinator recreates the
physical collection, a new collection-incarnation marker, payload indexes, points, and alias from
canonical descriptors. The semantic snapshot token combines deployment epoch/manifest identity and
that incarnation with the projected metadata watermark and metadata-snapshot fingerprint; it changes after same-generation catch-up,
static replacement, and loss/recovery. Direct inspection and exact invocation do not depend on
Qdrant.

A full source-partition enumeration computes the exact desired stable-point set for its
`Discoverable` descriptors, then prunes descriptor points absent from that set before the semantic
control record becomes ready. `InspectOnly` descriptors remain in the authoritative current view
but never contribute semantic points or coverage. A failed or partial enumeration never authorizes
pruning that partition. The provider-neutral semantic port
therefore exposes update-scoped partition record enumeration and requires expected per-partition
count/content fingerprints at commit; Qdrant scrolling is an adapter detail, and no provider may
publish `Ready` without rechecking coverage. If an owned physical collection
exists without a valid control record—for example, after a crash between collection creation and
the marker upsert—the coordinator removes that exact incomplete collection/alias edge, recreates it
with a fresh incarnation, and rebuilds from authority rather than adopting unknown points.

Epoch and sequence are non-negative integers. A dynamic source owns them durably; `(0, 0)` is the
pre-history origin, mutations start at sequence one, and an epoch increment restarts at sequence
one. A scoped descriptor ID is bound to one source; another source
claiming it is a projection integrity fault. Static platform contributions are rebuilt as a complete
validated immutable snapshot and swapped atomically, so removal is reconstructed from current
authority after restart rather than depending on an in-memory epoch/tombstone inventory. The
metadata projection retains a complete current-entry view for inspection and publishes a separate
immutable `Discoverable` snapshot for all retrieval lanes. That discovery snapshot has a
deterministic fingerprint over its ordered exact descriptor references, so semantic state can
distinguish different searchable canonical contents even when a numeric watermark repeats after
restart.

The first implementation slice has only static platform sources in production and therefore
rebuilds them idempotently on startup. It implements the mutation/source contracts and tests owner
overlays using fakes. Durable owner-directory outboxes and checkpoints arrive with the first
script/automation definitions which need them; they must follow this protocol rather than inventing
a second registry.

---

## 11. Relationship to neurons, signals, events, and synapses

- A **neuron type** is catalogued from a configured manifest, including its stable grain type and
  handled signal contracts.
- A **named neuron instance** becomes catalogued only through the owner directory. Grain activation
  alone is not registration.
- A **signal contract** is catalogued with its stable alias and schema hash. A signal occurrence is
  journal/activity data and is observed, not embedded as self-knowledge.
- An **event** has no extra substrate meaning: domain events which cross the graph are signals;
  persisted occurrences belong to journals or aggregate history.
- A **synapse** remains a source-owned route with provenance and learned weight. It can be inspected
  and later contribute a bounded ranking prior, but it is not an operation descriptor or lease.
- An **entity** remains passive state. It opts into a safe descriptor when discoverability is useful;
  arbitrary entity payloads are not indexed.

Assistant intent and signal routing share descriptor/index infrastructure but not policy:

```text
assistant intent: discover -> inspect one or more -> choose -> persist selection -> invoke
signal routing: exact signal contract -> compatible target candidates -> policy chooses -> deliver
```

Future Tier-3 signal routing runs only on a true Tier-1/Tier-2 miss, only over targets declaring the
exact signal alias/schema, and only when enabled. A selected target creates or strengthens a
`Discovered` synapse only after it returns `Handled`; `Unhandled`, `Refused`, failure, stale
resolution, or authorization failure learns nothing.

---

## 12. Evolution scenarios

### Installing a module

Its configured hook registers a stable module contribution containing module, neuron-type, signal,
and operation descriptors. Startup validation rejects duplicate IDs, invalid schemas, or a declared
neuron/signal contract which does not match the configured implementation. Unconfigured loaded
modules remain absent.

### Publishing a script

`ScriptDefinition` commits an immutable published-program revision and an outbox mutation. The
owner-scoped descriptor points to that exact revision; it does not contain source or vectors. A new
published revision creates a new descriptor revision. Existing runs keep their pinned prior handle.
Typed script wrappers are generated from exact canonical operation manifests and schema hashes,
never from semantic search results.

### Publishing an automation

`AutomationDefinition` publishes an owner-scoped descriptor plus an exact trigger-registry update.
The trigger registry maps stable signal contracts to published automation revisions; the semantic
catalog only helps a human or assistant find the automation. Rolling forward creates a new immutable
revision and catalog mutation.

### Creating an agent

A reusable `AgentDefinition` contributes a descriptor. An ephemeral delegated agent is only a
durable run/activity unless explicitly saved. Its selected context, model policy, operation handles,
and leases are pinned into the run; recovery does not rediscover them.

### Removing or retiring something

The authority changes lifecycle or publishes a tombstone. Exact resolution refuses it immediately;
projection cleanup follows idempotently. A stale vector hit cannot resurrect it.

---

## 13. Approaches considered

### 13.1 Rejected: reuse `VectorMemoryNeuron`

This is superficially quick but conflates user memory with trusted platform metadata, exposes the
wrong write semantics, drops vector scores, lacks source revisions and embedding profiles, and makes
rebuild/visibility rules ambiguous. Reuse operational patterns, not the public aggregate.

### 13.2 Rejected: make Qdrant the registry

This makes a disposable projection authoritative, couples exact operation use to index health,
makes model migration an identity migration, and encourages payload filters to become an
authorization boundary.

### 13.3 Rejected as the primary query path: live fan-out to every module and aggregate

This preserves ownership but creates unbounded fan-out, inconsistent ranking, partial failure, and
repeated embedding work. Source enumeration and exact resolution remain necessary for rebuild and
hydration, but not for every search.

### 13.4 Chosen: federated authorities plus one CQRS discovery projection

Modules and durable aggregates own canonical descriptors. A unified owner-aware exact, lexical, and
dense projection provides fast ranked retrieval. Every result is hydrated back through the source
resolver before it is returned as a provisional candidate; `inspect` then re-resolves the exact
handle before final selection.

---

## 14. Project and dependency boundaries

| Project | Responsibility | Dependency rule |
|---|---|---|
| `DigitalBrain.Modules.Catalog.Contracts` | Wire-safe descriptors, handles, queries, candidates, inspection results, and source/contribution abstractions. | References kernel contracts only; no Qdrant, MEAI, executors, or provider code. |
| `DigitalBrain.Modules.Catalog.Sdk` | Strongly typed descriptor builders and canonical schema/fingerprint helpers. | Depends on Catalog.Contracts and kernel contracts; kernel projects never depend on it. |
| `DigitalBrain.Modules.Catalog.Client` | Optional owner-bound `discover`/catalog-`inspect` facade. | Depends on Catalog.Contracts; does not widen kernel `IDigitalBrain`. |
| `DigitalBrain.Modules.Catalog` | Validation, its own typed contribution, source registry, metadata/lexical projection, ranking, discovery grain/service, projection coordination. | Depends on Catalog.Contracts/SDK, kernel runtime hooks, and AI.Contracts for its adapter/model profile; Qdrant is behind an internal interface. |
| `DigitalBrain.Modules.Catalog.Aspire.Hosting` | Qdrant resource/config projection for the catalog collection. | Contains no catalog/ranking logic. |
| `DigitalBrain.Modules.AI.Contracts` / `AI` | Selected embedding model descriptor and `IEmbeddingGenerator`. | Does not own catalog records or ranking. |
| Configured product modules | Explicit safe descriptor contributions and later executor bindings. | Do not call Qdrant or store embeddings. |
| Durable definition modules | Owner-scoped source resolution and outbox mutations. | Aggregate commits do not wait on embeddings or Qdrant. |
| Memory module | User vector memory only. | Cannot write or query the self-knowledge collection through its public API. |
| Execution module | Exact operation resolution, leases, runs, and effects. | Never treats a candidate or score as authority. |

Catalog wire contracts remain in their own module package. `IBrainNeuron` is not widened; neither
kernel contracts nor the kernel runtime depend on Catalog. The kernel runtime exposes only its
neutral `ConfiguredModuleHooks` set. `DigitalBrain.Modules.Catalog.Client` forwards to the separate
owner-keyed `ICatalogDirectory` grain, while kernel `IDigitalBrain` remains unchanged so Catalog is
genuinely optional. Catalog runtime references AI.Contracts only for the temporary
`IAgentToolSource` adapter and selected embedding-profile identity; ranking remains independent of
the assistant implementation.

The initial provider uses the already-pinned `Microsoft.Extensions.AI` 10.9.0 and `Qdrant.Client`
1.19.0. It does not add `Microsoft.Extensions.VectorData.Abstractions`: dense search, payload filters,
scores, collections, and alias swaps are already available through the Qdrant client, while lexical
ranking is deliberately provider-neutral application code.

---

## 15. First implementation boundary

The focused implementation plan delivers:

1. wire-safe descriptor, handle, query, candidate, evidence, status, catalog inspection, and stable
   multi-kind inspection-envelope contracts;
2. explicit static module contributions and validation for configured modules only;
3. deterministic descriptor canonicalization and SHA-256 fingerprints;
4. an authoritative static source resolver plus owner-overlay source interface;
5. exact and deterministic lexical retrieval;
6. explicit selected-embedding profile registration and dimension validation;
7. an internal semantic-index port with in-memory fake and Qdrant implementation;
8. idempotent static projection/rebuild, atomic startup readiness, and a versioned catalog
   collection;
9. compatibility filtering, stale-hit hydration, RRF ranking, deterministic tie-breaking, and rank
   evidence;
10. owner-scoped `discover` and exact catalog `inspect` through the client/application surface;
11. assistant adapters for `discover` and `inspect` while legacy domain tools remain during the
    invoke migration; and
12. module/neuron-type/signal/operation descriptors sufficient to prove self-knowledge end to end.

Deferred to the dependent durable scripting plan:

- journaled owner directory and descriptor outbox/checkpoint implementation;
- actual script, automation, reusable-agent, entity, activity, and named-neuron-instance sources;
- exact `invoke` and durable `observe` implementations;
- replacement of legacy `IAgentToolSource` domain tools with catalog-backed `invoke`;
- generated catalog manifests and replacement of `SignalHandlerIndex`'s temporary reflection scan;
- automatic Tier-3 signal routing and preference/synapse learning; and
- provider-specific sparse encoders or learned rerankers.

---

## 16. Verification

### Contract and source tests

- every wire type has stable Orleans serializer metadata;
- descriptor IDs, source revisions, and fingerprints are non-empty and deterministic;
- duplicate IDs or conflicting revisions fail startup validation;
- capability definitions are discoverable but cannot be used as capability leases;
- only configured modules contribute descriptors;
- static source enumeration and exact resolution agree;
- owner A sees platform plus owner A entries and never owner B entries;
- user vector-memory entries never enter catalog results.

### Ranking tests

- structural incompatibility removes a candidate before ranking;
- an exact stable ID/name/alias wins over a semantically similar description;
- lexical and semantic ranks use RRF with `k = 60` rather than raw-score addition;
- ties end in ordinal canonical-scope-key plus descriptor-ID order;
- every candidate exposes matched fields and rank components;
- no result causes invocation, signal delivery, lease creation, or synapse mutation;
- embedding failure returns exact/lexical results with degraded status;
- a stale semantic hit is removed by authoritative hydration;
- empty and ambiguous results remain explicit.
- cursor reuse across owners, metadata/availability watermarks, or semantic generations returns
  `StaleCursor` rather than silently repaging a changed result set.
- metadata/semantic snapshot equality is required before semantic evidence or a cursor is returned;
- availability snapshot tokens include a per-registry incarnation, so an equal numeric watermark
  after process restart does not validate an old cursor;
- same-generation semantic catch-up and collection loss/recovery change the semantic snapshot token;
  degraded results have no next cursor and every pre-transition cursor becomes stale.

### Projection/provider tests

- duplicate upsert is idempotent;
- lower source positions are ignored, identical same-position mutations are idempotent, and
  conflicting same-position payloads fault;
- observed availability can change without changing a descriptor fingerprint or semantic record;
- stale/tombstoned metadata prevents a lingering vector point from appearing;
- deterministic point identity includes scope, descriptor ID, and generation but deliberately not
  revision/fingerprint, so revision replacement overwrites one point;
- owner/global payload filters preserve owner isolation;
- emitted vector width must match the profile;
- changing model ID, dimensions, preprocessing, or document format changes generation;
- discovery before the first atomic snapshot returns `Initializing`, never a partial result;
- loss of the catalog collection is automatically recoverable by static source enumeration after a
  periodic reconciliation or a missing-collection/alias signal;
- every physical semantic collection has an incarnation marker excluded from descriptor searches;
- active-generation switch occurs only after the rebuild watermark is complete.

### End-to-end tests

- the assistant can discover every configured module's explicitly declared public neuron types and
  handled signal contracts, plus a capability definition and operation by intent;
- the assistant may inspect multiple provisional candidates without this slice selecting or
  executing one; the dependent invoke slice persists the final selection;
- catalog `inspect` returns the exact requested descriptor revision and unsupported future reference
  kinds fail explicitly without changing the tool schema;
- a fabricated or stale handle is refused;
- a wrong semantic top hit remains only a candidate and performs no action;
- build and all tests remain green with zero warnings.

---

## 17. Definition of done

The slice is complete when DigitalBrain can accurately describe and search its configured static
capabilities, return only owner-visible structurally compatible ranked candidates, resolve exact
handles without consulting the vector store, continue exact/lexical discovery during embedding or
Qdrant failure, rebuild a deleted semantic projection from canonical sources, and prove that search
has no path to execution or graph learning.

The durable scripting slice may then publish its definitions and generate typed wrappers against
this exact catalog without changing the assistant's stable discovery model.
