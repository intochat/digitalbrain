# DigitalBrain v2 — Self-Knowledge and Ranked Discovery

**Status:** Ratified in conversation; implementation plan follows  
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
> top vector result. The assistant or routing policy selects an exact typed reference, and a
> durable caller persists that selection before execution.

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
    -> assistant/policy selects an exact revisioned handle
    -> inspect(handle) resolves authoritative current state and schema
    -> invoke(operation handle + operation id) authorizes and executes
    -> observe(reference + cursor) follows durable progress
```

This slice implements the catalog, ranking, `discover`, and catalog form of `inspect`. It does not
implement automatic invocation, semantic signal delivery, or a second workflow engine.

---

## 2. Decisions and invariants

1. **The catalog is authoritative; the vector index is not.** Canonical descriptors come from
   configured module manifests and durable definition aggregates. Exact and semantic indexes are
   disposable projections.
2. **Discovery returns candidates, never actions.** No search result invokes an operation, sends a
   signal, grants a lease, or creates a synapse.
3. **Compatibility precedes ranking.** Owner visibility, lifecycle, kind, exact contract version,
   schema, and requested facets remove invalid candidates before lexical or semantic scores matter.
4. **Exact identity dominates similarity.** A matching stable ID, operation ID, or declared alias
   ranks before a merely similar description.
5. **Lexical and dense scores are not added together.** Candidate lists are fused by rank using
   Reciprocal Rank Fusion, followed by deterministic structural ordering.
6. **Every result is revisioned and explainable.** A candidate carries its exact descriptor handle,
   rank components, matched fields, availability summary, and projection watermark.
7. **`inspect` re-resolves authority.** A stale, retired, missing, or tampered handle fails
   explicitly. It never silently follows an active pointer to a different revision.
8. **Authorization stays separate.** Availability and lease eligibility may be described, but only
   `invoke` and the provider boundary authorize execution.
9. **User vector memory is a different domain.** `VectorMemoryNeuron` and its public namespaces are
   not the self-knowledge catalog and cannot write its index.
10. **Embeddings never enter aggregate state.** Neurons, definitions, scripts, automations, and
    entities store source data and exact references, not model-specific vectors.
11. **Model identity is part of index identity.** Provider/model ID, dimensions, preprocessing, and
    discovery-document format determine the index generation.
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
| **Discovery handle** | Stable descriptor identity plus exact source revision and fingerprint. | An Orleans proxy or mutable active pointer. |
| **Operation manifest** | Canonical operation/version, schemas, recovery semantics, and binding reference. | A run-scoped lease or search result. |
| **Capability lease** | Exact authority held by a run/caller. | A tag, synapse, prompt, similarity score, or provider connection. |
| **Trigger registry** | Exact signal-alias/schema routing for published automations. | The semantic catalog. |
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
Kind                Module | NeuronType | NeuronInstance | SignalContract |
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
Availability         declared/configured/connected/healthy summary
TypedReference       kind-specific opaque application reference
```

An operation descriptor additionally contains:

```text
OperationId and semantic Version
CapabilityId and capability Version
InputSchemaId + InputSchemaHash
OutputSchemaId + OutputSchemaHash
RecoverySemantics    ReplaySafe | Idempotent | Reconcileable | NonRecoverable
BindingReference     stable executor/application binding ID
RequiredScopes       declarative requirements, not a lease
```

The descriptor fingerprint excludes projection data: vectors, vector scores, token positions,
provider health samples, index generation, and projection checkpoints. Collection records include
the descriptor fingerprint so every hit can be checked against authoritative metadata.

Use one discovery document per discoverable resource or operation. Do not embed one large module
document containing unrelated operations. Discovery text is built only from the safe fields
`Name`, `Summary`, `Aliases`, `Keywords`, `Tags`, `RoutingExamples`, input concepts, output concepts,
and explicit “when not to use” guidance.

Never embed credentials, secrets, protected payloads, arbitrary entity state, user memory, raw
script source, provider responses, journals, or external documents.

---

## 6. Contribution and resolution interfaces

Static modules register immutable contributions during `IModule.Configure`. Because only module
types in `ModuleManifest` are configured, loaded-but-unconfigured modules cannot enter the catalog.
The contribution seam is separate from `IModule`, preserving its single configuration
responsibility.

Dynamic aggregates publish descriptor mutations after their canonical revision commits. The common
source contract supports:

- enumeration for full rebuild;
- exact resolution by source ID and revision;
- current-lifecycle resolution without following a stale handle; and
- idempotent mutation notification.

