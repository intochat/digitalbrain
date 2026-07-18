# ino — IAW Native OS

**ino** is an AI-native operating system that runs inside every existing OS.
It is not a desktop metaphor, not a chatbot, not a shell wrapper.
It is a continuously self-improving multi-agent system, modeled loosely on how brains actually work.
You can treat it as a distributed self-improving system with shared knowledge and shared skills.

> ino is early research. This README describes **where ino is going**, not everything that exists today. See [`CLAUDE.md`](CLAUDE.md) for the current repo shape and [`docs/product-vision-final.md`](docs/product-vision-final.md) for the 13 locked v0.1 decisions.

---

## The three primitives

ino has three things, and that is the whole system.

### 🧠 Neurons — everything is a neuron

Every capability is a **neuron**: an Orleans grain that does one thing well. A neuron does NOT require an LLM — it can be pure code, a formula, a timer, a state machine, or an LLM-powered reasoning agent. Some neurons parse C# with Roslyn, some run shell commands, some track flight prices with no AI at all, some compose multi-step travel plans with an LLM. The LLM is just one tool a neuron might use. Neurons are added, removed, and rewired by the self-improving loop. They do not know about each other statically — they discover each other through synapses.

### ⚡ Synapses — the unified primitive

A synapse in ino is **one thing that plays three roles at once**. This unification is the central idea: the same abstraction solves communication, memory, and Turing-complete thinking.

**Signal.** A synapse is a typed, durable, at-least-once message delivered between neurons. Delivery is journaled; nothing in-flight is lost.

**Memory.** Every synapse carries a `decay` score from 0 to 100. Important synapses stay hot; untouched ones fade; cross-references lift them back up. Memories are not a separate store — the durable messages *themselves* are the memory.

| Decay | State | Meaning |
|---|---|---|
| **100** | Hot | Freshly relevant, in active context, returned first in recall |
| **30** | Cold | Default floor for older synapses, still searchable |
| **1** | Soft-deleted | Invisible to normal search, still in storage |
| **0** | Gone | Hard-deleted |

A nightly **sleep cycle** decays untouched synapses downward. Access boosts them back up. Important cross-references lift them to 100 or pin them permanently. This is how ino solves the "logs forever" problem: biologically-inspired forgetting, not a bigger store.

**Thinking.** When a neuron needs full programmatic logic — branching, loops, arbitrary computation — it **fires a synapse that carries executable C# code**. The code can call other neurons, loop over results, branch on conditions, compose computations. It is the neuron's thought, made executable. The power of a real programming language beats any custom workflow DSL.

### 🔄 Self-improvement — the loop

ino watches its own behavior, decides what's missing, and reshapes itself. It can add new neurons, rewire synapse topology, and hot-reload silos via Aspire — no human deploy. The sleep cycle that ages synapses is also the feedback channel: what mattered, what can be pruned, what should be amplified.

---

## Communication, memory, and thinking — all one thing

The conventional multi-agent framing has three separate stacks: message buses for communication, vector databases for memory, and orchestrators for thinking. ino rejects that split. **One primitive (the synapse) covers all three.** This is not a metaphor — it is a load-bearing design decision.

| Conventional | ino |
|---|---|
| Message bus for communication | Synapse (signal) |
| Vector DB / memory store | Synapse (decay) |
| Orchestrator / workflow engine | Synapse (code-carrying, thinking) |
| Central planner agent | No planner — thinking is local to each neuron |
| Static workflow graphs | On-demand C# code, full Turing power |
| Prescriptive | Expressive |

Intelligence emerges from four things, in order of importance:

1. **Model intelligence** — frontier LLMs with expert system prompts
2. **Topology** — which neurons can fire synapses at whom, rewritten by the self-improving loop
3. **Thinking-as-code** — C# code embedded in synapses, written and executed by neurons on demand
4. **Memory-as-decay** — synapses that age biologically, so only what matters survives

---

## What ino is *not*

