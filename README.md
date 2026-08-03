# DigitalBrain

An open-source .NET framework for durable agents on Orleans and Aspire. Its paradigm is **neurons and
synapses**: neurons are durable Orleans-journaled agents, synapses are typed facts with full lineage,
and method-scoped `TestBrain` fixtures fire real multi-silo traffic and assert on committed journals.

> **A brain you program by writing ordinary C#, and that can program itself.**

```csharp
// production: builder.AddDigitalBrainClient(owner); inject IDigitalBrain
await brain.SendAsync<IAnalyst>(
    "incident-42",
    new SummaryRequested("Summarize the incident."));
```

The owner-bound `IDigitalBrain` facade enters through a session; neurons call typed capabilities such
as `IGemma4` inside the brain. The same vocabulary will later carry approved C# behaviors generated
from natural language.

## The shape of it

- **A synapse is a fact** — a thin record, broadcast, no reply. **An interface method is a request** —
  directed at a capability, and it replies. Both are journaled; neither is privileged.
- **Modules own vocabulary** — synapse records and neuron interfaces, resolved at compile time.
- **Namespaces and type names are architecture** — `DigitalBrain.AI.Ollama.IGemma4` is identity, not
  a lookup result.
- **Journals are the audit source**, recording causal facts only — never arguments, prompts or
  secrets. Telemetry is a projection and never replaces them.
- **Every install is a human-approved proposal**, journaled and reversible.

## Status

The plan of record. Nothing is shipped unless it says Built.

| Area | State |
|---|---|
| Neuron/synapse foundation, owner-scoped client, module activation, AppHost composition, testing path | **Built** |
| Typed AI, Tasks, Google, Salesforce, Chat, Shell, Quickstart families | **Built** |
| Memory / vector infrastructure (`IVectorMemory`, Qdrant hosting) | **Built** |
| Automatic discovery — exact catalog + semantic projection (lab) | **Built** (product claim pending live E1) |
| Shell vertical — shell/scene vocabulary, UI HTTP/SSE edge (`WithUiEdge`/`WithHeadlessHost`/`WithWindowHost`/`WithWebHost`), headless Dart host, Windows + web chrome | **Built** (web deploy host; K1 six-view pixel parity on web still open) |
| Product shell — responsive Chat, content-safe Activity, live 3D-projected Brain topology, pulses and inspector | **Built** |
| Behavior Studio surface (six views, host APIs) | **Built** |
| NL → C# behavior authoring (C1 ladder) | Designed until C1 green |
| Product MCP surface — durable chat send/read, neuron journal observation, active-neuron discovery | **Built** — live-verified over the silo's `/mcp` (`read_neuron_journal`, `read_chat_transcript`, `list_active_neurons`) against a real `aspire run` |
| Introspection — model-callable journal tally, journal read, and topology read as brain capabilities (`introspection.tally-journal-request`, `introspection.read-journal-request`, `introspection.read-topology-request`) | **Built** — the capabilities answer correctly when invoked, live-verified via the MCP surface above and via deterministic capability-call tests. Live Gemma4 (`gemma4:12b`) did **not** select `introspection.tally-journal-request` across repeated real turns in one session, even when told its name: it answered "how many messages have I sent" from conversation memory, once exhausted its context budget mid-deliberation without answering at all, and once substituted `chat.read-transcript-request` and counted turns manually. The off-by-one tally policy (the in-flight question counts) held in every case where an answer was given. Model tool-selection for this capability is an open live gap, not a code gap — see CLAUDE.md traps |
| Dual live Google + Salesforce OAuth productization | In progress — Gmail is Google SDK (REST) with reflected read-only catalog + typed ops; one browser sign-in flow via app callback `/oauth/callback`; Salesforce stays MCP. Unverified Google app in Testing mode: re-consent every 7 days, ≤100 test users. Register redirect `http://localhost:5080/oauth/callback` (owner re-registration pending, lane g6). Live dual-provider proof pending |
| Time — durable one-shot `ICountdown` and its recovery tests | **Built** |
| Time — reminders, recurring interval/calendar scheduling, DST | Designed |
| Multi-principal IdP edge, journal observation on `IDigitalBrain` | Designed |
| Multi-model UI combine / Settings model switch | Designed |
| Two Docker Hub product images (`digitalbrain`, `digitalbrain-ui`) | In progress — Dockerfiles and a local `docker-compose.yml` smoke exist; the UI image does not bundle the Flutter web build; no CI image build, no Docker Hub publish |
| Behavior rail — proposal, compile, BDD gate, approval, activation, rollback, signed BehaviorHost deploy/execute (L1/L2) | **Built** |
| Observability spine — host OpenTelemetry, structured logs, causal kernel spans, GenAI spans and metrics | **Built** |

`DigitalBrain.Behaviors` is a packable SDK foundation (authoring interfaces, constrained context,
manifests, artifact identities) and holds the canonical artifact codec. The product AppHost loads
`BehaviorsModule` and the Behavior Studio surface; full NL→C# authoring is not ship-claimed until C1
is green. Chat still owns its turn in `ChatNeuron` today.

One assumption remains load-bearing and unmeasured: **that a model can reliably emit behaviour scripts.**

## Repository shape

```text
src/       published packages: core/ (framework) and modules/ (IModule domains),
           plus the publish gate that polices them
os/        the product: silo (OS.Host, northbound MCP folded in), HTTP/SSE UI edge
           (OS.UiEdge), behavior worker, assistant neuron, AppHost
clients/   flutter/core (pure Dart edge) and flutter/shell (Material chrome)
samples/   product-shaped compositions and process neurons (not packable product)
tests/     fixtures/apphosts — shared L2 AppHost scaffolding
```

Southbound MCP transport lives in package `DigitalBrain.Mcp` (Salesforce and shared OAuth rail
mechanics). Gmail no longer uses southbound MCP — it calls Gmail REST through `Google.Apis.Gmail.v1`
with a reflected read-only planner catalog. Northbound agent tools live in library
`DigitalBrain.OS.AgentTools`, mapped on the silo host — not a separate product process. Older docs
that say `DigitalBrain.Integrations.Mcp` mean the southbound package.

Retired prototype generations live in git history — `git log --diff-filter=D --summary`, then
`git show <sha>^:<path>`.

## Running and verifying

```powershell
git clean -fdx
aspire run
```

The explicit product suite performs a self-cleaning live Aspire proof across resource health,
Gemma4 chat, command retry, durable journals, owner-scoped introspection, and OpenTelemetry:

```powershell
dotnet test os/tests/DigitalBrain.OS.Product.Tests -c Release -- -explicit only
```

[CLAUDE.md](CLAUDE.md) is the working discipline for every agent and contributor: the gates, the
verification ladder, and the traps. A green test suite is necessary, not sufficient — it proves the
code holds, not that a behaviour works.

The public site lives in [intochat/digitalbrain.docs](https://github.com/intochat/digitalbrain.docs)
and publishes **https://digitalbrain.tech**.
