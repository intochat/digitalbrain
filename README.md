# DigitalBrain

An open-source .NET framework for durable agents on Orleans and Aspire. Its paradigm is **neurons and
synapses**: neurons are durable Orleans-journaled agents, synapses are typed facts with full lineage,
and method-scoped `TestBrain` fixtures fire real multi-silo traffic and assert on committed journals.

> **A brain you program by writing ordinary C#, and that can program itself.**

```csharp
// grain clients (mcp, scripting): builder.AddDigitalBrainClient(); inject IDigitalBrain
// silo host only: builder.AddDigitalBrainSilo(silo => silo.AddDigitalBrain());
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
| Shell vertical — shell/scene vocabulary, Kernel-hosted HTTP/SSE maps (`MapChatStreams`/`MapShellStreams`/`MapOAuthCallback`), Flutter hosts (`WithHeadlessHost`/`WithWindowHost`/`WithWebHost`), Windows + web chrome | **Built** (web deploy host; K1 six-view pixel parity on web still open) |
| Product shell — responsive Chat, content-safe Activity, live 3D-projected Brain topology, pulses and inspector | **Built** |
| Behavior Studio surface (six views, host APIs) | Removed |
| NL → C# authoring / Scripting | Designed — product `DigitalBrain.Scripting` is a dummy generate+run chat proof; full AI→C# rail not in tree |
| Product MCP surface — durable chat send/read, neuron journal observation, active-neuron discovery | **Built** — northbound MCP is `DigitalBrain.Mcp` (cluster client) on `/mcp` port 5000; tools call `IDigitalBrain` |
| Introspection — model-callable journal tally, journal read, and topology read as brain capabilities (`introspection.tally-journal-request`, `introspection.read-journal-request`, `introspection.read-topology-request`) | **Built** — the capabilities answer correctly when invoked, live-verified via the MCP surface above and via deterministic capability-call tests. Live Gemma4 (`gemma4:12b`) did **not** select `introspection.tally-journal-request` across repeated real turns in one session, even when told its name: it answered "how many messages have I sent" from conversation memory, once exhausted its context budget mid-deliberation without answering at all, and once substituted `chat.read-transcript-request` and counted turns manually. The off-by-one tally policy (the in-flight question counts) held in every case where an answer was given. Model tool-selection for this capability is an open live gap, not a code gap — see CLAUDE.md traps |
| Dual live Google + Salesforce OAuth productization | In progress — Gmail is Google SDK (REST) with reflected read-only catalog + typed ops; one browser sign-in flow via app callback `/oauth/callback`; Salesforce stays MCP. Unverified Google app in Testing mode: re-consent every 7 days, ≤100 test users. Register redirect `http://localhost:5080/oauth/callback` (owner re-registration pending, lane g6). Live dual-provider proof pending |
| Time — durable one-shot `ICountdown` and its recovery tests | **Built** |
| Time — reminders, recurring interval/calendar scheduling, DST | Designed |
| Multi-principal IdP edge, journal observation on `IDigitalBrain` | Designed |
| Multi-model UI combine / Settings model switch | Designed |
| Docker product image (`digitalbrain` = silo + northbound MCP) | In progress — Kernel Dockerfile + local `docker-compose.yml` smoke; Flutter is not a container image; no CI image build, no Docker Hub publish |
| Scripting — external worker proof that generates a single-file C# brain client and prints a chat reply | **Built** (dummy) |
| Observability spine — host OpenTelemetry, structured logs, causal kernel spans, GenAI spans and metrics | **Built** |

Chat still owns its turn in `ChatNeuron` today; `IAssistant` lives in the AI module. Authored
behavior packages (`DigitalBrain.Behaviors` / Runtime) are removed from the product tree; residual
`BehaviorId` identity types remain in Abstractions for Tasks/Security wire compatibility.

## Repository shape

```text
DigitalBrain.AppHost/   local Aspire composition
DigitalBrain.Kernel/    Orleans silo + thin HTTP maps (chat stream, shell, OAuth, health)
DigitalBrain.Mcp/       northbound MCP cluster-client process
DigitalBrain.Scripting/ dummy generate+run brain-client proof
src/                    published packages: core/ (framework) and modules/ (IModule domains)
clients/                flutter/core (pure Dart transport client) and flutter/shell (Material chrome)
samples/                product-shaped compositions and process neurons (not packable product)
```

Southbound MCP transport lives in `DigitalBrain.Modules.Sdk` under namespace
`DigitalBrain.Modules.Sdk.Mcp` (plus Webhook). Google/Salesforce depend only on that package — never
on the northbound process. Gmail uses Gmail REST; Salesforce uses hosted MCP. Northbound agent tools
live in process `DigitalBrain.Mcp` (`AddDigitalBrainClient`). Docs that say MCP is co-hosted on the
silo are stale.

Aspire split: `DigitalBrain.Aspire.Hosting` is AppHost-only; `DigitalBrain.Aspire` owns
`AddDigitalBrainClient` (grain clients) and `AddDigitalBrainSilo` (silo host). Resource names are
shared via `DigitalBrainResourceNames` so hosting and client connection keys cannot drift.

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
dotnet test DigitalBrain.slnx -c Release
# live oracle when present:
# dotnet test os/tests/DigitalBrain.Product.Tests -c Release -- -explicit only
```

[CLAUDE.md](CLAUDE.md) is the working discipline for every agent and contributor: the gates, the
verification ladder, and the traps. A green test suite is necessary, not sufficient — it proves the
code holds, not that a behaviour works.

The public site lives in [intochat/digitalbrain.docs](https://github.com/intochat/digitalbrain.docs)
and publishes **https://digitalbrain.tech**.
