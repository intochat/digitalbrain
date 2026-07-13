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

- After `git clean -fdx`, `dotnet build` (or `aspire run`) auto-initializes the CodeGraph index via MSBuild target.
- Use the `codegraph` MCP server for architecture queries.

Full stack:

```sh
aspire run
```

See CLAUDE.md for the complete way of working, Elon's 5-step algorithm, iteration speed rules (MCP-first, parallel Context7, bg tests + polling, metrics + retro, self-evolution for WoW proposals), and pre-change ritual (Context7 + Aspire MCP + CodeGraph + todo).

**Rely on CodeGraph** (configured in .mcp.json) for architecture understanding, symbol exploration, and call-path analysis.

## Test Suites

The root test command is expected to run every test with zero skips:

```powershell
dotnet test --logger "console;verbosity=minimal"
```

- **Real-stack E2E** (`tests/DigitalBrain.Tests/E2E`) boots the full Aspire AppHost + Orleans silo and drives it over real gRPC/gRPC-Web.
- **AppHost execution-mode tests** (`tests/DigitalBrain.Tests/Aspire/AddDigitalBrainExecutionModeTests.cs`) inspect the declared Aspire resource graph without calling `BuildAsync`/`StartAsync`.

Do not keep a separate `aspire run` / `aspire start` session alive while running the full root test suite; the E2E fixture owns its AppHost lifecycle.

## Core Ideas

- **Self-evolution rail** (the point): Proposals → human (or trusted) approval → apply handler. Ino proposes; rail executes. No side doors for user mutations.
- **Neuron / Synapse**: Actor model with causation, journals, broadcasts.
- **Packs**: Signed C# (NeuroPack) embodied at runtime via collectible ALC. Marketplace install = instant new behavior.
- **UI**: Neurons emit rich server-driven surfaces. Client is thin renderer.
- **Aspire hosting**:  AppHost wires replicas, Ollama, storage, MCP, flutter client.

Use the CodeGraph MCP (see .mcp.json and CLAUDE.md) as the primary tool for architecture and codebase understanding.

## Target Dependency Direction

```text
DigitalBrain.Core
        ^
DigitalBrain.Kernel.Abstractions
        ^
        +----------------+----------------+
        ^                ^                ^
DigitalBrain.Kernel  DigitalBrain.Google  DigitalBrain.Salesforce
        ^                ^                ^
        +--------- DigitalBrain.RuntimeHost --------+
                           ^
             DigitalBrain.AppHost resource graph

DigitalBrain.Mcp -> DigitalBrain.Kernel.Abstractions
```

The final names may be simplified after deletion, but dependency direction must remain inward. `DigitalBrain.RuntimeHost` is the only process project allowed to compose the runtime with concrete providers. `DigitalBrain.AppHost` models resources and references the RuntimeHost executable without owning its service registrations.

No external mutation may bypass `InoEffectPlanAuthority`, durable approval evidence, idempotency, lease/fence checks, and outcome verification.

## Working Rules (see CLAUDE.md)

- Always follow Elon's 5 steps **in order**: less dumb reqs → delete (target 10%+) → simplify → accelerate → automate last.
- **CodeGraph MCP for architecture understanding**: Use the `codegraph` server (from .mcp.json; auto-inits on build after `git clean -fdx`) for symbols, call graphs, impact analysis, and architecture exploration. Prefer it over manual file reads or grep.
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

This is the architecture north star. Use the CodeGraph MCP for exploring the implementation. See the SelfEvolution types + handlers in Core/Kernel for the implementation.

## Status

AppHost + kernels + self-evolution rail are live. Use `aspire doctor`, MCP tools (incl. `codegraph` for architecture), and `codegraph status` for state.

For detailed rules and iteration improvements (including CodeGraph for architecture), read CLAUDE.md.

---

Built for speed, safety, and relentless self-improvement. Delete the dumb parts first.
