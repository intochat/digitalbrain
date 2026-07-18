# DigitalBrain Reactive Kernel

Status: Draft specification for review

Architecture direction approved: 2026-07-18

## Decision

Reactivity is intrinsic to `Brain.Kernel`, not a feature module.

A neuron is a durable Orleans virtual actor with stable identity, domain lifecycle, one journal, typed capabilities, command handling, event reactions, observable projections, and optional Agent Framework compatibility.

Point-to-point commands use direct grain calls. Broadcast events use persistent Orleans streams. Both synapse forms are immutable typed data; neither contains logic.

The kernel owns persistence, idempotency, authorization enforcement, delivery, subscription recovery, causal limits, projections, and owner feeds. Modules own domain decisions and providers. Edges own HTTP, MCP, and Agent Framework protocols.

## Goals

- Durable neuron-to-neuron reactions across activation and silo failure.
- A small universal neuron API with multidimensional metadata.
- Independent `Gpt56Neuron`, `Grok45Neuron`, and `GroupChatNeuron` instances.
- Microsoft Agent Framework Group Chat and checkpoints, not a custom turn loop.
- Correct Flutter replay and live updates after every committed change.
- Typed Gmail and Salesforce agents and MCP tools.
- Aspire client resources for Gateway, MCP, AgentGateway, and DevUI.
- End-to-end telemetry without sensitive content.

## Non-goals

- A generic workflow engine, in-memory event bus, or ordinary `ReactionNeuron`.
- Scripts, expressions, reflection targets, or prompts in reaction bindings.
- Business behavior from Orleans runtime activation.
- Exactly-once transport or cross-stream total ordering.
- An IAW-style God `IAgent` base.
- AppDomain-wide discovery.
- Provider, Agent Framework, MCP, Flutter, or Aspire types in kernel contracts.

## Invariants

1. One authoritative journal append represents each successful mutation.
2. Receipts, pending deliveries, reaction cursors, lifecycle, and projections are journal folds.
3. Snapshots and owner feeds are rebuildable projections.
4. No external effect occurs before its requesting commit is durable.
5. Every retryable transmission has a stable identity.
6. Domain handlers return transitions; they never persist, publish, call grains, or update feeds.
7. `OnActivateAsync` restores infrastructure only and invokes no domain behavior.
8. Dynamic reactions use explicit persistent Orleans subscriptions.
9. Durable target cursors make event handling effectively-once.
10. Every causal chain and transition has finite budgets.
11. Failures are typed, journaled or traced, and never swallowed.

## Runtime shape

```text
MCP / HTTP / AgentGateway / Neuron
                |
                | CommandSynapse<T>, direct grain call
                v
          Universal NeuronGrain
                |
                | one commit
                v
             Journal
          /      |       \
 direct command |        projection intent
        persistent       |
        event stream     v
             |       OwnerFeedGrain --> Gateway --> Flutter
             v
       target NeuronGrain
             |
             | ReactAsync
             v
          one commit
```

## Contracts

### `INeuron`

```csharp
public interface INeuron
{
    Task<NeuronDescriptor> DescribeAsync();
    Task<NeuronProjection> ReadAsync(NeuronReadRequest request);
    Task<NeuronEventPage> ReadEventsAsync(NeuronEventQuery query);
    Task<NeuronReply> InvokeAsync(NeuronCommandEnvelope command);
}
```

All mutations use `InvokeAsync`. Typed clients construct and validate envelopes. Event reads are paginated and authorized.

### Descriptor

`NeuronDescriptor` composes data facets:

- `Identity`: address, kind, owner or space, generation, revision.
- `Lifetime`: domain status, activation, dormancy, retirement, retention.
- `Persistence`: durability, journal format, snapshot and retention state.
- `Capabilities`: commands, queries, reactions, effect permissions.
- `Execution`: concurrency, causal, cost, rate, and retry budgets.
- `Observation`: emitted contracts, projections, visibility.
- `Agent`: optional Agent Framework compatibility and interaction modes.

Orleans activation is infrastructure state, not domain lifetime.

### `INeuronKind`

Trusted, explicitly registered kinds expose:

```text
Describe
HandleCommandAsync
ReactAsync
Project
```

Handlers receive immutable state and context and return `NeuronTransition` containing domain events, reply, command/event/effect intents, projection changes, and lifecycle changes. The kernel validates and commits it.

