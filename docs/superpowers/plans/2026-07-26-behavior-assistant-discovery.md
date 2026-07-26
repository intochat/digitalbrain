# Behavior Assistant Discovery and Composition Implementation Plan

> **Status:** Designed/current. This stable index retains plan scope, constraints, order, and task navigation; it does not claim the Behavior rail is built.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give an in-brain AI assistant exact primitives to discover and invoke approved Behaviors and to submit new one-file Behavior proposals without gaining approval or installation authority.

**Architecture:** Source generation and installed manifests feed a deterministic, owner-filtered catalog projection. Discovery returns advisory IDs and match reasons; callers re-resolve the authoritative module/Behavior record and repeat schema/grant checks. Proposal compilation is durable/off-turn, while only the authenticated owner client can approve an exact verified digest and grant set.

**Tech Stack:** Existing module manifests/source generation, owner-scoped Behavior catalog neuron, `BackgroundService` admission pump, Behavior admission/sandbox services, `System.Text.Json`, existing Microsoft.Extensions.AI abstractions only where an actual assistant consumes the APIs.

## Global Constraints

- Search is not authority: every candidate is re-resolved by exact catalog ID before invocation or proposal dependency binding.
- The first implementation is a deterministic in-memory projection; do not add a vector database, vector abstraction, embedding package, or provider.
- Catalog descriptors are immutable projections and contain no executable delegates, `Type` instances, grain proxies, credentials, or owner-private payloads.
- Owner and visibility filtering occurs before scoring and repeats after exact resolution.
- An assistant may invoke installed approved intents and submit source/schema/BDD proposals.
- An assistant may not approve, install, replace, roll back, uninstall, widen grants, or select an active revision.
- Owner approval binds exact revision digest, compiler/admission/BDD policy versions, requested grants, and provenance evidence.
- Proposal submission returns a durable receipt; compilation/verification never holds the caller or a grain turn open.
- Program-to-Behavior invocation is a separately grantable system capability and uses the same receipt/outcome model as client or neuron invocation.
- Dynamic Behavior intent schemas remain JSON; only modules add public CLR vocabulary.

---

## Task Allocation and Required Order

1. [Task 1 — Generate exact module and synapse discovery descriptors](2026-07-26-behavior-assistant-discovery-catalog-and-proposal.md#task-1-generate-exact-module-and-synapse-discovery-descriptors)
2. [Task 2 — Add deterministic owner-filtered candidate discovery](2026-07-26-behavior-assistant-discovery-catalog-and-proposal.md#task-2-add-deterministic-owner-filtered-candidate-discovery)
3. [Task 3 — Add durable proposal submission and off-turn admission](2026-07-26-behavior-assistant-discovery-catalog-and-proposal.md#task-3-add-durable-proposal-submission-and-off-turn-admission)
4. [Task 4 — Bind human approval to exact revision and grants](2026-07-26-behavior-assistant-discovery-approval-invocation-and-proof.md#task-4-bind-human-approval-to-exact-revision-and-grants)
5. [Task 5 — Let neurons and programs invoke exact installed Behavior intents](2026-07-26-behavior-assistant-discovery-approval-invocation-and-proof.md#task-5-let-neurons-and-programs-invoke-exact-installed-behavior-intents)
6. [Task 6 — Prove the assistant contract and record the vector deferral](2026-07-26-behavior-assistant-discovery-approval-invocation-and-proof.md#task-6-prove-the-assistant-contract-and-record-the-vector-deferral)
