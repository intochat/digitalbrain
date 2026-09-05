# Neuron and agent architecture assessment

**Implementation update (2026-09-05):** the approved [foundation and specialist-module plan](../plans/2026-09-05-neuron-foundation-and-specialist-modules.md) has been implemented. The baseline findings below are retained as the original assessment; links to removed helpers describe the reviewed commit. Request policy, awaited-cycle guards, agent/tool separation, provider specialists and graph metadata now have regression coverage. Diagnostic `AgentActivity` remains journal evidence and is excluded from subscription choices; no new broadcast lifecycle contract was introduced. See the plan's implementation record for validation.

Reviewed branch `codex/day-zero-scripting`, commit `d0a09c26360a8bc1b5262efca94bec44bb0c09e2` (`With IAspire`), against its parent and the current architecture in `CONTEXT.md` and `docs/JOURNALS.md`.

CodeGraph was used to inspect the request, delivery, subscription, agent, and MCP relationships. Two independent source reviews covered the kernel and module boundaries. This is a source assessment: the findings below were not reproduced by running new tests. No application code or running services were changed.

## Assessment

The implementation largely follows the neuron/synapse architecture. The new `Neuron.RequestAsync` uses the existing source-owned delivery path. It does not introduce another router, fabricate a subscription, or route nested agent requests through the busy owner root. `IAspire : IAgent` and native MCP tool discovery are the right abstractions for this feature.

The main weaknesses are coupling request completion to an observation cursor, inconsistent request semantics across entry points, and too many responsibilities in `Agent`. The next iteration should tighten these boundaries rather than replace the substrate.

## What the current primitives mean

| Primitive | Current meaning | Architectural implication |
|---|---|---|
| `Send` | Deliver a typed signal to one explicit neuron; successful handling reinforces a Learned synapse. | A request can start without a subscription. |
| `Request` | Send and obtain a causally associated reply. | This is a convenience over delivery, not a second routing system. |
| `SubscribeTo` | Ask the source to bind an outgoing edge to the subscriber. | The source owns the Bound synapse; it does not decay. |
| `UnsubscribeFrom` | Ask the source to remove the current target/signal edge. | This is not a persistent prohibition on future delivery. |
| `Broadcast` | Deliver along the source's active synapses, including Learned and Bound edges. | It does not search for every `IHandle<T>` implementation. |
| `RecordOutgoingAsync` | Record journal evidence and notify journal watchers. | It does not deliver a signal to subscribers. |

The unsubscribe distinction matters: a later successful direct send can create a Learned edge again, restoring broadcast eligibility. That behavior predates this commit and is covered by the existing broadcast specification. Changing to Bound-only broadcasts or persistent unsubscribe suppression would be a separate behavioral decision, not routine cleanup.

## Findings and proposed changes

### 1. Fix retained replies being mistaken for compacted replies

**Priority: P2. Introduced by the new request helper.**

`RequestAsync` saves a target outgoing-journal cursor before delivery, then reads after it when the handler finishes. If enough activity was emitted during that handler, the original cursor falls outside retention. `JournalWindow.Read` returns an empty delta and a reset snapshot. `NeuronResponse.Read` treats that as a lost reply, even when the final reply is still retained.

For example, a request emits enough tool/activity records to compact the beginning of its turn, then records its final reply. The request currently fails despite that reply being present. Either the 512-entry limit or the 512-KB limit can cause this.

Evidence: [request lookup](D:/digitalbrain/src/Kernel/DigitalBrain/Neuron/Neuron.cs:130), [journal reset behavior](D:/digitalbrain/src/Kernel/DigitalBrain/Neuron/JournalWindow.cs:46), [response rejection](D:/digitalbrain/src/Kernel/DigitalBrain/Neuron/NeuronResponse.cs:26).

**Proposed change:** on reset, perform a bounded read from the earliest retained sequence and use the existing exact-causation matcher. Explicitly handle another reset without an unbounded retry loop. If retained-reply lookup becomes a recurring need, expose that bounded query directly; do not make the journal an unbounded reply store.