`ICatalogSource` implementations own domain translation. The catalog application layer owns
validation, fingerprinting, metadata projection, semantic documents, ranking, and hydration. A
source never receives a query embedding or Qdrant client.

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

- exact `DiscoveryHandle` (`DescriptorId`, `SourceRevision`, `Fingerprint`);
- kind, name, summary, typed reference, lifecycle, and availability;
- compatibility evidence and matched exact fields;
- exact, lexical, and semantic rank positions where present;
- final `RankScore`, explicitly not called confidence;
- deterministic rank-reason strings; and
- projection watermark and semantic-degradation status.

Empty results and ambiguous top candidates are normal. The assistant may choose automatically when
its policy has enough grounded evidence, or ask the user for clarification. Either way, it selects
an exact returned handle; discovery does not select or execute on its behalf.

---

## 8. Retrieval and ranking pipeline

The application service performs the following stages in order:

1. **Scope and lifecycle filter.** Keep only platform entries and the current owner overlay which
   satisfy visibility and lifecycle policy.
2. **Structural compatibility filter.** Apply requested kind, operation/capability version,
   signal/schema, input/output schema, tag, and invocability constraints.
3. **Exact lane.** Match canonical IDs, operation IDs, names, and declared aliases. Exact candidates
   form a dominant rank group.
4. **Lexical lane.** Search a deterministic normalized-token inverted index and record matched
   fields/rank.
5. **Semantic lane.** Embed the query once, retrieve a bounded dense candidate set, and retain the
   vector rank and raw provider score for diagnostics.
6. **Hydration gate.** Resolve every candidate against the canonical source and require an exact
   revision/fingerprint match. Drop stale or missing hits.
7. **Rank fusion.** Fuse lexical and semantic *rank positions* using Reciprocal Rank Fusion with
   `k = 60`. Raw lexical and cosine scores are never added.
8. **Deterministic rerank.** Exact group first, then structural specificity, active/available state,
   fused rank, and ordinal `DescriptorId` as the final tie-break.
9. **Bound and explain.** Return the requested page with evidence, watermark, and degradation state.

The initial implementation performs RRF in application code. It uses a small deterministic lexical
index and dense Qdrant search. It does not adopt a provider-specific sparse encoder or pretend the
current .NET vector abstractions provide a portable sparse model. Qdrant remains behind
`ISemanticCatalogIndex`.

Owner preference and learned graph priors may later reorder this already-compatible set within a
strict bound. They can never introduce a candidate removed by stages 1–2 or outrank an exact ID.

---

## 9. Inspect, select, invoke, and replay

`inspect(handle)` performs no vector search. It asks the source resolver for the exact source
revision and fingerprint and returns one of:

- exact descriptor details;
- `StaleDescriptor` when the source exists at another revision;
- `Retired` or `Unavailable` when the exact revision cannot be used; or
- `NotFound` when the source is gone or the handle was fabricated.

The assistant or routing policy chooses from the compatible ranked candidates. A durable command
which will execute work persists the chosen exact handle, operation reference, input, operation ID,
and relevant projection watermark before dispatch. Recovery reuses that selection; it does not run
semantic search again and silently choose a different operation.

`invoke` is intentionally outside this slice. Its later implementation accepts only an exact
operation handle and caller-supplied idempotent operation ID, resolves the canonical operation
manifest, validates input schema, admits a run or command, derives/checks the run-scoped capability
lease, and rechecks authority at the effect/provider boundary.

---

## 10. Projection, recovery, and model migration

Canonical state changes produce idempotent catalog mutations:

```text
MutationId
Scope
DescriptorId
Source
SourceRevision
Fingerprint
Upsert | Tombstone
DescriptorArtifactReference or immutable descriptor payload
```

Publication commits canonical state and an outbox item. Projection is asynchronous and at least
once. The projector:

1. rejects an older revision than the materialized descriptor;
2. treats duplicate mutation IDs and identical revisions as success;
3. applies metadata/lexical upsert or tombstone;
4. builds and embeds the safe discovery document outside the definition aggregate turn;
5. writes the semantic record idempotently;
6. advances a durable owner/shard checkpoint and watermark only after projection work is durable;
   and
7. retries from the pending outbox/reminder until acknowledged.

Metadata is the visibility gate. A tombstone immediately removes an entry from exact/lexical
results; a lagging stale vector point is rejected during hydration. Qdrant point identity is
deterministic from scope, descriptor ID, source revision, fingerprint, and index generation.