### Synapses

- `CommandSynapse<T>` is immutable data addressed to one target and delivered through a direct grain call.
- `EventSynapse<T>` is an immutable fact from one source and published through a persistent stream.

Common metadata includes message ID, contract and version, source and generation, optional target, journal revision, stream sequence, caller, correlation, causation, root activation, hop budget, and creation time.

When an external caller sends a command, `Brain.Client` calls the target directly. When a neuron emits a command, its commit contains an outbox intent and the kernel later performs the same direct call.

## Persistence and dispatch

The journal is the only write model. A commit coherently records:

- accepted command or reaction input;
- domain events and reply receipt;
- outgoing command, event, and provider-effect intents;
- projection publication intents;
- lifecycle changes;
- reaction cursor advancement.

Materialized state is replayed from commits and optionally accelerated by snapshots. Dispatch acknowledgements, effect outcomes, paused bindings, and dead letters are subsequent journal records.

After commit, the kernel attempts immediate dispatch. Pending work stays derivable from the journal until acknowledged. A non-durable timer handles short retries while active; an Orleans reminder is the durable backstop for dormant grains. Reminder-driven draining is recovery plumbing, not domain behavior.

Direct calls remain at-most-once per attempt. Stable command IDs and durable receipts make caller retries effectively-once.

## Reactions

### Stream topology and ordering

Stream identity is derived from source address, source generation, event contract, and contract major version. This permits contract-level authorization and per-source, per-contract order.

Each stream has a monotonic sequence. The source outbox does not advance past an unacknowledged head item for that stream. No ordering is promised across streams.

Broadcast channels are excluded from correctness-critical behavior.

### `ReactionBinding`

A target-owned binding stores:

- binding ID;
- source address and generation;
- event contract and accepted versions;
- reaction ID declared by the target kind;
- source authorization grant or public marker;
- status;
- causal and rate limits.

It stores no executable mapping. The target kind owns the typed reaction implementation.

Binding creation validates target support, source observation policy, grant, and contract compatibility before committing the binding. The kernel then creates or resumes the explicit persistent subscription.

Domain bindings are journal data. Orleans subscription handles are runtime metadata reconciled during activation. Implicit subscriptions are reserved for fixed infrastructure mappings, not dynamic relationships.

Retirement, source-generation mismatch, revoked authorization, or incompatible contracts pause the binding and produce a visible fault. Nothing is silently garbage-collected.

### Delivery

The target kernel:

1. resolves and validates the binding;
2. checks source generation, version, authorization, and status;
3. checks identity, sequence, watermark, hop, rate, and cost budgets;
4. calls the target kind's typed `ReactAsync`;
5. commits its transition and cursor together;
6. acknowledges delivery.

Sequence handling:

- sequence at or below watermark: acknowledge duplicate without applying;
- next sequence: apply;
- sequence gap: pause that binding and recover the authorized missing range before continuing.

If acknowledgement is lost after commit, redelivery observes the cursor and does not repeat domain behavior.

## Activation and loops

`OnActivateAsync` may load snapshots, replay commits, rebuild indexes, reconcile subscriptions, restore timers, and register a reminder for committed pending work. It may not invoke kind handlers or emit business events.

Domain activation is an explicit command and committed event such as `NeuronActivated`.

Each reaction-produced transmission preserves correlation and root activation, sets causation to the input message, and decrements a hop budget. The kernel also enforces maximum effects and payload per transition, binding rate limits, neuron concurrency, retry ceilings, dead letters, circuit breaking, and provider token or cost budgets.

Budget exhaustion creates a visible typed event such as `CausationChainExhausted`. The kernel does not attempt global graph-cycle detection.

## Live UI

A transition declares projection changes. The source commit records a publication intent, not a client push.

The outbox sends the intent to an `OwnerFeedGrain`. The feed deduplicates by message ID, assigns a monotonic owner cursor, stores bounded change entries, and identifies the affected neuron, projection contract, and revision.

`Brain.Gateway` authenticates the user and streams from the last acknowledged cursor. Flutter persists its cursor, replays missed entries after reconnect, refreshes affected projections, and then resumes live watching.

The feed is rebuildable. The neuron journal remains authoritative. MCP, DevUI, provider effects, commands, and reactions all use this one path.

## AI and Agent Framework

### Named model neurons

