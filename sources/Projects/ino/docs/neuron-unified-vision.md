# Neuron — ino's unified primitive

**One-sentence pitch.** A neuron is an Orleans grain that carries its own memory, reasoning, actions, schedule, integrations, metrics, and local ML — uniformly, from the moment it exists — so humans and ino itself can add new capability by doing exactly one thing: writing or calling another neuron.

## The thesis

Neurons are simple and complex at the same time.

**Simple**: one base class (`Neuron<TEvent>`), one grain identity, one synapse schema. An engineer writing a new neuron writes domain logic and nothing else.

**Complex under the hood**: every neuron is a fully-featured distributed object. State is durable. Reasoning is LLM-powered when needed. Actions are typed methods the LLM can trigger. Scheduling is Orleans-native. Integrations are DI-injected. Metrics are per-neuron and live. Local ML self-optimizes the hot paths. Every aspect is uniform — not an opt-in plugin, not a framework extension, but part of what it means to *be* a neuron.

This uniformity is the lever. Humans and ino itself operate on the same substrate. The surface Claude Code drives to create a neuron is the surface ino drives to self-improve. No second system.

## The seven aspects of every neuron

### 1. Identity
- Grain key (string or Guid) + canonical target name (cluster-wide address)
- Capability list — authorization scopes the neuron holds, enforced at the FirePort
- Discoverable via `IDiscovery` across silos

### 2. State — journal IS the state
- Built on Orleans 10's `DurableGrain` + `IDurableList<EventEnvelope<TEvent>>`
- Every mutation is an event; projections are derived on demand
- No separate projected-state concept to keep in sync — the log is the truth
- Time-travel and replay fall out for free (see `docs/vision.md` — parallel universes)

### 3. Reasoning — LLM, optional
- `[Llm<Persona>]` attribute injects an `IChatClient` with the right model tier
- Instructions + tool schema + conversation state live on the neuron
- Pure-code neurons (`TimelineGrain`, `RegistryGrain`, `DiscoveryGrain`) skip this entirely and are first-class neurons anyway

### 4. Actions — typed methods, LLM-callable
- Methods on the neuron's interface are both synapse targets (fire from another neuron) *and* LLM tools (surfaced via `DefineTools()`)
- Same code path, two callers
- End-to-end typed — no JSON schemas hand-written, no drift between tool description and implementation
- Phase 3 source generator derives tool schemas + CLI subcommands + MCP bindings from the interface

### 5. Scheduling — Orleans reminders + timers
- `IRemindable` + `RegisterOrUpdateReminder` for durable time-triggered actions (minutes to years)
- Grain timers for in-activation periodic work (seconds)
- Survives silo restarts — a neuron that schedules "remind me in a week" keeps its promise across deployments
- Native fit for `SchedulerSpecialist`, nightly decay consolidation, ML retrain cadence

### 6. Integrations
- Declared per-neuron via Orleans DI (HttpClient, MCP clients, DB, vector store, external APIs, auth tokens)
- Each neuron's "world" is explicit — what it can reach is visible in its constructor signature
- **Domains** ship integration bundles as installable units (Phase 2: marketplace + capability consent)

### 7. Metrics + NeuronML self-optimization

Metrics and local ML are one aspect, not two — they form the neuron's self-observation and self-improvement loop.

**Per-neuron metrics:**
- Each neuron owns a `Meter` with neuron-specific counters, histograms, gauges
- Baseline metrics come free: activation count, synapse rate, handler duration percentiles, LLM cost
- Custom metrics declared on the neuron interface; source generator emits the Meter + Aspire dashboard wiring
- All metrics flow to OpenTelemetry → Aspire → Claude Code (via Aspire MCP) → ino itself

**NeuronML — LightGBM layer (what):**
- Per-neuron `NeuronOptimizer` grain records decisions, trains LightGBM after 50 samples, serves microsecond predictions
- High-confidence (>0.90) predictions skip the LLM entirely; low-confidence falls through and becomes more training data
- Full design in `docs/neuron-ml.md`

**Mandelbrot layer — multifractal time-series analysis (when):**
- Metric time-series (latency, error rate, activation bursts) are heavy-tailed, self-similar, long-memory — classic domain of Mandelbrot's fractal analysis
- Per-neuron multifractal spectrum detects bursty load patterns, cascade failure precursors, regime shifts that ARIMA-style models miss
- Outputs are orthogonal to LightGBM: LightGBM predicts *what* decision to make; Mandelbrot predicts *when* to retrain, short-circuit, degrade gracefully, or alert
- The two layers compose — a neuron can be highly confident on the decision AND on the timing

