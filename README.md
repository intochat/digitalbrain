# DigitalBrain

DigitalBrain is an open-source .NET framework for durable agents on Orleans and Aspire. Its
paradigm is **neurons, synapses, and simulations**: neurons are durable Orleans-journaled agents,
synapses are typed messages with full lineage, and simulations fire synapses into a real in-process
cluster and assert on the timeline.

What it is being built toward:

> **A brain you program by writing ordinary C#, and that can program itself.**

```csharp
var brain = DigitalBrainClient.Connect(grains, "acme");
await brain.SendAsync<IAnalyst>(
    "incident-42",
    new SummaryRequested("Summarize the incident."));
```

The owner-bound client enters through a session; neurons call typed capabilities such as `ILlama32`
inside the brain. The same vocabulary will later support approved C# behaviors generated from
natural language. See [website/architecture.md](website/architecture.md) for what is built versus
designed.

## The shape of it

- **A synapse is a fact** — a thin record, broadcast, no reply. **An interface method is a request** —
  directed at a capability, and it replies. Both are journaled; neither is privileged.
- **Modules own their domain** — contracts, neurons, dependencies, authentication, and Aspire
  resources. Kernel stays domain-neutral.
- **Namespaces and type names are architecture** — `DigitalBrain.AI.Ollama.ILlama32` is identity,
  not a lookup result from a model descriptor.
- **Behaviors own logic** — single-file C# scripts carried as durable state by one registered grain
  type. Adding a verb needs only approval.
- **Capability is the contracts package a script compiles against**, enforced where it resolves one.
- **Every install is a human-approved proposal**, journaled and reversible.

## Status

The durable foundation, generated module activation, typed AI neurons, and AI-owned Aspire integration
are built and unpublished. Agent/group-chat implementations, semantic discovery, integration modules,
and the scripting rail are not built. See [website/status.md](website/status.md).

[`REFINED-ARCHITECTURE-AND-NEXT-STEPS.md`](REFINED-ARCHITECTURE-AND-NEXT-STEPS.md) is the plan of
record: current status, ratified architecture, hard deletion manifest, and ordered implementation.

## Repository shape

```text
src/       domain-neutral framework packages
modules/   independently shipped domains, beginning with AI
hosts/     runnable silo, AppHost, and the test-only probe hosts
samples/   package-only consumers proven against an empty package cache
tests/     contract tests, simulations, hosted proof
eng/       pack and verification scripts
docs/      VitePress documentation and the published specification
```

Earlier prototype generations were retired to git history rather than kept on disk. Recover any of
them with `git log --diff-filter=D -- sources/` and `git show <sha>^:<path>`.

## Gate

The fast gate, run at every slice and before any completion claim:

```powershell
dotnet test --logger "console;verbosity=minimal"
```

The full gate, run before a release:

```powershell
dotnet test .\DigitalBrain.slnx -c Release
.\eng\pack.ps1
.\eng\verify-consumer.ps1
.\eng\verify-dependencies.ps1
```

Never `--filter`, on either. The website gate runs `node` directly rather than through npm:

```powershell
cd docs
node tools/render-specification.mjs
node --test tests/*.test.mjs
```

Every commit keeps the gate green.

## Way of working

[CLAUDE.md](CLAUDE.md) is the canonical working discipline for every agent and contributor, and
`AGENTS.md` points there. The contributing guide is [docs/contributing.md](docs/contributing.md).
