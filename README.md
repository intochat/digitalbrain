# DigitalBrain

**DigitalBrain** is the .NET Aspire + Orleans runtime for a self-evolving personal OS.

Keep one path: `Client -> Edge/Auth -> INO operation -> deterministic function or bounded model workflow -> effect gate -> connector adapter`. Commands and queries use typed grain interfaces. Orleans streams are reserved for progress, fan-out, and observability. The generic Neuron/Synapse runtime, legacy gateway, second auth system, Foundry execution loop, pack runtime, and duplicate UI rail are removed after their remaining behavior is either migrated or explicitly discarded.

## Quick Start (Local)

From repo root:

```powershell
aspire doctor
dotnet build
dotnet test --logger "console;verbosity=minimal"
```

- After `git clean -fdx`, `dotnet build` (or `aspire start`) auto-initializes the CodeGraph index via MSBuild target.
- Use the `codegraph` MCP server for architecture queries.

Full stack:

```sh
aspire start
```

See CLAUDE.md for the complete way of working, Elon's 5-step algorithm, iteration speed rules (MCP-first, parallel Context7, bg tests + polling, metrics + retro, self-evolution for WoW proposals), and pre-change ritual (Context7 + Aspire MCP + CodeGraph + todo).

**Rely on CodeGraph** (configured in .mcp.json) for architecture understanding, symbol exploration, and call-path analysis.

## Test Suites

The root test command is expected to run every test with zero skips:

```powershell
dotnet test --logger "console;verbosity=minimal"
```

- **Real-stack E2E** (`tests/DigitalBrain.E2ETests`) boots the full Aspire AppHost + Orleans silo and drives it over real gRPC/gRPC-Web.
- **AppHost model tests** (`tests/DigitalBrain.AppHostTests`) inspect the declared Aspire resource graph without starting it.

Do not keep a separate `aspire run` / `aspire start` session alive while running the full root test suite; the E2E fixture owns its AppHost lifecycle.

## Core Ideas

- **External mutation rail**: Durable INO effect plans with approval evidence, idempotency, lease/fence checks, and outcome verification.
- **External edge**: V2 UI gRPC plus the retained MCP operation surface.
- **Orleans**: Typed grain interfaces for commands and queries; streams only for progress, fan-out, and observability.
- **Aspire hosting**:  AppHost wires replicas, Ollama, storage, MCP, flutter client.

Use the CodeGraph MCP (see .mcp.json and CLAUDE.md) as the primary tool for architecture and codebase understanding.

## Target Dependency Direction

```text
Provider Contracts       Feature SDK       Kernel Contracts
       ^                      ^                  ^
       |                      |                  |
Provider Integrations    Feature releases    Kernel runtime
       ^                      ^                  ^
       +---------- RuntimeHost authority -------+
                              ^
                    MCP/UI + FeatureHost
                              ^
                 AppHost resource composition
```

Dependencies point inward toward the three contract seams. `DigitalBrain.RuntimeHost` is the only process project allowed to compose the kernel with concrete providers. `DigitalBrain.AppHost` models resources and references process projects without owning their service registrations.

No external mutation may bypass `InoEffectPlanAuthority`, durable approval evidence, idempotency, lease/fence checks, and outcome verification.

## Working Rules (see CLAUDE.md)

- Always follow Elon's 5 steps **in order**: less dumb reqs → delete (target 10%+) → simplify → accelerate → automate last.
- **CodeGraph MCP for architecture understanding**: Use the `codegraph` server (from .mcp.json; auto-inits on build after `git clean -fdx`) for symbols, call graphs, impact analysis, and architecture exploration. Prefer it over manual file reads or grep.
- **Context7** for every library/framework API before touching code.
- **Aspire MCP + CLI** for fast inspection/restarts/logs/traces (prefer over full runs). Use resource-targeted restarts.
- Tests: `dotnet test --logger "console;verbosity=minimal"` from root only. No --filter. Launch bg + poll with MCP logs.
- After every change: build + above test + `aspire doctor` + MCP health. Log cycle time + 5-steps retro.
- Delete superseded documentation. Keep this README and CLAUDE.md current; retain an active approved spec or implementation plan only while its work remains open.
- Relative paths. Self-explanatory names. No vacuous summaries.
- Self-evolution is non-negotiable for mutations. Use rail to propose WoW improvements.
- Minimal/isolated starts when possible; pre-build for MCP; parallel Context7 + MCP.

## Status

AppHost + kernels + self-evolution rail are live. Use `aspire doctor`, MCP tools (incl. `codegraph` for architecture), and `codegraph status` for state.

For detailed rules and iteration improvements (including CodeGraph for architecture), read CLAUDE.md.

---

Built for speed, safety, and relentless self-improvement. Delete the dumb parts first.
