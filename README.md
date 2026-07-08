# DigitalBrain

**DigitalBrain** is the .NET Aspire + Orleans kernel for self-evolving personal OS where everything is a **Neuron** (Orleans grain) or **Synapse** (typed message).

The core product: **safe, explicit, journaled, human-approved self-evolution is the *only* path** for any user-visible mutation (packs, automations, new neurons, Ino proposals, foundry runs, etc.).

Ino (the orchestrator) + Marketplace + Foundry + automations all stage proposals. Only approved decisions apply effects. Everything is durable, replayable, and rollback-capable.

Thin client (Flutter + RFW/ForUI) consumes server-driven `UiSurface` / `UiWidgetTree` emitted by neurons.

## Quick Start (Local)

From repo root:

```powershell
aspire doctor
dotnet build
dotnet test --logger "console;verbosity=minimal"
```

Full stack:

```sh
aspire run
```

See CLAUDE.md for the complete way of working, Elon's 5-step algorithm, iteration speed rules (MCP-first, parallel Context7, bg tests + polling, metrics + retro, self-evolution for WoW proposals), and pre-change ritual (Context7 + Aspire MCP + todo).

## Opt-in Test Suites

Two suites are skipped by default because they're expensive or environment-sensitive — not because they're unmaintained:

- **Real-stack E2E** (`tests/DigitalBrain.Tests/E2E`): boots the full Aspire AppHost + Orleans silo and drives it over real gRPC/gRPC-Web. In Visual Studio, select `e2e.runsettings` as the solution-wide run settings file (Test > Configure Run Settings) and run any `[Trait("Category", "E2E")]` test — no env vars to remember. From the CLI: `RUN_REAL_STACK_E2E=true dotnet test --logger "console;verbosity=minimal"`. Never run in CI (see `.github/workflows/ci.yml`).
- **AppHost execution-mode tests** (`tests/DigitalBrain.Tests/Aspire/AddDigitalBrainExecutionModeTests.cs`): fast (no Docker/Orleans — only inspects the declared Aspire resource graph), but loading the `DigitalBrain.AppHost` assembly can collide with a running Aspire process on Windows, so it's opt-in: `RUN_APPHOST_MODEL_TESTS=true dotnet test --logger "console;verbosity=minimal" -p:EnableAppHostTests=true`. Don't run this while `aspire run`/`aspire start` has the AppHost live.

## Core Ideas

- **Self-evolution rail** (the point): Proposals → human (or trusted) approval → apply handler. Ino proposes; rail executes. No side doors for user mutations.
- **Neuron / Synapse**: Actor model with causation, journals, broadcasts.
- **Packs**: Signed C# (NeuroPack) embodied at runtime via collectible ALC. Marketplace install = instant new behavior.
- **UI**: Neurons emit rich server-driven surfaces. Client is thin renderer.
- **Aspire hosting**:  AppHost wires replicas, Ollama, storage, MCP, flutter client.

## Working Rules (see CLAUDE.md)

- Always follow Elon's 5 steps **in order**: less dumb reqs → delete (target 10%+) → simplify → accelerate → automate last.
- **Context7** for every library/framework API before touching code.
- **Aspire MCP + CLI** for fast inspection/restarts/logs/traces (prefer over full runs). Use resource-targeted restarts.
- Tests: `dotnet test --logger "console;verbosity=minimal"` from root only. No --filter. Launch bg + poll with MCP logs.
- After every change: build + above test + `aspire doctor` + MCP health. Log cycle time + 5-steps retro.
- Delete trash (especially docs/plans). Only living docs: this README + CLAUDE.md.
- Relative paths. Self-explanatory names. No vacuous summaries.
- Self-evolution is non-negotiable for mutations. Use rail to propose WoW improvements.
- Minimal/isolated starts when possible; pre-build for MCP; parallel Context7 + MCP.

## Vision (Self-Evolving System)

The OS evolves itself safely through one explicit rail. Durable journals capture proposals/decisions/applies. Ino is the smart front-end that proposes; the rail is the trusted executor. Human approval (or explicit trusted bypasses for seeds/dev) gates everything.

Packs, Ino creations, automations, foundry outputs — all flow the same way.

This is the architecture north star. See the SelfEvolution types + handlers in Core/Kernel for the implementation.

## Status

AppHost + kernels + self-evolution rail are live. Use `aspire doctor` and MCP tools for state.

For detailed rules and iteration improvements, read CLAUDE.md.

---

Built for speed, safety, and relentless self-improvement. Delete the dumb parts first.