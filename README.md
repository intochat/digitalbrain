# DigitalBrain

**One living workspace where you and your agents work together — safely.**

DigitalBrain is a personal operating system built on **.NET Aspire + Orleans**, with a Flutter workspace. The axiom:

> **Everything addressable is a Neuron** — chat, windows, the feed, connections, approvals, models, behaviors.

Not a metaphor. `INeuron` is a real Orleans grain contract. One universal grain type hosts every addressable object; the differences between a chat and a Gmail connection and a running behavior are *facets*, not competing runtimes. People and agents share one brain; the app is a live window over it; nothing touches the outside world without your approval.

Deep design: [`EVERYTHING-IS-A-NEURON.md`](EVERYTHING-IS-A-NEURON.md) · way of working: [`CLAUDE.md`](CLAUDE.md) · specs & plans under [`docs/superpowers/`](docs/superpowers/).

---

## The one execution path

```text
Client (Flutter | MCP | behavior script)
  -> Edge / Auth
  -> INO operation (deterministic fn | bounded model workflow)
  -> effect gate  (human-approval rail — unbypassable, in the kernel)
  -> connector adapter
```

Commands and queries travel as typed grain calls through one invocation pipeline: resolve identity → check grants → idempotency replay → resolve the facet handler → execute → **external mutation can only be *proposed* as an Effect Neuron** → persist (the journal is the truth) → project to the workspace → append the feed. Orleans streams carry progress and observation only, never commands.

## Repository shape

```text
kernel/
  Brain.Contracts     INeuron · NeuronAddress · Synapse · envelope · error taxonomy
  Brain.Kernel        one NeuronGrain on Orleans journaling · pipeline · effect gate · grant issuance
  Brain.Client        BrainCluster.Connect(args) + Get<T>(scope) typed proxies
modules/
  Brain.Modules.Sdk         kind registration · conformance suite · BrainTest harness
  Brain.Modules.Workspace   chat · window (two-tier block vocabulary) · feed compositor · catalog
  Brain.Modules.Ai          ILlm · model tier catalog (Fast/Balanced/Reasoning) · Ollama + AzureOpenAI
  Brain.Modules.Web         bounded journaled fetch
  Brain.Modules.Connections durable OAuth state machine · closed health union
  Brain.Modules.Google      gmail (read + effect-gated send)
  Brain.Modules.Salesforce  salesforce (read + effect-gated update)
  Brain.Modules.Behaviors   behavior lifecycle (hash + journal) · grant binding · Roslyn compile gate
edge/
  Brain.Mcp           neuron_describe · neuron_read · neuron_invoke
  Brain.UiGateway     POST /ui/invoke · GET /ui/read · GET /ui/describe · WS /ui/watch
hosts/
  Brain.Kernel.Host   the silo · DigitalBrain.AppHost · DigitalBrain.ServiceDefaults
behaviors/            single-file C# scripts run as cluster clients (+ BDD)
workspace/            the Flutter app: gateway client · block renderers · governed kind views · shell · inspector
tests/
  Brain.KernelTests · Brain.ConformanceTests
```

## Non-negotiable invariants

- **The human-approval rail is in the kernel.** Behavior scripts propose Effect Neurons and can never execute external mutations directly; grants bind to a content-hash script identity.
- **No credentials to Flutter.** The gateway carries no provider tokens; the workspace speaks only closed-vocabulary JSON + WebSocket.
- **The UI vocabulary is closed.** Five governed Tier-1 kinds + a versioned Tier-2 block set, rendered natively. New visual power is a new first-party widget, never a more powerful interpreter. RFW is gone.
- **The journal is the truth.** Every revision, decision, and grant is an appended event; "how the system evolved and what was decided" is answerable by construction.

## Run it

```bash
dotnet build Brain.slnx
dotnet test --logger "console;verbosity=minimal"     # kernel + conformance
cd hosts/DigitalBrain.AppHost && aspire run           # ollama + brain-kernel + brain-mcp + brain-ui
cd workspace && flutter test                          # the Flutter workspace
```

MCP agents connect to the Brain.Mcp edge; the Flutter workspace talks to Brain.UiGateway on `:5320`.
