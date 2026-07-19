# DigitalBrain

DigitalBrain is an open-source .NET framework for durable agents. Its paradigm is **neurons,
synapses, and simulations**: neurons are durable Orleans-journaled agents, synapses are immutable
typed messages with full lineage, and simulations fire synapses into a real in-process cluster and
assert on the timeline. It is built on Orleans and Aspire and is the foundation the full Digital
Brain system grows on.

## Status

The v2 foundation is being rebuilt from scratch on `master`. The previous implementation survives
only as git history; the prototype generations under `sources/` are read-only requirement evidence.
No packages are published yet. See [website/status.md](website/status.md) for the milestone state.

## Repository shape

```text
src/       framework packages (Abstractions, Kernel, Client, Testing, Aspire, Aspire.Hosting, DevTools)
tests/     contract tests, simulations, hosted proof
website/   VitePress documentation and the published specification
docs/      planning material
sources/   historical prototypes, read-only evidence
```

## Gate

```powershell
dotnet test .\DigitalBrain.slnx -c Release
cd website
npm ci
npm test
npm run build
```

Every commit keeps the gate green.

## Way of working

[CLAUDE.md](CLAUDE.md) records the working discipline; [GOAL.md](GOAL.md) is the active execution
contract for the rebuild.
