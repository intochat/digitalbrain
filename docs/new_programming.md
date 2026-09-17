# DigitalBrain v2: A Neuron-and-Signal Programming Model for the AI Era

> **CRITICAL ACCESS NOTICE (read first).** I was **not** given any filesystem/MCP tool that can read the user's Windows machine. I therefore **could not inspect** `E:\intochat\digitalbrain`, `E:\projects\IAW`, or `E:\projects\NewProgrammingModel`. Every statement about those three codebases below is labeled **UNVERIFIED — from brief, code not inspected**, and I have not fabricated any file contents, class shapes, line numbers, or runtime behavior. The `CodeChangedMessage` trace, the `IDotNet` composition analysis, and the DigitalBrain signal-flow analysis are presented as **hypotheses to confirm by code audit**, grounded in the public prior art (Orleans, Microsoft Agent Framework, MCP, durable-execution literature) that I *could* verify. All external claims are cited with dates.

## TL;DR
- **The core proposal:** DigitalBrain v2 should be a single **actor-graph programming model** in which every unit of behavior is a **neuron** (an Orleans-style virtual actor with a stable identity, private durable state, and a turn-based mailbox), neurons are wired by **synapses** carrying **typed signals**, and the *same* graph is authorable three ways — by an end user in natural language (compiled to a verified neuron graph), by a programmer in a C# API or `.cs` file-based apps, and by an AI assistant that emits the same graph/code on the user's behalf. This is technically feasible today on .NET 10 + Orleans + Microsoft Agent Framework 1.0 + MCP, but it requires deliberate separation of **durable checkpoint boundaries** from **live token streaming**, because durable state alone does *not* make an in-progress async method resumable.
- **The single most consequential unresolved decision:** *What is the atomic unit of durable execution and its determinism contract?* — i.e., is a neuron reaction a single-checkpoint "handle one signal, persist once, emit outgoing signals" step (event-sourced actor, at-least-once + idempotency), OR is a behavior a replay-deterministic durable workflow (Temporal/Durable-Task style), OR a hybrid where neurons are actors but multi-step behaviors run on a durable-workflow substrate? This choice determines everything else and is not yet settled.
- **Recommended path:** Adopt the **hybrid** — neurons as event-sourced virtual actors for identity/state/reactions, plus an explicit **durable-workflow substrate** for multi-step orchestrations and generated-code neurons — and treat every earlier suggestion (`Agent<TState>`, journal-backed `EventLog`, separate conversation/run IDs, a specific agent lifetime) as an unconfirmed hypothesis pending the code audit.

## Key Findings

1. **The vision maps cleanly onto a well-established lineage.** "Neurons + signals" is a modern restatement of Alan Kay's original object model (objects as biological cells communicating only by messages — Kay: *"I thought of objects being like biological cells and/or individual computers on a network, only able to communicate with messages"*), Erlang/OTP's isolated processes with supervision and "let it crash," and J. Paul Morrison's Flow-Based Programming (black-box processes, named typed ports, bounded-buffer connections defined externally to the components). The novelty is not the primitive; it is the *tri-modal authorship* (end user / programmer / AI) over one runtime.

2. **The "natural-language → living behavior" pipeline is already shipping in adjacent products, which both validates the idea and sets the usability bar.** Per Microsoft Learn ("Create your first cloud flow using Copilot"), Power Automate's Copilot "generates the structure for your flow ... a suggested trigger and one or more actions," will "Automatically set up connections on your behalf," and requires the user to confirm ("If you're satisfied with the suggested flow, select Keep it and continue"). DigitalBrain v2's compile pipeline should mirror this: intent → typed graph → validation → human confirmation → deployment → observability → rollback.

3. **Durable state ≠ resumable async method.** The durable-execution literature is unanimous. Per Temporal's official docs (docs.temporal.io/workflows), "Temporal doesn't restore memory from a snapshot" — instead the Event History is "a complete, ordered log of everything that has already happened in a Workflow" and is "the source of truth"; and a Workflow "is deterministic if every execution of its Workflow Definition produces the same Commands in the same sequence given the same input." This directly constrains DigitalBrain: a neuron reaction must be modeled as a *deterministic, checkpoint-bounded step*, and the "one-snapshot-per-reaction" rule in the brief is the right instinct — but it collides with live LLM token streaming, which must be treated as non-durable, replayable-from-scratch output.