`Gpt56Neuron` and `Grok45Neuron` are separate kinds and instances with independent address, journal, session, instructions, tools, grants, provider configuration, budgets, and projections.

They may share an internal `ModelNeuronRuntime`; implementation reuse does not collapse identity or lifecycle.

### Adapter

`NeuronAIAgentAdapter` uses `Brain.Client` to adapt an eligible neuron address to Agent Framework's `AIAgent`. It cannot access neuron storage or module internals. Framework session and checkpoint types remain at this boundary.

### Group chat

`GroupChatNeuron` stores participant neuron addresses, group policy, iteration and termination settings, conversation state, checkpoint bytes, lifecycle, and projection.

Microsoft Agent Framework owns turn selection, shared-history synchronization, streamed workflow events, termination, checkpointing, and resume. DigitalBrain owns participant authorization, persistence, budgets, reactions, and projections.

Participants remain separate neurons and sessions. Framework checkpoint encoding lives in `Brain.AgentFramework`, not in kernel contracts.

## Gmail, Salesforce, and MCP

Gmail exposes typed mailbox, message, search, draft, send-proposal, and approved-send capabilities. Salesforce exposes typed account, contact, lead, opportunity, search, mutation-proposal, and approved-mutation capabilities.

Credentials live in connection neurons or host credential services. They never enter MCP payloads, Flutter projections, prompts, synapses, traces, or logs.

Provider mutations follow:

```text
propose -> authorize or approve -> claim -> execute -> record outcome
```

A stable effect ID survives retries and is used as provider idempotency key where supported. Uncertain outcomes are reconciled before retry.

`Brain.Mcp` is one edge process with segregated Brain, Gmail, and Salesforce tool classes. Production MCP exposes typed capabilities, not unrestricted contract IDs or generic `Invoke`.

Caller identity originates from the authenticated MCP request and flows through `Brain.Client`. Hardcoded owner, actor, session, or connection addresses are forbidden.

## Aspire playground

```text
kernel-host
gateway            .AsClient(kernel-host)
mcp                .AsClient(kernel-host)
agent-gateway      .AsClient(kernel-host)
devui              .WithAgentService(agent-gateway)
flutter-ui         references gateway
```

`DigitalBrain.Hosting` supplies `AddDigitalBrain`, `.AsClient`, shared Orleans client references, health, and telemetry composition.

DevUI is development-only and excluded from deployment. It connects through `Brain.AgentGateway`, never directly to module implementations.

## Projects and dependencies

```text
core/
  Brain.Contracts
  Brain.Kernel.Abstractions
  Brain.Kernel
  Brain.Client

modules/
  Brain.Modules.AI.Contracts
  Brain.Modules.AI
  Brain.Modules.Google.Contracts
  Brain.Modules.Google
  Brain.Modules.Salesforce.Contracts
  Brain.Modules.Salesforce

integrations/
  Brain.AgentFramework

edge/
  Brain.Gateway
  Brain.Mcp
  Brain.AgentGateway

hosting/
  DigitalBrain.Hosting

hosts/
  Brain.Kernel.Host
  DigitalBrain.AppHost

workspace/
  digital_brain_flutter
```

Dependency direction:

- Kernel depends only on kernel abstractions and contracts.
- Kernel abstractions depend only on contracts.
- Module implementations depend on their contracts and kernel abstractions.
- Agent Framework integration depends on `Brain.Client` and AI contracts.
- Edges and hosts depend on clients, contracts, and composition.
- Core never depends on modules, integrations, edges, hosts, or UI.

Contract assemblies are justified by multiple consumers: implementations, MCP, Gateway or UI, AgentGateway, tests, and other neurons.

AppHost contains composition only. Provider callbacks and business behavior belong in edges or modules.

## Discovery, security, and telemetry

The host supplies explicit module assemblies or generated registrations. Startup rejects duplicate kind, command, event, reaction, projection, or serializer registrations. AppDomain scanning is forbidden.

Commands, bindings, replay, reads, and effects are authorized. Caller context contains subject, tenant or owner, actor, session, grants, and trace context. Protected event contracts require a source-approved grant.

OpenTelemetry propagates W3C context through edge, Orleans call, commit, outbox, stream or provider, feed, and Gateway. Metrics cover duration, retries, deduplication, pending-outbox age, dead letters, binding gaps, feed lag, provider outcomes, LLM tokens and cost, MCP, and AgentGateway.

