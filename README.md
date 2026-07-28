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
| Typed AI, Tasks, Google, Salesforce, Chat, Flutter, Quickstart families | **Built** |
| Flutter vertical — shell/scene vocabulary, UI HTTP/SSE edge, `WithUIEdge`/`WithFlutterHost`, headless Dart host, Windows chrome | **Built** |
| Product MCP surface — durable chat send/read, neuron journal observation, active-neuron discovery | **Built** |
| Time — durable one-shot `ICountdown` and its recovery tests | **Built** |
| Time — reminders, recurring interval/calendar scheduling, DST | Designed |
| Product chrome polish, multi-principal IdP edge, journal observation on `IDigitalBrain` | Designed |
| Behavior rail — proposal, approval, installation, execution, rollback | Designed |
| Observability spine — host OpenTelemetry, structured logs, causal kernel spans, GenAI spans and metrics | **Built** |

`DigitalBrain.Behaviors` is a packable SDK foundation (authoring interfaces, constrained context,
manifests, artifact identities) and holds the canonical artifact codec. It is not a compiler, builder,
broker or execution rail. Chat today is *behaviour-shaped, not behaviour-installed* — its program is a
real `IIntentProgram` composed at build time.

One assumption is load-bearing and unmeasured: **that a model can reliably emit behaviour scripts.**

## Repository shape

```text
src/       published packages: core/ (framework) and modules/ (IModule domains),
           plus the publish gate that polices them
os/        the product: silo, MCP server, OS behaviours, AppHost
clients/   Flutter shell and the Dart wire package
tests/     fixtures/ — shared test subjects and their scaffolding AppHosts
```

Retired prototype generations live in git history — `git log --diff-filter=D --summary`, then
`git show <sha>^:<path>`.

## Running and verifying

```powershell
git clean -fdx
aspire run
```

[scripts/verify-product.ps1](scripts/verify-product.ps1) performs the Release build and a live,
self-cleaning Aspire proof across resource health, MCP discovery, Gemma4 chat, durable journals, and
OpenTelemetry:

```powershell
./scripts/verify-product.ps1
```

[CLAUDE.md](CLAUDE.md) is the working discipline for every agent and contributor: the gates, the
verification ladder, and the traps. A green test suite is necessary, not sufficient — it proves the
code holds, not that a behaviour works.

The public site lives in [intochat/digitalbrain.docs](https://github.com/intochat/digitalbrain.docs)
and publishes **https://digitalbrain.tech**.