4. **Microsoft Agent Framework (MAF) 1.0 is the natural "AI runtime" layer and it already answers several of the brief's questions.** Microsoft Agent Framework 1.0 shipped GA on April 3, 2026 (public preview Oct 2025, RC Feb 19 2026), per Microsoft's April 3 announcement quoted in Visual Studio Magazine: *"This is the production-ready release: stable APIs, and a commitment to long-term support"* — unifying Semantic Kernel + AutoGen, with native MCP + A2A and the `Microsoft.Agents.AI` namespace on NuGet. Its `AIAgent`/`ChatClientAgent` abstraction (built on `Microsoft.Extensions.AI.IChatClient`), typed graph **Workflows**, superstep **checkpointing**, human-in-the-loop request/response, MCP tool integration, and A2A interop are close to what the brief wants from the AI module — but MAF's core checkpointing is snapshot-resume, *not* distributed durable execution (that is a separate Durable Extension running on Durable Task infrastructure).

5. **MCP is now HTTP-stateless-first, which changes tool ownership design.** The 2026-07-28 MCP specification, announced by lead maintainers David Soria Parra and Den Delimarsky, made MCP "a fully stateless protocol"; Multi-Round-Trip Requests (SEP-2322) return `resultType: "input_required"` for confirmations and the client retries with `inputResponses`, while Streamable HTTP requests must include `Mcp-Method` and `Mcp-Name` headers (SEP-2243). A Salesforce neuron owning MCP-backed tools should assume stateless calls plus MRTR / `InputRequiredException` for confirmations, rather than long-lived sessions. This is good news for Orleans-style horizontal scaling of tool-executing neurons.

6. **The critical runtime subtlety the brief flags is real and must be verified in code:** in Orleans, publishing to a stream does **not** by itself invoke a consumer — the consumer must have an implicit (`[ImplicitStreamSubscription]`) or explicit subscription, and delivery only happens if the subscriber attaches processing logic with the *exact same* type and matching namespace/scope (a documented Orleans pitfall: explicit subscriptions can be silently dropped when they collide with an implicit subscription on the same grain-type/namespace). An interface like `IReceiver<CodeChangedMessage>` is only a *capability marker*; something (a router/registry) must wire publication to invocation. This is the single most important thing to confirm by audit.

## Details

### A. Executive summary — the proposed core concept

**DigitalBrain v2 is one programming model expressed as a directed graph of neurons connected by synapses that carry typed signals.** A *neuron* is a virtual actor: it has (1) a stable identity/address, (2) private durable state, (3) a single-threaded mailbox that processes one signal at a time (turn-based concurrency), and (4) a set of typed input "ports" (the signals it reacts to) and output ports (the signals/announcements it emits). Synapses are the externally-defined connections between output ports and input ports — exactly FBP's "configurable modularity," where you rewire behavior without editing the neurons.

On top of this substrate sit three specializations, all *the same kind of thing*:
- **Deterministic neurons** — pure/logic transforms; may be implemented as **code neurons** (a `.cs` file-based app run via `dotnet run app.cs`) when only code can express the logic (data transforms, parsing, math). Per Microsoft's .NET Blog ("Announcing dotnet run app.cs") and Microsoft Learn, file-based apps shipped in the .NET 10 SDK (GA Nov 2025) using `#:package`, `#:sdk` and `#:property` directives with Native AOT enabled by default; they are strictly single-file in .NET 10, with multi-file support (`#:include`) deferred to .NET 11 / SDK 10.0.300+.
- **Agent neurons** — neurons that own an LLM loop (an MAF `ChatClientAgent`), tools, and conversation/session state. An agent is a neuron with extra capabilities, *not* a separate hierarchy.
- **Integration neurons** — neurons that own an external connection and expose capabilities as tools (e.g., a Salesforce neuron backed by an MCP server).

The **three authorship modes** produce the *same artifact* — a validated neuron graph plus optional code neurons:
- **End user (NL):** "When Elon Musk posts a new tweet about crypto, add a chart to the UI dashboard" → an LLM compiler emits a typed graph: `TwitterSource(user=elonmusk) → Filter(topic=crypto) → ChartBuilder → DashboardSink`, schema-validated, shown for confirmation, then deployed as living neurons.
- **Programmer:** the IntoChat lead-generator is authored in the C# neuron API and/or file-based apps, composing the same neuron/synapse primitives.
- **AI assistant:** programs on the user's behalf using the identical model — this is IAW's `CodeOrchestratorAgent` idea, but the generated code/graph operates **neurons** as operands rather than `IAgent`s. Note that MAF's own "CodeAct" feature (BUILD 2026) takes exactly this stance for the model's action space — the LLM writes a short program that calls tools and runs once in a sandbox rather than emitting sequential tool-call JSON.

**The single most consequential unresolved decision** (expanded in section K): the atomic unit of durable execution and its determinism contract.

### B. Evidence-backed map of both implementations

