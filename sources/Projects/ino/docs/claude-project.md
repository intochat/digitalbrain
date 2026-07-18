# ino — IAW Native OS

Pinned descriptions for the Anthropic Claude project. The short form fits the
project description field; the long form is the project's onboarding README
that gives a fresh agent enough context to be useful from message one.

## Title

`ino — IAW Native OS`

## Short description (project description field)

> AI-native OS built on three primitives — **neurons** (Orleans grains,
> LLM-optional), **synapses** (durable typed messages that are signal +
> memory + thinking at once), and a **self-improving L1/L2/L3 loop**. Sits
> on the **IAW substrate** (Orleans 10 + Aspire 13 + Agent runtime). Ships a
> Flutter web/mobile/Telegram client and a domain-first model — Travel,
> Taxi, Recall, Reminders, Location — with the canonical 6-hop **Plan Trip**
> as the reference domain flow. Working repo at `E:\ino`, `master` branch.
> Build via `aspire run` (never `dotnet run --project`). Verify in browser +
> Aspire dashboard traces; build+test alone is not "done."

## Long description (pinned project README)

ino is an AI-native OS where every capability is an Orleans grain (a
**neuron**) and every interaction is a durable typed message (a **synapse**)
that doubles as memory and reasoning. Built on the **IAW substrate** —
Orleans 10 + Aspire 13 + an Agent base class with `IChatClient` streaming,
tool registration, durable chat history.

**Two neuron base classes ship.** Pure-code `Neuron<TEvent>` and LLM-backed
`LlmNeuron<TEvent> : IAW.Core.Agent`. Cortex is **open-closed** via per-domain
plans, **ML-routed** via LightGBM, and **self-improving** via an L1 loop:
`MissedIntentTracker` → `NeuronOptimizer` → `CreatorNeuron` → human-gated
approval in the Flutter Inspector → dynamic neuron registration in Discovery
materialises the new capability with no silo restart.

**v0.1 ships:** Travel (the 6-hop Plan Trip with weather + events +
activities + RFW cards), Taxi (Uber MCP scaffold), Recall + Reminders +
Location bridges, plus a Telegram mini-app reusing the Flutter web bundle
and a planned 3D brain home screen that visualises the live neuron/synapse
graph with real synapse-fire pulses.

**Verification doctrine:** build + test alone is not "done." The Aspire
dashboard must report every resource Healthy, the Flutter UI must be driven
in a real browser, and Aspire **Traces** must show end-to-end `traceparent`
propagation from `grpc Chat` through every cross-silo grain hop. UI changes
are verified via the Flutter OTel exporter (`ino.grpc.requests`,
`ino.grpc.duration`, `ino.chat.messages`, BLoC transitions in Structured
Logs) — not by squinting at the screen.

**Load-bearing docs to read first** (in this order):

1. `docs/product-vision-final.md` — the 14 locked v0.1 decisions.
2. `docs/domain-neuron-anatomy.md` — what makes a domain neuron flow good, the
   BDD-mock → real-LLM two-interface seam, the 6-hop scope.
3. `docs/plan-poc-phase-4.md` — per-slice execution log.
4. `CLAUDE.md` — load-bearing operational rules and the "known traps" list.

**Operational rules that bite the unwary:**

- Never `dotnet run --project`. Always `aspire run` (foreground) or
  `aspire start --isolated` + `aspire stop` (background service). The repo
  ships `aspire.config.json` so neither command needs `--apphost` from `E:\ino`.
- Per-resource rebuilds via the Aspire MCP tools, not full AppHost restarts:
  `mcp__aspire__execute_resource_command(resourceName="kernel", commandName="rebuild")`.
- `INO_TEST_MODE=true` (default) wires `BddMockChatClient`; flipping it to
  `false` swaps in xAI Grok via `XaiProviderFactory`. **Today, only Cortex
  routing is real-LLM-driven.** Plans past routing (e.g. `PlanTripPlan`) are
  state machines over mock corpora — the LlmNeuron rewrite is the next big
  slice.
- Context7 is mandatory before writing library-touching code. No exceptions.
- Never read or reference paths under `C:\Users\` (local NuGet cache, user
  profile). Stay in the project directory.

**Substrate boundary:** `iaw/` is in the tree as the Orleans + Agent runtime
substrate. ino projects ProjectReference iaw assemblies; `AddIno()`
delegates to `AddIAW()`; `LlmNeuron<TEvent>` inherits `IAW.Core.Agent`.
Don't bypass the substrate or duplicate its primitives.

**Out of scope for Phase 4:** Cortex self-creation beyond L1, cross-user
missed-intent aggregation, full multifractal spectrum research, revenue model
decisions, domains beyond Travel + Taxi, full Telegram or ino-windows
migration. If a review comment pushes toward any of these, defer with a link
to the post-v0.1 epic list in `docs/product-vision-final.md`.
