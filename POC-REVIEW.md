# PoC Review — how well DigitalBrain fits the vision

*Written 2026-08-11 against HEAD (b3e7eb35). The PoC spans two arcs: the foundation arc
(59 commits: contracts-only modules, the three-tool assistant, the synapse graph flows,
the MCP gateway — verified by a suite that ended that arc 74/74 green and seven live
Gemma rounds) and the owner's Stage 1 refit (durable conversation turns, the identity
boundary, Gmail strangled into the generic MCP rail — verified at its peak by 165/165
green before the central test project was deliberately deleted in the move to
source-first verification, with module-owned test frameworks planned for hardening).*

## What the PoC is

A personal "alive OS": durable neurons (Orleans grains) exchanging immutable facts
(synapses), where **who hears what is data** — a runtime synapse graph the owner and the
model rewrite live — and every event is journaled, so the system can always explain
itself. One local model (Gemma4) fronts it through exactly three tools. External SaaS
enters through one generic MCP gateway — Salesforce *and* Gmail, on the same rail. Chat
turns are durable work: HTTP is an observer, disconnecting cancels nothing, and an
Execution neuron owns attempts, liveness, and cancellation. The UI (Flutter) renders the
same vocabulary the neurons speak: chat, charts, clocks, and the brain's own wiring.

## Vision alignment, tenet by tenet

### "Everything is a connection" — **holds, and it is the best part**
The graph is the routing table, stored as durable data: `Connect(source, alias, target,
transform?)`. Flows proven through it during the arc: timer → morph → clock card in
chat; timer elapse → morph → note; probe feed → morph → chart; MCP result rows → chart.
Connections expire, sweep, and validate their morphs *at wiring time* (a typo'd field
refuses with the real field list), and draw live in the Brain view. Nothing here is
aspirational — each path ran under a cluster proof and most also ran live.

### "Simple stupid but fully capable" — **holds; the second integration proved it**
The deletion ledger is the evidence trail: ten hand-written manifests, the capability
router, per-turn tool materialization, the Salesforce demo neuron + planner + mutation
flow, `IEmit<>`, every `[Description]` — all deleted, each deletion making the system
more general. The decisive data point came in Stage 1: **strangling Gmail onto the
generic MCP rail removed code** (typed contracts, GmailAuthRail, planner, token store)
instead of adding it. When integrating a second provider shrinks the codebase, the
abstraction is real. A module is its contracts; a SaaS provider is a server definition.
The caveat stands: this simplicity is enforced by convention (names carry meaning,
interface names derive grain types), and conventions bite when violated.

### The 3-call assistant (find → get → fire) — **holds, proven live**
Three constant tools regardless of system size; the tool list never grows. Live round
six on Gemma4-12b: intent → find → get → fire db.connect (correct canonical identities,
right chat) → fire time.start-timer — five calls, 26 seconds, and the reply "I have
wired the timer to your chat" was *true*. Correctable errors are the mechanism that got
a 12b model there: wrong targets, guessed identities, misshapen arguments, and missing
fields all return as text the model fixes in-loop. Six such guards came directly from
live-run evidence.

### "This is exactly why we have MCP" — **holds by construction, now twice over**
One gateway neuron (`mcp`, instance = server key) serves every server: the server's
*live* catalog is the capability surface — nothing re-declared in C#. `FireRowsAs`
fires each tabular result row as a named synapse, shaped by the query itself (SOQL
column aliases → `ui.chart-point` fields), so results flow through the graph, never
through the model. Stage 1 sharpened the authority model: all catalog tools are
callable; provider OAuth with verified per-principal integration is the authority
boundary, destructive metadata stays visible for audit. Salesforce and Gmail both ride
the bounded PKCE rail with the composition-derived `/oauth/callback`. **Still unproven
live**: a full provider sign-in round-trip and the sales barchart demo.

### Observability / self-explanation — **holds**
Every failure in both arcs was diagnosed from the system's own records: journals named
the selected-but-unexecuted tool calls; topology exposed the guessed
`timer:dev/timer → chat:dev/chat` wiring; the broadcast tier renders beside the graph
tier. The system meets the "explains itself" bar better than most production software.