**All items in this subsection are UNVERIFIED — from brief, code not inspected.** They restate the brief's own claims and my hypotheses, and must be confirmed by a code audit.

**IAW (reference implementation) — from brief:**
- `Agent`/`IAgent` authoring model with generic variants (`Agent`, `AgentGeneric`) and partial-class separation of concerns (`Agent.State`, `Agent.Events`, `Agent.Streams`, `Agent.Tools`, `Agent.Lifecycle`, `Agent.Scheduling`) — *UNVERIFIED*.
- `AgentDurableState` (explicit durable state), `AgentEvent`/`IEvent`/`IAgentMessage` (typed messages/events), a separate `EventLog`, and conversation history — *UNVERIFIED*.
- Communication capability contracts: `IReceiver<T>`, `IStreamConsumer`, `IStreamProducer`, `MessageReceipt`; routing via `EventRouterGrain`; a `Registry` — *UNVERIFIED*.
- Orchestration via `CodeOrchestratorAgent`/`ICodeOrchestrator` that generates code operating `IAgent`s — *UNVERIFIED*.
- The composition `public interface IDotNet : IAgent, IReceiver<CodeChangedMessage>` in `Agents.CSharp/DotNet` with a `DotNetAgent` implementation — *UNVERIFIED*.

**DigitalBrain (framework) — from brief:**
- Contracts: `INeuron`, `Signal`, `SignalDelivery`, `Announcement`, plus `Commands/`, `Journals/`, `Descriptors/` — *UNVERIFIED*.
- Runtime: `Neuron`, `NeuronOfState` (`Neuron<TState>`), `NeuronRuntime`, `AnnouncementDrain`, `NeuronJournals`, `ReactionContext`, and admission/retry/cancellation code — *UNVERIFIED*.
- AI module: `AgentNeuron`, `AgentState`, `ChatNeuron`, `ChatState`, `ConversationalAgent`, `AIModule`, `BrainTools`, `TypedNeuronFunctions`, `Tools/TurnBoundFunction` — *UNVERIFIED*.
- Salesforce module: `SalesforceNeuron`, `SalesforceState`, `SalesforceNativeTools`, `SalesforceMcpProvider`, `SalesforceModule` — *UNVERIFIED*.
- IntoChat app: `ConversationalAgentEndpoints`, `AgentStreamResult` — *UNVERIFIED*.

**Preliminary observations from the brief to verify by audit (do not treat as settled):** `SignalDelivery` carries source, correlation, timestamp, signal identity, causation, sequence; `Neuron<TState>` persists state together with pending outgoing announcements; AI `AgentState` stores serialized model sessions per correlation; `ConversationalAgent` embeds instructions for several specialist modules.

### C. Actual message-flow examples

**C.1 The `CodeChangedMessage` trace (HYPOTHESIS — to confirm by audit).** The brief correctly warns: *do not assume publishing an event automatically invokes an `IReceiver` implementation.* Based on how Orleans works (verified), the plausible runtime trace is:

1. **Registration.** At startup a registry/`EventRouterGrain` scans types implementing `IReceiver<CodeChangedMessage>` (e.g., `IDotNet`) and records a routing table entry: `CodeChangedMessage → {DotNetAgent}`. In Orleans terms, `IReceiver<T>` is a **capability marker**; either the router uses it to build subscriptions, or the grain carries `[ImplicitStreamSubscription(namespace)]`.
2. **Production.** A producer calls something like `Publish(codeChangedMessage)` — either a direct grain call routed by the `EventRouterGrain`, or `stream.OnNextAsync(msg)` on a scoped stream.
3. **Routing / admission.** The router resolves recipients and delivers. **Critical:** in Orleans, a stream publish invokes a consumer *only if* that consumer subscribed to the matching stream id **and** attached processing logic for the exact message type; per Orleans docs, an implicit subscription is keyed by grain identity + `ImplicitStreamSubscription` namespace, and explicit subscriptions can be silently dropped if they collide with an implicit one on the same grain-type/namespace. Admission (dedupe by message id, ordering by sequence, retry) happens here.
4. **Handling.** `DotNetAgent.Receive(CodeChangedMessage)` runs as one turn; it mutates durable state and enqueues outgoing announcements.
5. **Subsequent events.** On successful reaction, state + pending announcements persist (one checkpoint), then announcements drain to downstream neurons — the causation/correlation IDs chain the next hop.

**What `IDotNet : IAgent, IReceiver<CodeChangedMessage>` actually enables at runtime (HYPOTHESIS):** the interface composition gives the grain (a) the agent contract surface and (b) a *typed capability declaration* that it can consume `CodeChangedMessage`. It does **not**, by itself, cause delivery. Delivery requires a router/registry that reads the marker (or an Orleans implicit-subscription attribute) and a subscription whose type and scope match the publication. `IReceiver<T>` (direct/mailbox delivery with a `MessageReceipt`) and `IStreamConsumer` (pub/sub stream subscription) are **distinct mechanisms** and must be traced separately; the brief's instinct to keep them distinct is correct.