Prompts, model responses, email or CRM content, credentials, tokens, and direct personal identifiers are never logged or traced. High-cardinality identities are not metric labels.

## Failure contract

| Failure | Required result |
|---|---|
| Caller times out after commit | Same command ID returns the durable receipt |
| Silo stops before dispatch | Reminder-backed recovery drains the intent |
| Event is duplicated | Target cursor suppresses re-execution |
| Event has a sequence gap | Binding pauses and replays the missing range |
| Handler fails before commit | Delivery remains retryable |
| Commit succeeds but acknowledgement is lost | Redelivery sees the committed cursor |
| Feed delivery fails | Publication intent remains pending |
| Feed append is duplicated | Owner feed deduplicates it |
| Source generation changes | Binding pauses with a visible fault |
| Causal budget is exhausted | Typed exhaustion event is committed |
| Provider result is uncertain | Reconcile by stable effect ID |
| Group chat stops mid-turn | Resume from the durable checkpoint |

## Delete or rename

Delete:

- any proposed Reactions project;
- generic reactivity in the Flutter module;
- generic production MCP invocation;
- hardcoded development callers;
- separate post-commit feed writes with swallowed failures;
- AppDomain-wide discovery;
- provider callbacks in the kernel host;
- domain behavior tied to Orleans activation.

Replace relational `SynapseRecord` with precise `ReactionBinding`, `NeuronReference`, `GrantRecord`, `ParticipantReference`, or `DependencyReference`. Reserve synapse for typed transmission data.

## Proof order

1. **Journal core:** command, receipt, replay, snapshot, descriptor, explicit kind catalog. Kill after commit, restart, retry, and return the original receipt.
2. **Reactive kernel:** outbox, streams, bindings, cursors, gap recovery, reminders, and causal limits. Kill between commit and dispatch; the target reacts once after restart. Verify a bidirectional loop terminates visibly.
3. **Owner feed:** feed grain, Gateway cursor API, Flutter replay. Disconnect Flutter, mutate through MCP and another neuron, reconnect, and replay every change.
4. **Model neurons and DevUI:** independent Gpt56 and Grok45 neurons, adapter, AgentGateway, `.AsClient`, and development DevUI.
5. **GroupChat:** authorized participants, Agent Framework workflow, checkpoint and resume. Kill mid-turn and continue without duplicating a committed turn.
6. **Gmail:** connection, typed reads, proposal and approved send, typed MCP. Retry an uncertain approved send and record one outcome.
7. **Salesforce:** connection, typed reads, proposal and approved mutation, typed MCP. Retry and prove durable idempotency.
8. **Hardening:** production persistence, auth and privacy audit, retention, compaction, reconciliation, load, restart, and fault injection.

## Acceptance

- Reactivity has no feature-module dependency.
- Source commit survives termination before dispatch.
- Duplicate commands and events do not repeat domain behavior.
- Dynamic bindings survive deactivation.
- Runtime activation performs no business behavior.
- Causal loops terminate visibly.
- Flutter reconnects and replays committed changes.
- Gpt56 and Grok45 remain independent.
- GroupChat uses Agent Framework and durable checkpoints.
- Gmail and Salesforce mutations use the approved effect rail.
- MCP, Gateway, and AgentGateway are Aspire Orleans clients.
- DevUI is development-only.
- Kernel contracts contain no provider or Agent Framework types.
- Telemetry contains no sensitive content.
- No failure is swallowed.

## References

- Orleans delivery guarantees: <https://learn.microsoft.com/dotnet/orleans/implementation/messaging-delivery-guarantees>
- Orleans stream APIs: <https://learn.microsoft.com/dotnet/orleans/streaming/streams-programming-apis>
- Orleans broadcast channels: <https://learn.microsoft.com/dotnet/orleans/streaming/broadcast-channel#broadcast-channels-vs-streams>
- Orleans monitoring: <https://learn.microsoft.com/dotnet/orleans/host/monitoring/>
- Agent Framework Group Chat: <https://learn.microsoft.com/agent-framework/workflows/orchestrations/group-chat>
- Agent Framework checkpoints: <https://learn.microsoft.com/agent-framework/workflows/checkpoints>
- Aspire Agent Framework DevUI: <https://aspire.dev/reference/api/csharp/aspire.hosting.agentframework.devui/agentframeworkbuilderextensions/methods/>