**Acceptance:** activity before a retained reply succeeds; an actually evicted reply fails clearly; an unrelated reply with the same correlation cannot satisfy the request. The existing eviction test covers activity after the reply and should gain the inverse case.

This is a source-confirmed failure path, not a claim that it caused the earlier Aspire incident.

### 2. Share request policy while preserving the two entry points

**Priority: next kernel cleanup. Existing and new behavior diverge.**

The activation-local helper matches the target sender, exact request causation, and response type. The external facade instead watches/polls the owner root's incoming journal and matches correlation plus type. Correlation identifies a conversation chain and can contain multiple requests; it is a weaker response identity. The entry points also use different deadline behavior and repeat delivery-outcome validation.

Evidence: [activation-local request](D:/digitalbrain/src/Kernel/DigitalBrain/Neuron/Neuron.cs:100), [exact matcher](D:/digitalbrain/src/Kernel/DigitalBrain/Neuron/NeuronResponse.cs:9), [facade response loop](D:/digitalbrain/src/Kernel/DigitalBrain.Contracts/DigitalBrainClientTransport.cs:278).

**Proposed change:** share the response identity rule, outcome mapping, and deadline policy. Preserve separate external-root and activation-local transports. A nested agent request must continue to originate from its actual neuron; putting both paths through the root would reintroduce serialized reentry problems.

Keep `RequestAsync` as a small neuron capability. Extract mechanics only where that creates real reuse; moving the same code into another class is not sufficient simplification. Document that the present local helper expects the target to record its reply during handling. Deferred workflows need an explicit signal/subscription or journal-observation flow.

### 3. Extend awaited-hop protection beyond nested requests

**Priority: kernel correctness hardening. Newly added protection is partial.**

`NeuronRequestPath` guards entry into `RequestAsync`. A mixed chain such as `A.Request(B) -> B.Send(A)` bypasses that check on its return hop. `B.SubscribeTo(A)` can similarly await a source-owned binding mutation on the busy A activation. These paths can occupy the chain until cancellation or timeout instead of failing promptly.

Evidence: [request guard](D:/digitalbrain/src/Kernel/DigitalBrain/Neuron/NeuronRequestPath.cs:13), [awaited signal delivery](D:/digitalbrain/src/Kernel/DigitalBrain/Neuron/SignalSender.cs:62), [source binding call](D:/digitalbrain/src/Kernel/DigitalBrain/Neuron/Neuron.cs:287).

**Proposed change:** centralize nested awaited-hop tracking at delivery and binding boundaries. Preserve intentional in-process self-delivery and detached replies. Retain deadlines: propagated call-path checks cannot prove the absence of every cycle between independently started operations.

**Acceptance:** request/request, request/send, and request/subscription cycles fail promptly; normal self-delivery and directed replies still work; cancellation does not leave the sender continuing to mutate its activation state after its turn has ended.

### 4. Make subscribable behavior events distinct from diagnostic observations

**Priority: required before promising lifecycle subscriptions to custom behaviors.**

`AgentActivity` inherits `Signal`, but all its producers call `RecordOutgoingAsync`. A receiver can have the appropriate handling capability and a Bound edge yet receive no `AgentActivity` deliveries from these producers. The graph works because it observes journals.

Evidence: [activity contract](D:/digitalbrain/src/Modules/AI/Contracts/AgentActivity.cs:9), [agent producers](D:/digitalbrain/src/Modules/AI/AI/Agent.cs:95), [tool producer](D:/digitalbrain/src/Modules/AI/AI/Agent.Mcp.cs:22), [journal-only recording](D:/digitalbrain/src/Kernel/DigitalBrain/Neuron/SignalSender.cs:126).

This matches the approved UI-evidence scope. It is a capability gap for subscription-based automation, not a regression against that scope.

**Proposed change:** define which generic lifecycle facts form a stable behavior contract, then publish those through existing `BroadcastAsync`. Start with terminal agent outcomes if a concrete behavior needs them. Keep detailed tool progress, previews, and telemetry as observations. Do not broadcast every diagnostic record or introduce Aspire-specific status request DTOs.