An embedding profile contains:

```text
GenerationId
ProviderId
ModelId
Dimensions
PreprocessingVersion
DiscoveryDocumentFormatVersion
```

The collection name includes `GenerationId`; a stable alias identifies the active generation.
Any model, dimension, preprocessing, or document-format change creates a new generation. Rebuild
enumerates configured platform contributions and every owner directory, catches up to the source
watermark, validates counts and dimensions, then atomically switches the active alias. The old
generation remains during a rollback grace period.

If the semantic index is lost, full enumeration rebuilds it. If embedding generation fails, exact
and lexical discovery remain available. Direct inspection and exact invocation do not depend on
Qdrant.

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
assistant intent: discover -> inspect -> choose -> invoke
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
resolver before it can be inspected or selected.

---

## 14. Project and dependency boundaries

| Project | Responsibility | Dependency rule |
|---|---|---|
| `DigitalBrain.Modules.Catalog.Contracts` | Wire-safe descriptors, handles, queries, candidates, inspection results, and source/contribution abstractions. | References kernel contracts only; no Qdrant, MEAI, executors, or provider code. |
| `DigitalBrain.Modules.Catalog` | Validation, canonical hashing, source registry, metadata/lexical projection, ranking, discovery grain/service, projection coordination. | Depends on contracts and abstractions; Qdrant is behind an internal interface. |
| `DigitalBrain.Modules.Catalog.Aspire.Hosting` | Qdrant resource/config projection for the catalog collection. | Contains no catalog/ranking logic. |
| `DigitalBrain.Modules.AI.Contracts` / `AI` | Selected embedding model descriptor and `IEmbeddingGenerator`. | Does not own catalog records or ranking. |
| Configured product modules | Explicit safe descriptor contributions and later executor bindings. | Do not call Qdrant or store embeddings. |
| Durable definition modules | Owner-scoped source resolution and outbox mutations. | Aggregate commits do not wait on embeddings or Qdrant. |
| Memory module | User vector memory only. | Cannot write or query the self-knowledge collection through its public API. |
| Execution module | Exact operation resolution, leases, runs, and effects. | Never treats a candidate or score as authority. |

The initial provider uses the already-pinned `Microsoft.Extensions.AI` 10.9.0 and `Qdrant.Client`
1.19.0. It does not add `Microsoft.Extensions.VectorData.Abstractions`: dense search, payload filters,
scores, collections, and alias swaps are already available through the Qdrant client, while lexical
ranking is deliberately provider-neutral application code.

---

## 15. First implementation boundary

The focused implementation plan delivers:

1. wire-safe descriptor, handle, query, candidate, evidence, status, and inspection contracts;
2. explicit static module contributions and validation for configured modules only;
3. deterministic descriptor canonicalization and SHA-256 fingerprints;
4. an authoritative static source resolver plus owner-overlay source interface;
5. exact and deterministic lexical retrieval;
6. explicit selected-embedding profile registration and dimension validation;
7. an internal semantic-index port with in-memory fake and Qdrant implementation;
8. idempotent static projection/rebuild and a versioned catalog collection;
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
- only configured modules contribute descriptors;
- static source enumeration and exact resolution agree;
- owner A sees platform plus owner A entries and never owner B entries;
- user vector-memory entries never enter catalog results.

### Ranking tests

- structural incompatibility removes a candidate before ranking;
- an exact stable ID/name/alias wins over a semantically similar description;
- lexical and semantic ranks use RRF with `k = 60` rather than raw-score addition;
- ties end in ordinal descriptor-ID order;
- every candidate exposes matched fields and rank components;
- no result causes invocation, signal delivery, lease creation, or synapse mutation;
- embedding failure returns exact/lexical results with degraded status;
- a stale semantic hit is removed by authoritative hydration;
- empty and ambiguous results remain explicit.

### Projection/provider tests

- duplicate upsert is idempotent;
- stale/tombstoned metadata prevents a lingering vector point from appearing;
- deterministic point identity includes scope, descriptor revision/fingerprint, and generation;
- owner/global payload filters preserve owner isolation;
- emitted vector width must match the profile;
- changing model ID, dimensions, preprocessing, or document format changes generation;
- loss of the catalog collection is recoverable by static source enumeration;
- active-generation switch occurs only after the rebuild watermark is complete.

### End-to-end tests

- the assistant can discover a module, a neuron type, a signal contract, and an operation by intent;
- `inspect` returns the exact selected descriptor revision;
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