**C.2 The equivalent DigitalBrain signal/announcement flow (HYPOTHESIS).** A neuron reaction: signal admitted into `ReactionContext` → handler runs → `Neuron<TState>` persists state *with* pending outgoing `Announcement`s in one write (the outbox pattern) → `AnnouncementDrain` publishes announcements to subscribed neurons via synapses → causation/sequence in `SignalDelivery` chains downstream. This is structurally an **event-sourced actor with a transactional outbox** — which is the correct shape for at-least-once delivery + idempotent consumers. (The outbox pattern, per Azure Architecture Center's CQRS guidance, is precisely what "persist the state change and event atomically, and make the read-model consumer idempotent to tolerate duplicate delivery" requires.)

### D. Answers to the five investigation questions

**D.1 Neurons and typed communication.** *Does `SignalDelivery` already cover `IAgentMessage`'s responsibilities?* Likely yes at the envelope level (source, correlation, causation, sequence, timestamp, identity) — **verify by audit**. Recommendation: put **typed payload contracts, event markers, and producer/consumer capability contracts in a dependency-light `Contracts` package** so ordinary neurons and agents share them (this is the package-placement question the brief says must be discussed, not silently decided — see responsibility matrix).

Comparison of the five communication styles (each has a distinct guarantee/purpose):
- **Direct typed calls** (grain-to-grain method): request/response, ordered per-pair, no durability of the call itself; best for synchronous queries.
- **Durable commands** (persisted intent): at-least-once, survive crashes, idempotency-key deduped; best for "do this once."
- **Directed signals** (addressed to a specific neuron): mailbox delivery, per-sender ordering, backpressure via bounded mailbox.
- **Synapse-based events/announcements** (pub/sub over synapses): decoupled fan-out, subscription discovery via the graph, at-least-once + idempotent consumers.
- **Streams** (token/data streams): high-throughput, near-real-time, typically non-durable or separately durable; best for LLM token streaming and telemetry.

Cross-cutting: **routing** via registry/graph; **subscription discovery** should be a first-class graph query; **scope** must be explicit (publication scope vs consumer subscription scope — the Orleans lesson); **schema evolution** via versioned contracts (see risks); **cycles** need detection at compile time (FBP/dataflow graphs otherwise deadlock); **ordering** is per-source/sequence, not global; **backpressure** via bounded mailboxes/connections (FBP bounded buffers: "when they fill up, the process doing the SEND is blocked"); **cancellation** via linked tokens propagated along causation chains.

**D.2 Agent state.** Genuinely agent-specific state: run status, model/session handles, summaries, tool calls/results, usage/metrics, orchestration checkpoints. Recommendation: **compose, don't inherit** — a `NeuronState` base plus a typed specialist state slice, plus an *agent* slice. **History vs serialized model session must not both be sources of truth:** treat the durable **conversation history (event-sourced) as the source of truth**, and treat the provider "model session" as a derived, rebuildable cache (MAF's `AgentSession`/`ChatClientAgentSession` is exactly a session abstraction that can be server-side or reconstructed from history). A **separate `EventLog` vs views over neuron journals** is a genuine trade: a dedicated EventLog gives clean retention/replay/audit and cross-neuron querying; journal-views avoid a second source of truth but complicate cross-cutting audit. **Do not assume durable state resumes an in-progress async method** — model long work as explicit checkpoint-bounded steps (the Temporal/Durable-Task lesson).

