# CoreV2 Journal-First Chat Design

**Date:** 2026-08-14

**Status:** Approved for autonomous implementation

## Outcome

`aspire start` launches a headless DigitalBrain whose production behavior is the CoreV2 neuron, journal, outbox, and BrainGraph model. MCP can open a chat turn, the AI assistant can invoke a versioned Operation, every meaningful firing is durably journalled, and the UI protocol streams both the journal and resulting BrainGraph changes. The optional Flutter window renders the same live streams as a real chat experience with an interactive graph and journal inspector.

The existing working Aspire process topology, ProductHost authority checks, and Flutter launch mechanics are retained. The parallel `Brain.Runtime` operation runner, module-specific CRUD grains, generic operation browser, and empty marker-only UI module are removed or replaced.

## Product Truth

The CoreV2 dictionary remains canonical:

- a **Neuron** thinks in one serialized durable turn;
- a **DomainEvent** records what happened;
- a **Synapse** is a versioned junction in **BrainGraph**;
- a **BrainActivity** groups one stretch of work;
- an **Operation** is a versioned, authorized ingress intent;
- an **Entity** is current typed belief, never a substitute for the journal;
- journals are the mine from which usage, activity, and topology projections are derived.

There is one production runtime. ProductHost, MCP, headless Dart, and Flutter are adapters over that runtime. No adapter owns durable brain state.

## Source Structure

Projects that change together live together. The target source tree is:

```text
src/CoreV2/
  Aspire/
    Hosting/
    Runtime/
    ServiceDefaults/
  Hosts/
    AppHost/
    RuntimeHost/
    ProductHost/
  Kernel/
    Abstractions/
    Runtime/
    Testing/
  Modules/
    Proof/
      Contracts/
      Runtime/
    AI/
      Contracts/
      Runtime/
      Aspire.Hosting/
    UI/
      Contracts/
      Runtime/
      Aspire.Hosting/
      Flutter/
        core/
        kit/
        shell/
    Introspection/
      Contracts/
      Runtime/
```

Tests mirror this shape under `tests/CoreV2`. Contract projects contain only typed contracts. Runtime projects contain module behavior. Aspire hosting projects contain only external resource projections and configuration injection. Flutter code belongs to the UI module rather than a top-level parallel tree.

Conversation is part of UI because chat is an ingress and presentation workflow, not an independent data store. Introspection owns safe journal, activity, and topology read projections. Scheduling, Behavior, and Memory are not carried forward as CRUD product modules; they return later as journal-first Neurons and Capabilities.

## Aspire Resource Model

`AddDigitalBrain("brain")` provisions persistent Azurite in development with distinct resources:

```text
storage
  ├─ clustering   Azure Tables
  ├─ reminders    Azure Tables
  ├─ grainstate   Azure Blobs
  └─ journal      Azure Blobs
```

The runtime reference receives Orleans plus the named `journal` connection. Runtime startup registers Azure Blob journal storage and fails with a clear error when `ConnectionStrings:journal` is absent. Generic grain state does not masquerade as the neuron journal.

The DigitalBrain aggregate and all four backing resources are visible in `aspire describe`. Runtime waits for their health. ProductHost waits for RuntimeHost. The UI headless/window resource waits for ProductHost.

## Durable Runtime

The existing CoreV2 proof semantics become production Orleans behavior rather than being copied into another runtime:

1. An Operation opens or resumes one workspace-scoped BrainActivity using caller idempotency.
2. Core resolves the Operation's entry Neuron role and direct-sends the typed input.
3. A Neuron turn atomically commits state, inbound/outbound journal records, and route-snapshot outbox entries through Orleans journaling.
4. Emit resolves live Synapses from BrainGraph; Send bypasses BrainGraph.
5. Outbox delivery is at-least-once with deterministic delivery identity and receiver deduplication.
6. Zero-route emissions remain in the journal and BrainActivity and create no fabricated delivery.
7. BrainGraph installs, replaces, and retires Synapses durably while preserving revision history.

`Brain.Runtime`, `Brain.Runtime.Abstractions`, `IRuntimeProductModule`, `ProductActivityGrain`, and module-specific product executors are deleted once ProductHost is bound to the production Core interface.

## Journal Mining and Live Projections

Introspection reads journals and BrainGraph without mutating either. It exposes policy-filtered projections for:

- ordered journal records with Neuron, direction, contract, firing, cause, activity, principal, timestamp, route count, and delivery outcome;
- the current BrainGraph with Neuron endpoints and live Synapses;
- Synapse revision history and provenance;
- live pulses derived from journaled deliveries;
- per-contract and per-Synapse usage tallies derived between turns.

Mining never runs inside a Neuron turn and never writes BrainGraph implicitly. A checkpointed projector may cache read models, but the journal and BrainGraph remain authoritative.

ProductHost exposes snapshot plus resumable SSE streams. Reconnection uses monotonically increasing sequence numbers and cannot cause another brain action.

## AI Module

The AI module is migrated by behavior, not by namespace replacement:

