# DigitalBrain

DigitalBrain is an open-source .NET framework for durable agents. Its paradigm is **neurons,
synapses, and simulations**: neurons are durable Orleans-journaled agents, synapses are immutable
typed messages with full lineage, and simulations fire synapses into a real in-process cluster and
assert on the timeline. It is built on Orleans and Aspire and is the foundation the full Digital
Brain system grows on.

## Status

The v2 foundation is complete and unpublished. No packages are on NuGet; the prerelease packages are
built locally and staged for approval. The previous implementation survives only as git history, and
the prototype generations under `sources/` are read-only requirement evidence.

See [website/status.md](website/status.md) for the milestone table, the gates, and the open debts.

## Repository shape

```text
src/       framework packages (Abstractions, Kernel, Client, Testing, Aspire, Aspire.Hosting, DevTools)
hosts/     runnable silo, AppHost, and the test-only probe hosts
samples/   package-only consumers proven against an empty package cache
tests/     contract tests, simulations, hosted proof
eng/       pack and verification scripts
website/   VitePress documentation and the published specification
sources/   historical prototypes, read-only evidence
```

## Gate

```powershell
dotnet test .\DigitalBrain.slnx -c Release
.\eng\pack.ps1
.\eng\verify-consumer.ps1
.\eng\verify-dependencies.ps1
cd website
npm ci
npm test
npm run build
```

Every commit keeps the gate green.

## Way of working

[CLAUDE.md](CLAUDE.md) records the working discipline; [GOAL.md](GOAL.md) is the execution contract
for the rebuild. The contributing guide is [website/contributing.md](website/contributing.md).
