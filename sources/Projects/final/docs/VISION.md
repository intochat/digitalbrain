# DigitalBrain — Product Vision

> A new kind of operating system built out of neurons and synapses.
> **ino** is the personal assistant that lives on it. **Experiences** are how capability spreads between brains.

## The one-sentence pitch

Your computer stops being a pile of apps and becomes a **living brain**: every capability is a neuron (an Orleans grain), every interaction is a synapse (a typed, durable, replayable event), and anything you do regularly can be **packed into an experience and given to a friend**, whose brain grows that capability instantly — no restart, no installer, no app store gatekeeper.

## Why this is different from an OS, an app store, or an agent framework

| Conventional world | DigitalBrain |
|---|---|
| Apps are opaque binaries with hidden state | Experiences are transparent capsules: contract (synapses handled/emitted) + behavior (`.ino` / neurons) + evidence (usage profile from real journals) |
| Installing = copying files, restarting | Installing = the brain grows N+1 handlers at runtime (proven in `DistributionDynamicHandlers.feature`) |
| Integration between apps = vendor APIs | Every neuron already speaks one language: the shared timeline of synapses |
| Telemetry is an afterthought | The journal IS the system: durable Incoming/Outgoing per neuron, full causal replay |
| AI assistant bolted on top | ino is a neuron among neurons — it observes the same timeline, reads the same journals, installs the same experiences it recommends |
| Distribution = centralized store | Distribution = brains exchanging capsules, starting on your LAN, scaling to GlobalBrain |

## The three-layer product

1. **InoLang + BrainOS (OSS)** — the substrate. Synapse protocol, neuron model (`DurableNeuron` over Orleans 10 + Journaling), `.ino`/`.brain` formats, dispatch source-gen, the simulation harness. Open so the synapse contract becomes a standard.
2. **DigitalBrain (proprietary)** — the product. Kernel (boot/gateway/identity), ino assistant, Creator self-evolution loop, TUI/Flutter clients, world orchestration via Aspire, the polished local experience.
3. **GlobalBrain (marketplace)** — the network. Starts as a **LAN marketplace built into every kernel** (this milestone), grows into a federated registry where brains publish, discover, rate, and monetize experiences.

## Core Laws (invariants the codebase enforces or must enforce)

1. **Everything is a Neuron or a Synapse.** No side channels. Even UI is a synapse (`UiSurface`), even lifecycle is (`Activated`, `SynapseIncoming/Outgoing`).
2. **The journal is the truth.** Durable per-neuron Incoming/Outgoing lists give causal replay; AI reasoning, packing, and debugging all read the same tape.
3. **Install grows the brain, never restarts it (L0/L1 — proven N+1 today). Upgrading an L3 silo bundle restarts that bundle's silo only; the brain, the kernel silo, and the UI keep running.** `InstallBundle → BundleInstalled` broadcast → new handlers participate immediately (the N+1 proof). `UpgradeBundle` on an L3 promoted bundle (e.g. google-auth .AsSilo) drives Aspire.RestartResourceAsync on that bundle silo resource only; Orleans cluster membership + retry semantics handle the handoff.
4. **Experiences carry evidence.** A packed experience includes a usage profile derived from real journals — "tested by living," not just by CI.
5. **Trust is earned in a world.** Risky installs route through a sandbox world (Aspire-spawned cluster) before promotion to the root brain.
6. **ino is a peer, not a god.** The assistant proposes (`ImprovementProposal`), gates (`ActionRunSimulation`), and installs through the same public synapses any neuron uses.

## Who it's for (in order of attack)

1. **You** — the developer dogfooding a brain that packs its own workflows.
2. **Power users / tinkerers on a LAN** — family/team brains sharing automation recipes: weather watchers, task supervisors, review bots.
3. **Indie experience authors** — write an `.ino`, prove it by using it, publish to GlobalBrain, earn installs.
4. **Teams** — a department brain where domain experiences (like `awesome-se-team`) encode how the team works.

## The flagship demo (the bar everything must clear)

> On machine A, Vlad chats with ino in the TUI and uses a weather-watcher daily for a week.
> He types `/pack weather-watcher`, then `/publish weather-watcher`.
> On machine B (his friend's account, same LAN), the friend types `/market root@vlads-pc:30000`, sees the listing with Vlad's usage evidence, types `/install weather-watcher`.
> Machine B's brain verifies the content hash, installs, the handler count grows N+1, and the friend asks ino "what's the weather in Kyiv" — answered by the experience Vlad lived with.

Everything in the roadmap serves this demo first; GlobalBrain generalizes it.

## North-star metrics

- Time from "I use this regularly" → "my friend's brain does it too": **< 60 seconds**.
- Installs requiring a restart: **0**.
- Listings carrying real usage evidence: **100%**.
- Creating + connecting a second account/world: **one command**.