**D.3 Agent abstraction and ownership.** Prefer **`Agent : Neuron` (composition-first)**: every agent is a neuron, but not every neuron needs an LLM. Deterministic specialists fit the *same* abstraction as code neurons/integration neurons. Contract metadata, instructions, typed domain methods, tool exposure, discovery, and module registration should be declarative descriptors on the neuron. **Lifetime/identity is unsettled** — per-conversation, per-workspace, per-account, per-run, or compound — and must be decided with the user (it is decision #2 below). MAF itself uses `AIAgent` as an abstract base with `ChatClientAgent` as the concrete workhorse, which is a good precedent for "one base, many concrete kinds."

**D.4 Orchestration and execution.** Distinguish five orchestration modes and support them explicitly: **delegation** (handoff), **group chat** (AutoGen-style), **event reactions** (neuron reacts to a signal), **deterministic workflows** (typed graph), and **generated-code orchestration** (CodeAct/`CodeOrchestratorAgent`). MAF gives you graph Workflows (a Pregel-like superstep model with typed edges: direct, conditional, switch-case, fan-out, fan-in) + a Handoff pattern + group-chat orchestrations to borrow patterns from. **Crash recovery, checkpoint boundaries, duplicate delivery, external side effects, idempotency** must be designed at the reaction boundary. The **one-snapshot-per-reaction rule** is correct for durability but means **live token streaming cannot be durable** — stream tokens are ephemeral UI, and only the final durable "progress" event is checkpointed. Note the LangGraph/CrewAI caution from the durable-execution literature: checkpoints are a *save point*, not durable execution — "no automatic failure detection ... no automatic resumption ... no duplicate execution prevention" unless you build it or delegate to a durable substrate.

**D.5 Module boundaries and tools.** A **Salesforce agent consumes MCP-backed capabilities**: the *integration neuron* owns the connection, credentials, and the MCP client; the *specialist agent* selects and invokes tools (converted to `AIFunction`s via `Microsoft.Extensions.AI`'s `AIFunctionFactory`) with confirmations via MCP elicitation / `InputRequiredException` (the stateless MRTR path). Execution scope, confirmations, and credential ownership live in the integration neuron, not the agent loop. **UI-facing chat and graph agents share execution behavior** by sharing the same underlying agent neuron and history contract, differing only in their presentation port (streamed chat vs graph output) — avoiding incompatible histories.

### E. Responsibility matrix

| Concern | DigitalBrain core (framework) | AI runtime (AI module) | Contracts package | Specialist modules (Salesforce, Coding) | Application/UI (IntoChat) |
|---|---|---|---|---|---|
| Neuron identity/lifetime/mailbox | ✅ owns | consumes | — | consumes | — |
| Signals/synapses/journals/outbox | ✅ owns | consumes | signal & event *types* | consumes | — |
| Durable checkpoint & replay semantics | ✅ owns | consumes | — | consumes | — |
| Agent loop / LLM / sessions / tools | — | ✅ owns | tool schema types | — | — |
| Typed payloads, receiver/producer contracts | provides base | consumes | ✅ owns | consumes | — |
| Connections, credentials, MCP client | — | — | — | ✅ owns | — |
| Tool selection/confirmation/exec scope | policy hooks | ✅ orchestrates | schemas | ✅ owns tools | confirms via UI |
| NL→graph compiler | pipeline host | ✅ owns compiler | graph schema | contributes node types | prompt UI |
| Streaming/observability/rollback | ✅ owns | emits | event types | emits | ✅ renders |

The **package-placement question** (brief: "I initially requested Agent and IAgent in contracts") is a real trade: putting `Agent`/`IAgent` in `Contracts` maximizes reuse but risks pulling AI/tooling dependencies into a package everything references. **Recommendation:** keep the *neuron* and *communication capability* contracts dependency-light in `Contracts`, but keep the *agent* abstraction in the AI module (or a thin `Contracts.Agents` that depends only on `Microsoft.Extensions.AI.Abstractions`), so deterministic neurons never transitively depend on an LLM stack.

### F. The DigitalBrain v2 concept — primitives and semantics

**Minimal primitive set:**
1. **Neuron** — virtual actor; identity, private durable state, single-threaded mailbox, typed input/output ports.
2. **Signal** — an immutable typed message on a synapse; carries payload + `SignalDelivery` envelope (source, correlation, causation, sequence, timestamp, id).
3. **Synapse** — an externally-defined typed connection from an output port to an input port (FBP connection); may be bounded (backpressure).
4. **Command** — a durable, idempotent "do this once" intent.
5. **Journal** — the append-only per-neuron event log (event sourcing) that is the source of truth; state is a projection/snapshot.
6. **Agent** — a neuron that owns an MAF `ChatClientAgent`, tools, and session.
7. **Code neuron** — a transform implemented as a `.cs` file-based app (`dotnet run app.cs`, .NET 10 SDK) for logic only code can express.

**Formal-ish semantics:**
- **Identity:** stable address (e.g., `neuronType/key`); virtual (activated on demand, deactivated when idle, state restored on reactivation) — Orleans virtual-actor lifecycle.
- **Lifetime:** neuron activations are ephemeral; durable identity + state persist. (Agent *scope* is a separate, unsettled decision.)
- **Delivery guarantees:** at-least-once for signals/commands; **idempotent consumers required** (dedupe by signal id); exactly-once *processing* achieved via outbox + idempotency, never exactly-once *delivery*.
- **Ordering:** per-source/sequence, not global.
- **Idempotency:** every reaction keyed by signal id; projections use upsert-on-conflict.
- **Cancellation:** cooperative, via tokens propagated along causation chains.
- **Versioning:** contracts versioned (backward/forward compatible), see risks.

**The compile pipeline (NL → living behavior):** intent (NL) → **typed graph** (schema-constrained LLM generation into the neuron/synapse schema) → **validation** (type-check ports, detect cycles, verify capabilities/permissions) → **confirmation** (human-in-the-loop review, Power-Automate-style) → **deployment** (instantiate neurons, wire synapses) → **observability** (traces, per-reaction events) → **rollback** (versioned graph, revert to prior).

**Worked example — "When Elon Musk posts a new tweet about crypto, add a chart to the UI dashboard":**
- Compiler emits: `TwitterSourceNeuron(user=elonmusk)` —tweet signal→ `TopicFilterNeuron(topic=crypto)` —match signal→ `ChartBuilderCodeNeuron` (a `.cs` file-based app that transforms tweet+market data into a chart spec) —chart signal→ `DashboardSinkNeuron` (UI port).
- Validation checks the tweet payload schema matches the filter's input port, the chart spec matches the dashboard's input port, and no cycles exist. User confirms. Neurons start living; each tweet flows through as signals; the dashboard updates. (This is deliberately the same shape as Power Automate's canonical "when a new tweet is posted, ..." Copilot example, so the UX bar is a known quantity.)

**Worked example — IntoChat lead generator:** a programmer composes `InboundEventNeuron → LeadScoringAgentNeuron (MAF agent + tools) → SalesforceIntegrationNeuron (MCP-backed create/update lead, with confirmation) → NotificationNeuron`. The same graph could equally have been produced by the AI assistant from an NL description, or hand-written in a file-based app.

### G. Two or three coherent architectural alternatives

**Alternative 1 — Event-sourced virtual actors (Orleans-native).** Neurons = grains; journals = event sourcing; synapses = streams/direct calls; outbox for announcements. *Pros:* simplest mental model, great scaling, matches the brief's apparent current design. *Cons:* multi-step orchestration and crash-safe long work are *your* responsibility (checkpoints are not durable execution); you must build retry/saga/idempotency plumbing.

**Alternative 2 — Durable-workflow-first.** Behaviors are durable workflows (Temporal .NET / Dapr Workflow / Azure Durable Task); neurons are activities/entities. *Pros:* true durable execution, deterministic replay, built-in retries/timers/compensation. *Cons:* determinism constraints on workflow code are hard for authors *and* for LLM-generated code ("your workflow code must be deterministic ... every replay with the same history must produce the same commands"); less natural as a "living graph"; a heavier runtime.

**Alternative 3 (RECOMMENDED) — Hybrid actor-graph + durable substrate.** Neurons are event-sourced virtual actors (Alt 1) for identity/state/reactions; multi-step orchestrations and generated-code neurons run on an explicit durable-workflow substrate (Alt 2) invoked *from* neurons. MAF's own split is a working precedent: graph Workflows with superstep checkpointing for the AI layer, plus a separate **Durable Extension** on Durable Task infrastructure for true distributed durability (Microsoft documents these as different things — checkpoint storage "helps resume a workflow run in the Agent Framework runtime," while the Durable Extension "runs the workflow on Durable Task infrastructure so workflow progress is checkpointed and recovered across distributed durable workers"). *Pros:* right tool per job; matches MAF; keeps the graph model while getting durability where it matters. *Cons:* two execution models to learn and to bound (the migration risk).

**Migration implications:** current DigitalBrain (neurons/journals) maps to Alt 1/3 with least disruption; the AI module maps onto MAF `ChatClientAgent`; the Salesforce module keeps its MCP provider but adopts the 2026-07-28 stateless/MRTR patterns; a durable substrate is additive.

### H. Illustrative C# sketches — **all UNCOMPILED SKETCH; PROPOSED API**

```csharp
// PROPOSED API / UNCOMPILED SKETCH — communication contracts (dependency-light Contracts pkg)
public interface INeuron { NeuronId Id { get; } }
public readonly record struct SignalEnvelope(
    Guid SignalId, NeuronId Source, string Correlation, string Causation, long Sequence, DateTimeOffset At);
public interface ISignal<TPayload> { SignalEnvelope Envelope { get; } TPayload Payload { get; } }
public interface IReceiver<in TSignal> { Task ReceiveAsync(TSignal signal, ReactionContext ctx); } // capability marker
public interface IProducer<out TSignal> { } // declares an output port for discovery

// PROPOSED API / UNCOMPILED SKETCH — Agent and IAgent (in AI module or Contracts.Agents)
public interface IAgent : INeuron { AgentDescriptor Descriptor { get; } }
public abstract class Neuron<TState> : INeuron { protected TState State = default!; /* journal + outbox */ }
public abstract class Agent<TState> : Neuron<TState>, IAgent { /* wraps MAF ChatClientAgent + session */ }

// PROPOSED API / UNCOMPILED SKETCH — shared + specialist durable state (compose, not inherit)
public record NeuronState(long Version);
public record AgentSlice(string RunStatus, string? SessionHandle, int PromptTokens, int CompletionTokens);
public record SalesforceSlice(string OrgId, string? InstanceUrl);
public record SalesforceAgentState(NeuronState Core, AgentSlice Agent, SalesforceSlice Specialist);

// EXISTING API (Microsoft.Extensions.AI) used inside an agent neuron
// IChatClient client; ChatOptions { Tools = [ AIFunctionFactory.Create(...) ] };
// EXISTING API (Microsoft.Agents.AI) — AIAgent agent = new ChatClientAgent(chatClient, instructions: "...");

// PROPOSED API / UNCOMPILED SKETCH — typed event + receiver
public sealed record CodeChangedSignal(SignalEnvelope Envelope, string Path, string Diff) : ISignal<string>
{ public string Payload => Diff; }
public interface ICodeNeuron : INeuron, IReceiver<CodeChangedSignal> { }

// PROPOSED API / UNCOMPILED SKETCH — Salesforce agent using MCP-backed tools
public sealed class SalesforceNeuron : Agent<SalesforceAgentState>, IReceiver<LeadSignal>
{
    // integration neuron owns connection + MCP client + credentials
    public async Task ReceiveAsync(LeadSignal s, ReactionContext ctx) {
        // tool selection via MAF; confirmation via MCP elicitation / InputRequiredException (stateless MRTR)
        // one durable checkpoint at end of reaction; outgoing announcements in outbox
    }
}

// PROPOSED API / UNCOMPILED SKETCH — orchestrator coordinating neurons (CodeAct-style)
public sealed class OrchestratorNeuron : Agent<NeuronState>
{
    // generates a .cs file-based app / neuron graph that operates neurons as operands
}

// PROPOSED API / UNCOMPILED SKETCH — a code neuron as a .NET 10 file-based app (chart-builder.cs)
// #:package YahooFinance@*
// var tweet = Signal.Read<TweetPayload>();
// var chart = BuildChartSpec(tweet, await GetMarketData());
// Signal.Emit(chart);
```

### I. Risks, migration implications, and required tests

- **Crash recovery / checkpoint boundaries:** test kill-mid-reaction → verify state + outbox recover atomically; verify no partial external side effects.
- **Duplicate delivery:** inject duplicate signals → verify idempotent no-op (upsert-on-conflict projection).
- **External side effects (tools):** test that a retried reaction does not double-create a Salesforce lead (idempotency key / dedupe).
- **One-snapshot-per-reaction vs live streaming:** test that a crash during token streaming loses only ephemeral tokens, and the durable progress event is exactly-once.
- **Schema evolution:** test old neurons consuming new signal versions and vice versa (backward/forward compatibility; include an `eventVersion`/schema-version field in every signal envelope, per event-sourcing best practice — "establish event versioning strategies early ... adding them later is painful").
- **Cycles / ordering / backpressure / cancellation:** compile-time cycle detection; per-source ordering under load; bounded-mailbox backpressure; cooperative cancellation across causation chains.
- **Determinism (if any durable-workflow path):** replay tests to catch non-determinism.

### J. Outline of the "plans folder for Grok"

Nothing starts until the user approves. Proposed numbered plan files, each with goal / scope / in-out / files-touched / acceptance criteria / tests / dependencies:
- `00-overview.md` — vision, glossary, decision log, sequencing.
- `01-contracts.md` — neuron & communication contracts; package placement decision.
- `02-neuron-runtime.md` — identity, mailbox, journal, outbox, checkpoint boundary.
- `03-signals-synapses.md` — typed signals, synapse wiring, subscription discovery, backpressure.
- `04-durability-substrate.md` — hybrid durable-workflow integration; determinism tests.
- `05-agent-neuron.md` — `Agent : Neuron`, MAF `ChatClientAgent`, session-vs-history source-of-truth.
- `06-tools-mcp.md` — MCP client ownership, confirmations, stateless/MRTR patterns.
- `07-salesforce-module.md` — integration neuron + specialist agent.
- `08-orchestration.md` — delegation/group-chat/reactions/workflows/generated-code.
- `09-nl-compiler.md` — intent→graph→validate→confirm→deploy→observe→rollback.
- `10-code-neurons.md` — `.cs` file-based app neurons; sandboxing.
- `11-intochat-app.md` — lead generator; UI streaming vs graph output.
- `12-observability-rollback.md` — traces, versioning, rollback.
- `13-tests-harness.md` — the crash/duplicate/idempotency/schema test suite.

Sequencing across Grok CLI sessions: 00–03 first (foundation), then 04–06 (durability + agents), then 07–11 (modules + compiler + app), then 12–13 (ops + tests), each session gated on passing the prior session's acceptance criteria.

### K. The most consequential unresolved decision (for discussion)

**Decision:** *What is the atomic unit of durable execution and its determinism contract in DigitalBrain v2?*
- **Option A — Event-sourced actor step (at-least-once + idempotency).** Simple, scalable, matches current design; but you build orchestration reliability yourself.
- **Option B — Replay-deterministic durable workflow.** True durable execution; but determinism constraints are hostile to authors and to LLM-generated code.
- **Option C — Hybrid (recommended).** Actors for reactions, durable substrate for multi-step/generated behaviors.
- **What evidence would settle it:** the code audit of `NeuronRuntime`/`NeuronJournals`/`AnnouncementDrain` (does it already give outbox + idempotency?), plus a spike measuring how hard determinism is for generated-code neurons, plus the reliability requirements of the IntoChat lead generator (does it need cross-step crash-safety and compensation?).

**Direct question to you:** *Do you want a neuron reaction to be the durable atom (Option A/C — at-least-once with idempotent handlers and an outbox), or do you want whole multi-step behaviors to be durably replayable (Option B), accepting the determinism constraints that imposes on both your code and the AI's generated code?*

**Next 3–5 decisions in priority order:** (2) agent lifetime/identity scope (conversation vs workspace vs account vs run vs compound); (3) `Agent`/`IAgent` package placement (Contracts vs AI module vs thin `Contracts.Agents`); (4) source-of-truth for conversation state (event-sourced history vs serialized model session); (5) separate `EventLog` vs views over neuron journals.

## Recommendations

1. **First, run the code audit** (you have local access; I do not). Confirm/deny each UNVERIFIED item in section B, and specifically trace `CodeChangedMessage` and the `IReceiver` vs `IStreamConsumer` mechanisms, plus stream publication-vs-subscription scope. Nothing below should be finalized before this.
2. **Adopt the hybrid execution model (Alt 3)** unless the audit shows the current runtime already gives durable-workflow guarantees. Benchmark/threshold to change this: if generated-code neurons cannot be made deterministic cheaply, lean harder on Option A actors; if the lead generator needs cross-step compensation, lean harder on the durable substrate.
3. **Base the AI module on MAF 1.0** (`ChatClientAgent` on `IChatClient`, Workflows for orchestration, checkpointing + HITL), and treat conversation history (event-sourced) as source of truth with the MAF session as a rebuildable cache.
4. **Design tools around MCP's 2026-07-28 stateless model** (MRTR / `InputRequiredException` for confirmations; `Mcp-Method`/`Mcp-Name` headers on Streamable HTTP); keep connection/credential ownership in integration neurons.
5. **Build the NL compiler as schema-constrained generation + mandatory human confirmation**, mirroring Power Automate Copilot's review-before-deploy UX ("Keep it and continue"), with rollback via versioned graphs.
6. **Settle Decision K with the user before writing plan `02`.**

## Caveats
- **No local code was inspected.** All DigitalBrain/IAW specifics are the user's brief restated as hypotheses; the audit may overturn them.
- **Fast-moving sources.** MAF 1.0 (GA April 3, 2026), the MCP 2026-07-28 spec, .NET 10 file-based apps (SDK GA Nov 2025), and BUILD 2026 features (CodeAct/Agent Harness/Hosted Agents) are recent; some are new or in preview and APIs may shift. MAF's `AgentThread` concept was renamed to `AgentSession`/`ChatClientAgentSession` in the GA `Microsoft.Agents.AI` namespace; confirm the exact type names against current docs before coding. There is a one-day discrepancy in Microsoft's own sources on the MAF GA date (April 2 vs April 3, 2026).
- **Vendor-reported numbers** (e.g., MAF CodeAct's reported ~52% latency / ~64% token reduction on a single "representative" workload) are single-workload and should be independently validated. MAF's CodeAct was reported as Python-only (the `agent-framework-hyperlight` alpha) at BUILD 2026 — I could not independently confirm a .NET CodeAct path, so treat DigitalBrain's code-neuron/CodeAct story as *your* design rather than a shipped MAF .NET feature.
- **This is research and collaborative design, not an approved redesign.** Every earlier suggestion (`Agent<TState>`, journal-backed `EventLog`, separate conversation/run IDs, a particular agent lifetime) remains a hypothesis to evaluate.