Together: every neuron observes itself, learns from itself, and micro-optimizes itself. The system gets cheaper and faster per call the more it's used, without human tuning.

## Composition — domains

A **domain** is a bundle of neurons, synapses, capabilities, and integrations shipped as a unit (`IDomain`). Travel is a domain. Notes is a domain. Identity is a domain. Domains expose user-facing behavior by routing entry synapses to neurons. There is no separate product/runtime unit between a domain and its neurons.

For the builder, three levels of engagement:
1. **Install an existing domain.** One line in `AddIno(...).WithDomain<Travel>()`. Its neurons become cluster-addressable. Capabilities negotiate at install time with user consent (Phase 5 marketplace).
2. **Extend a domain.** Subclass a neuron, override a method, add a tool. State + metrics + ML come along automatically.
3. **Write a new domain from scratch.** Inherit `Neuron<TEvent>`, implement handler methods, declare metrics on the interface. Everything else — journaling, reminders, ML training, integration DI, discovery, capability enforcement — is provided.

This is the engineering-power claim: the marginal cost of adding a neuron is *only* the domain logic. No boilerplate for persistence, no metric plumbing, no ML wiring, no discovery registration, no tool-schema drift.

## Composition — self-improvement

When ino encounters a request it can't route, it creates a neuron through the exact same primitive. L1 creation is a registry row plus a canonical target announcement — cheap and cluster-wide in ~10ms. The new neuron inherits every aspect from day one: it gets state, metrics, ML optimization, reminders, discovery visibility, all of it.

Claude Code drives this same surface during development. ino drives it autonomously in production. Same verb, same primitive, same introspection. That is why the loop can close.

## What this is NOT

- **Not microservices.** Neurons share the Orleans cluster substrate. No REST, no schema drift across service boundaries, no per-service deployment.
- **Not actors-only.** Actors are the grain layer underneath. Neurons add reasoning, actions-as-tools, metrics, ML, and a uniform schema on top.
- **Not a framework with pluggable extensions.** Every neuron is full-featured. Aspects are constitutional, not optional plugins.
- **Not a hot path to micro-optimize first.** The neuron surface is optimized for simplicity, composability, and introspectability. Perf falls out of Orleans + LightGBM + Mandelbrot, not from hand-tuning each call.

## Mapping to current state

| Aspect | POC status | Legacy `src/` status |
|---|---|---|
| Identity | done — canonical targets + discovery | done |
| State | done — `Neuron<T>` + `IDurableList` journal | partial — `AgentDurableState` (ad hoc) |
| Reasoning (LLM) | planned Phase 3 | done — `[Llm<T>]` injection |
| Actions (tools) | planned Phase 3 (source generator) | done — `DefineTools()` |
| Scheduling | not started | partial — direct Orleans reminders |
| Integrations | Phase 2 domain flows in flight | ad hoc DI |
| Metrics (per-neuron) | not started | partial — global meter |
| NeuronML (LightGBM) | not started | done — `docs/neuron-ml.md` |
| NeuronML (Mandelbrot) | not started | not started |

## Roadmap beats

1. **Phase 2 finish** (current branch) — cross-silo runtime + domain install + capability enforcement. Substrate.
2. **Phase 3 source generator** — neuron interface → MCP tool schemas + CLI subcommands + client proxies + metric declarations. Self-documenting, drift-free surface.
3. **Metrics as contract** — declare metrics on the neuron interface via attributes; generator emits the `Meter`, counters, and Aspire wiring. No hand-plumbed observability.
4. **NeuronML lift to POC** — port `NeuronOptimizerGrain` + `FeatureArchitectGrain` to `Ino.Core.Hosting`. Wire to every neuron by default via the base class.
5. **Mandelbrot layer** — multifractal / self-similar time-series module consumes neuron metrics. Output feeds retrain cadence, anomaly alerts, graceful degradation triggers. Orthogonal to LightGBM.
6. **Scheduling as aspect** — promote Orleans reminders to a first-class neuron aspect with typed schedule declarations on the interface.
7. **Domain marketplace** — install domains as Aspire resource bundles; capability enforcement + user consent at install time; discovery and removal.

## The unifying claim

Every question about ino — how it stores state, how it reasons, how it acts, when it acts, what it integrates with, how it observes itself, how it improves itself, how users extend it — has the same answer: *it's a neuron*. One primitive, seven uniform aspects, two composition axes: domains and self-improvement. Build the primitive well and the system follows.