The UI should only offer subscriptions with meaningful delivery semantics. Also distinguish a directed request reply from a lifecycle notification: `AgentReply` should remain associated with its caller.

**Acceptance:** a bound behavior receives the intended event once; after unsubscribe it receives none unless a later explicit operation establishes another eligible edge; graph evidence remains visible even with zero subscribers. Source-owned synapses remain the only routing authority.

### 5. Give `Agent` one tool preparation path

**Priority: main simplification.**

`Agent` now combines the inherited request handler, model streaming, MCP discovery, principal checks, catalog observations, SDK-specific tool detection, result screening/redaction, function wrapping, and telemetry. Partial files organize this code but do not reduce its responsibilities.

There are also overlapping extension paths: `Tools`, `IAgentToolSource`, and `IAgentMcpTools` with `McpAgentTools`. The compatibility defaults on `IAgentToolSource` allow an incomplete implementation to silently contribute no tools.

Evidence: [agent preparation](D:/digitalbrain/src/Modules/AI/AI/Agent.cs:28), [tool source contract](D:/digitalbrain/src/Modules/AI/Contracts/IAgentToolSource.cs:8), [MCP adapter](D:/digitalbrain/src/Modules/AI/AI/Tools/McpAgentTools.cs:8), [MCP execution wrapper](D:/digitalbrain/src/Modules/AI/AI/Agent.Mcp.cs:12).

**Proposed change:** converge on one required asynchronous tool-source method accepting the actual turn context. Ordinary sources return immediately; MCP sources perform discovery. Compose screening, safe evidence, and failure classification at the tool boundary. Keep `Agent` responsible for its signal handler and model turn, and preserve the narrow, expiring source-bound delegation capability.

Retain native MCP schemas, target-owned catalogs, principal isolation, restricted continuation checks, stale-catalog rejection, and cancellation. These protect real behavior and should survive cleanup.

The same boundary should preserve safe failure categories. Currently [the broad exception replacement](D:/digitalbrain/src/Modules/AI/AI/Agent.Mcp.cs:49) collapses unavailable transport, catalog changes, and content rejection into one message. Export a small stable classification and correlation while keeping raw transport details out of model-visible errors.

Before turning Gmail and Salesforce into agents, reuse shared MCP session and transport mechanics beneath discovered tools. The existing authenticated HTTP client and new STDIO discovery client are a migration seam; avoid copying either into a third client or deleting authentication safeguards during consolidation.

## Proposed implementation order

1. **Request correctness:** fix retained-reply lookup, align response identity policy, and add the missing mixed-hop cases. Run the existing neuron request, substrate, and agent delegation tests plus focused regressions.
2. **Agent simplification:** migrate tool sources to one asynchronous contract, move MCP execution concerns to that boundary, and preserve safe failure identity. Verify discovered schemas, owner isolation, cancellation, screening, and AI telemetry content.
3. **Behavior subscriptions:** implement the first concrete lifecycle subscriber using existing source-owned broadcast semantics. Verify subscribe/unsubscribe delivery and graph visibility with no subscribers.
4. **Naming and documentation:** explain Learned versus Bound broadcast behavior in the graph; consolidate current architecture guidance; remove obsolete compatibility paths after migrating callers. Review the preexisting targeted `PublishAsync` name against `SendAsync` rather than adding another synonym.

## Boundaries to retain

- `Neuron` owns identity, state, its journals, and outgoing synapses.
- Typed signals and `IHandle<T>` express receiving capabilities.
- Directed requests and explicit subscriptions serve different purposes.
- `Agent` supplies a reusable `AgentRequest`/`AgentReply` contract.
- `Aspire` supplies its identity, instructions, and MCP connection setup. Operational tool schemas remain MCP-owned.
- The graph projects observed execution and actual synapses; it does not create topology to illustrate an imagined request route.
- Journals remain bounded observation windows. OpenTelemetry remains the detailed diagnostic record. Long-running behaviors should not depend on one actor turn or indefinite journal retention.

The recommended next step is the request-correctness pass, followed by tool-source consolidation. This produces a smaller and more consistent architecture without changing the domain model or introducing another orchestration framework.