### Truthfulness and durability of work — **strengthened by the refit**
"Tool results are the only truth" is enforced by error design; live round seven showed
the assistant reporting a failed wiring instead of claiming success. Stage 1 extended
truthfulness to *work itself*: chat turns are FIFO durable executions with liveness,
terminal bridging, and revision-idempotent re-apply — a disconnected screen can no
longer lie about, or cancel, in-flight work. The remaining seam from the foundation arc:
settled refusals still carry no reply, so a request loop sees a timeout, not the reason;
honest but not self-correcting.

### Local models, explicitly routed — **holds, narrowed by choice**
Gemma4 is the one unkeyed `IChatClient` — the main model, one registration. Llama32 is
currently disabled at the composition level; `ask_llama` exists but is dormant, which is
the routing philosophy working: every model is an explicit, revocable choice. The
reliability ceiling is honest: a 12b model works here because the guards teach it, and
turns cost tens of seconds on this hardware.

## Flexibility — the cost-of-change test

| Change | Cost, as demonstrated |
|---|---|
| New SaaS via MCP | A `McpServerDefinition` + OAuth params. Gmail's migration **deleted** its typed path. |
| New module (vocabulary + behavior) | Contracts assembly + neuron; manifest/discovery/tools derive automatically. |
| New data flow (X feeds Y) | Zero code: one `db.connect` with a morph — by owner, script, or model. |
| New chat/UI element | One synapse + chat handler + one `KitPart` (timer card: ~150 lines end to end). |
| Swap/disable a model | One keyed registration (Llama32 was disabled without touching the assistant). |
| Deep refactors | Stage 1 decomposed core/execution/introspection neurons and moved the SDK into the Kernel without breaking the product surface — the strongest flexibility evidence there is. |

The flexibility is real because the three layers that normally ossify — tool lists,
manifests, integration contracts — are *derived* (from the loop, from reflection, from
the MCP server) rather than written.

## What does not fit the vision yet

1. **Refusal visibility**: refusal *reasons* never reach request loops; the model stays
   honest but cannot self-correct on them.
2. **Two routing tiers**: compile-time `IHandle` broadcast vs the runtime graph still
   coexist. Both visible in topology (one mental model); true unification remains the
   deepest deferred architectural decision.
3. **Verification posture in transition**: the central suite (peak 165/165) is
   deliberately gone; source-first verification carries the interim, with module-owned
   test frameworks planned. Until those exist, refactors lean on the gate script and
   discipline — a chosen trade, but the window is real.
4. **Discovery embeddings unwired**: the capability index runs keyword-only until an
   embedding model is added; fine at today's vocabulary size, semantic matching will
   matter as it grows.
5. **Live provider proof outstanding**: the OAuth sign-in walk-through and the
   "barchart with sales from salesforce" demo have not run end to end with a real
   provider.
6. **Latency**: correct ≠ fast; local-model turns are tens of seconds, and the
   measurement/tiering work was never run.

## Verdict

**The PoC proves the thesis — twice.** The load-bearing ideas — durable neurons,
synapses, a rewritable graph, reflected capability, one generic SaaS gateway — survived
a real local model, real OAuth plumbing, seven adversarial live rounds, and then a
full-scale owner refit that *reused* them under pressure: Gmail collapsed onto the
gateway, chat turns became durable executions on the same neuron substrate, and the
system got smaller and more capable at each step. The owner's instincts drove every
major simplification (contracts-only modules, deleting `IEmit`, rejecting per-action
SaaS contracts, catalog-is-the-surface), and each one generalized the system — the
signature of a design that fits its vision rather than fighting it.

What separates PoC from product is ledgered work, not architecture: refusal visibility,
the live provider round-trip, module-owned verification, and patience with local-model
latency.

*Next moves, in the owner's own frame: finish the refit's hardening with module-owned
test frameworks; run the Salesforce sign-in + barchart live; close the
refusal-visibility seam; then measure before optimizing the model layer.*