- Not a boss-and-worker orchestration framework
- Not a shell for a single LLM
- Not a desktop with windows and icons
- Not a fixed workflow DAG
- Not a chat frontend with "tools"
- Not "AI-only" — neurons can be pure code, formulas, or state machines without any LLM

Think of it as a continuously running, self-modifying, introspectable society of specialized neurons that communicate, remember, and think — all through the same primitive. Some think with LLMs. Some think with code. Both are neurons.

---

## Domains — the v0.1 set

Two end-to-end domains ship in v0.1:

- **Travel** — trip planning, flights, hotels, places; integrates with **TripRadar** (external product at `tripradar/`).
- **Taxi** — ride-hailing via **Uber MCP** with the user's Google auth (if a real MCP server exists; scaffold-only otherwise).

Full decomposition in [`docs/product-vision-final.md`](docs/product-vision-final.md).

---

## Clients

ino visualises itself through multiple surfaces that share one Flutter codebase:

| Client | Renderer | Status |
|---|---|---|
| **Flutter web** | CanvasKit (Skia) | primary demo surface, served by the system silo |
| **Telegram mini-app** | reuses Flutter web | planned |
| **ino-windows desktop** | Flutter desktop | planned |

Persona rendering uses the marketplace Rive asset at `clients/ino.flutter/assets/rive/persona_orb.riv` with a CustomPaint fallback.

---

## Repository layout

```
ino/
├── ino.slnx                solution (the single solution; iaw/IAW.slnx is dormant)
├── src/                    Core, Core.Hosting (Neuron + LlmNeuron), Kernel, Identity, Gateway, AppHost, …
├── domains/                Travel (TripRadar), Taxi (Uber MCP, scaffold), Location, testing fixtures
├── iaw/                    IAW substrate — Orleans Agent runtime, Aspire AddIAW/AddIAWClient,
│                           and the Agent base class that LlmNeuron<TEvent> inherits from
├── clients/
│   └── ino.flutter/        the Flutter app (web + mobile + desktop targets)
├── test/                   kernel-level e2e + unit-style tests
├── tripradar/              external travel product ino integrates with (own slnx)
├── docs/                   product vision, Phase 3 plan, design notes
├── reviews/                demo hero screenshots
├── aspire.config.json      points the Aspire CLI at src/Ino.AppHost
├── CLAUDE.md               working instructions for Claude Code
└── README.md               this file
```

`AddIno()` is a thin wrapper over `AddIAW()` from `iaw/src/Aspire.Hosting`: ino silos run on the IAW Orleans cluster, IAW's Azure Blob storage emulator, and IAW's Qdrant vector store. The Aspire dashboard prompts on first run for any LLM API keys declared via `.WithLLM<T>()`.

---

## Prerequisites

```bash
winget install Microsoft.DotNet.SDK.Preview
winget install Microsoft.FoundryLocal
```

## Running

```bash
git clone https://github.com/InteractiveAgents/IAW.git ino
cd ino
dotnet build ino.slnx
aspire start --isolated
```

Aspire starts the AppHost detached; dashboard at the URL Aspire prints on startup. The dashboard exposes live traces with `gen_ai.*` semantic conventions for every LLM call, tool invocation, and grain call.

---

## Testing

E2E tests boot an in-process Orleans `TestCluster` + gRPC server, configure a mock LLM, and verify the full pipeline: gRPC request → neuron routing → tool call → RFW template → response. Playwright (headless Chromium) then loads the Flutter web app, intercepts the gRPC-Web response, and saves a screenshot.

```bash
CI=true dotnet test test/Ino.E2E.Tests
```

---

## Status

Active research and development. Phase 3 is in flight — see [`docs/plan-poc-phase-3.md`](docs/plan-poc-phase-3.md) for the slice-by-slice plan.

## Creators

- [LeftTwixWand](https://github.com/LeftTwixWand)
- [anton-kharchenko](https://github.com/anton-kharchenko)
- [EaGLenok](https://github.com/EaGLenok)
- [alexbardjo](https://github.com/alexbardjo)

## Contributors

- [ScientistFromMars](https://github.com/ScientistFromMars)

## License

See [LICENSE](LICENSE) for details.