- `Contracts` defines typed model and assistant contracts without provider SDK objects on the Core bus.
- `Runtime` provides durable LLM/Assistant Neurons, streaming response handling, durable conversation context, and CoreV2 tool invocation.
- `Aspire.Hosting` owns a persistent Ollama resource and the `gemma4:12b` model, injects its endpoint/model, and makes RuntimeHost wait until the model resource is healthy.
- the assistant tool surface is constant: discover eligible Operations/Capabilities, inspect policy-safe live topology, and invoke an Operation/Capability through Core; it cannot mutate journals or forge caller identity.
- assistant text deltas, tool selection, Operation invocation, DomainEvents, deliveries, and terminal response are journalled under one BrainActivity.

Provider calls are behind a narrow chat-model seam. Tests use a deterministic adapter. Live acceptance uses the configured local Ollama model when available; absence is a startup/setup failure for the product composition, not a fake Ready module.

Voice and multi-agent orchestration from master are migrated after the primary assistant chat is green. They remain inside the AI module and reuse the same journal semantics; they do not block the first journal/chat cutover.

## UI Module and Flutter

`UiModule` becomes a real runtime module. It owns Chat and presentation-facing projection contracts, not merely the Flutter launcher.

The Flutter family contains:

- `core`: pure Dart protocol client, reconnecting SSE reducers, chat/journal/topology models, and the headless executable;
- `kit`: reusable chat, graph, journal timeline, status, and layout widgets;
- `shell`: desktop/web composition using the kit.

The graph control carries forward the useful master interaction model—directed edges, pulses, rotation, selection, and inspection—but binds only to CoreV2 Neurons and Synapses. It does not display V1 broadcast aliases or connection DTOs.

The primary screen has three coordinated regions:

```text
┌──────────────── chat transcript ────────────────┬──── live BrainGraph ────┐
│ streamed user/assistant/tool turns              │ Neurons + Synapses      │
│ composer                                         │ firing pulse + history  │
├──────────────── journal timeline ───────────────┴─────────────────────────┤
│ ordered records for the selected BrainActivity / Neuron / Synapse         │
└────────────────────────────────────────────────────────────────────────────┘
```

Selecting a chat turn filters the graph and journal to its BrainActivity. Selecting a Neuron or Synapse filters the journal. New journal records animate the matching graph pulse; a Synapse install/replace/retire updates the graph without a refresh.

## Headless Default

AppHost reads `DigitalBrain:UI:HostKind`, accepting `headless`, `window`, or `web`, and defaults to `headless`. The headless Dart executable connects to ProductHost, verifies module/protocol readiness, subscribes to resumable journal and graph streams, and reports health without opening a window.

Window and web hosts remain explicit options over the same package and protocol. Development documentation gives one configuration switch for opting into the desktop window. Headless mode has no Flutter VM-service hot reload; window/web retain the dashboard hot-reload command.

## ProductHost and MCP

ProductHost keeps authentication, workspace derivation, HTTP/SSE, and MCP transport responsibilities. Its product interface is narrowed to:

```text
GET  /health
GET  /v2/operations
POST /v2/operations/{operationId}:invoke
GET  /v2/activities/{activityId}
GET  /v2/activities/{activityId}/journal
GET  /v2/activities/{activityId}/journal/events
GET  /v2/brain
GET  /v2/brain/events
POST /mcp
```

MCP exposes product tools for Operation discovery/invocation, activity observation, chat send/read, journal read, and brain snapshot read. MCP never exposes raw credentials, arbitrary grain addressing, or direct journal writes.

## Live Acceptance Scenario

The migration is complete only when one isolated headless Aspire run proves all of this through public adapters:

1. `aspire describe` shows healthy `storage`, `clustering`, `reminders`, `grainstate`, `journal`, RuntimeHost, ProductHost, AI model, and headless UI resources.
2. MCP opens a chat turn asking the assistant to wire the Proof route to assessment and run a supplied value.
3. The assistant discovers and invokes `Proof.Wire@1`, then invokes `Proof.Run@1`, rather than fabricating either result.
4. MCP observes the terminal assistant reply.
5. MCP reads the BrainActivity journal and finds the user message, assistant/tool decision, Operation invocation, typed DomainEvent firing, Synapse resolution/delivery, and assistant response in causal order.
6. MCP reads the brain snapshot before and after and observes the new live Proof Synapse and its usage update.
7. The journal and graph streams expose the same change with increasing sequence numbers.
8. RuntimeHost is restarted; the chat, activity journal, and BrainGraph revision remain readable.
9. The headless UI resource remains healthy and no desktop window was launched.
10. The optional window host renders the chat, graph, and journal widget tests from the same recorded acceptance fixture.

## Deletion and Cutover Rules

- Do not preserve the incorrect runtime behind compatibility adapters.
- Do not add journal writes to CRUD grains.
- Do not copy V1 contract types or its broadcast topology.
- Delete obsolete projects only after their replacement slice is green and no active project references them.
- Keep every slice small, test-first, independently buildable, and committed.
- `status.md` must distinguish orchestration evidence from product-semantic evidence.
