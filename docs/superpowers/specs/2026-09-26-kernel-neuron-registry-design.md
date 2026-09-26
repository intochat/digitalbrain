# Kernel neuron registry and capability discovery

Date: 2026-09-26  
Status: Implemented; startup registration revised at owner request.
Baseline: PR #104, `delivery/integration` at `83308a429`.

## Purpose

Give the brain an authoritative inventory of neurons supplied by selected modules, and let Discovery search the subset suitable for agent routing. An unselected module must not appear merely because its assembly was loaded. Preserve the existing manifest catalog and workspace isolation for apps.

## Ownership and seams

`Kernel/Registry` owns immutable neuron descriptors and the lookup interface. A descriptor has a stable ID, the `INeuron` contract type, owning module ID, human name, description, and `AgentRoutable` flag. It describes a neuron **contract**, not every grain instance or workspace key. Registry IDs are stable across process starts; no random GUIDs or assembly-wide scanning. The registry contains all descriptors contributed by selected modules, including those not offered to agents.

Modules contribute descriptors explicitly through an optional Kernel interface on their `IModule` implementation. `BrainComposition.Build()` collects contributions from its resolved module definitions, validates that contract types implement `INeuron`, rejects blank fields and duplicate IDs, and produces an immutable local snapshot. At silo startup, `NeuronRegistrationStartupTask` publishes serializable records from that snapshot to a persistent Kernel registry grain. Startup waits for publication; a failure prevents the silo from becoming ready. Kernel never references Apps, AI, Discovery, an embedding provider, or Qdrant. Modules without contributions remain valid.

The registry grain supports listing descriptors and resolving one by ID. It does not rank text, authorize an invocation, construct grain references, or expose executable tools. Agent routing eligibility is a declaration, not permission to call a neuron; existing enforcement remains authoritative. A stable content hash is the grain key and makes publication idempotent. Different snapshots use separate grains, so silos with different module sets cannot overwrite each other during a rolling deployment. Discovery uses the snapshot version selected by its local silo.

## Discovery

Discovery reads the published Kernel registry grain as a second catalog input alongside `IManifestSource`. It indexes only `AgentRoutable` neuron descriptors, using their stable IDs, names, and descriptions. App manifests and operations remain authoritative in the app manifest directory. Registry entries are global; app entries retain their current workspace visibility rules. Search still returns IDs and kinds, with a new `Neuron` kind for registry results. Consumers resolve neuron details through Kernel and app details through the manifest directory.

The existing warmup, invalidation, keyword fallback, embedding behavior, and unmet-intent recording remain Discovery responsibilities. Static registry entries require no change-feed subscription; rebuilding after a manifest signal incorporates the same registry snapshot. The index signature includes registry descriptor fields so an updated composition is reflected after restart. Vector persistence must use stable IDs and avoid stale results from removed entries; it cannot be treated as the source of truth.

## Agent integration

`find_capability` may return neuron IDs and identify them as `Neuron`. The agent must read registry metadata before presenting or selecting a neuron. `AgentToolSelection` may use Discovery hits to find relevant *apps*, but a neuron hit alone does not create an `AIFunction` or bypass the existing tool policy. The present hard-coded app-to-tool mapping is separate work; this refactor must not silently claim that a registry entry makes a tool executable. There is no IAW-style planning agent in this change.

## Migration and verification

First add the Kernel contribution and immutable lookup, then contribute a small set of real neuron contracts from selected modules, then index them in Discovery and surface them through `find_capability`. Keep the current app manifest search behavior throughout. Document which modules have contributed contracts and avoid claiming the inventory is complete until all selected modules are migrated.

Tests cover selected versus unselected modules, duplicate IDs, invalid contracts, deterministic IDs, non-routable visibility in the registry but absence from search, neuron and app result kinds, workspace app isolation, manifest invalidation, keyword fallback when embeddings fail, and the rule that a neuron search hit does not add an executable agent tool. Run affected Kernel, Discovery, and IntoChat tests plus a solution build.

## Out of scope

Per-instance neuron discovery, live grain enumeration, automatic reflection scanning, an LLM team selector, a replacement for app manifests, and a general rewrite of agent tool registration.
