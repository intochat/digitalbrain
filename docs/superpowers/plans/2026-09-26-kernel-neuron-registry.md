# Kernel Neuron Registry Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Inventory explicitly declared neuron contracts from selected modules and include routable entries in Discovery search.

**Architecture:** Kernel validates and exposes an immutable registry; modules contribute through an optional interface. Discovery indexes registry descriptors beside app manifests, while agent tools treat neuron hits as metadata only.

**Tech Stack:** .NET, Orleans, xUnit.

**Spec:** `docs/superpowers/specs/2026-09-26-kernel-neuron-registry-design.md`

## Global Constraints

- Work in branch `codex/kernel-neuron-registry-impl` in the existing checkout; do not create a worktree.
- Kernel has no dependencies on Apps, AI, Discovery, embeddings, or Qdrant.
- Preserve app manifest search and workspace isolation.

## Review Focus

- Unselected modules must not leak descriptors into the registry.
- Non-routable descriptors must remain readable in Kernel but absent from Discovery.
- Duplicate IDs and invalid contract types must fail during composition.
- Neuron hits must never add executable agent tools.
- Embedding failure must retain keyword search for both kinds of entry.

---

### Task 1: Kernel registry

**Files:** Create `Kernel/Registry` contract and implementation files; modify `BrainComposition`, `BrainCompositionBuilder`, `ModuleDefinition`, runtime hosting and test hosting paths; add Kernel unit tests.

**Interfaces:** Produce `INeuronRegistry`, `NeuronDescriptor`, `INeuronRegistryContributor`, and an immutable registry built from resolved modules.

- [x] Write tests for selected modules, lookup, immutability, invalid contracts, duplicate IDs, and deterministic IDs.
- [x] Run Kernel test filter and observe failure.
- [x] Implement and register the registry in composition and hosts.
- [x] Run Kernel tests and solution build; commit.

### Task 2: Discovery integration

**Files:** Modify `CapabilityCatalog`, `CapabilityIndex`, `ICapabilityCatalog`, `DiscoveryModule`, `DiscoveryTools`, `AgentToolSelection`; add Discovery and IntoChat tests.

**Interfaces:** Consume `INeuronRegistry`; return `CapabilityKind.Neuron` hits while keeping app behavior.

- [x] Write tests for routable indexing, non-routable exclusion, app isolation, keyword fallback, and tool selection behavior.
- [x] Run affected tests and observe failure.
- [x] Implement registry indexing and metadata lookup in the agent tool.
- [x] Run affected tests and solution build; commit.

### Task 3: Real module contributions and verification

**Files:** Add descriptors to selected real modules and update module inventory documentation.

**Interfaces:** Contribute stable IDs and contract types through `INeuronRegistryContributor`.

- [x] Write test proving real selected modules appear, and unrelated modules do not.
- [x] Run test and observe failure.
- [x] Add a small initial set of real descriptors; document coverage.
- [x] Run affected suites and solution build; commit.
