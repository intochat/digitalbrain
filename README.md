# DigitalBrain

DigitalBrain is an open-source .NET framework for durable agents on Orleans and Aspire. Its
paradigm is **neurons and synapses**: neurons are durable Orleans-journaled agents, synapses are
typed facts with full lineage, and method-scoped `TestBrain` fixtures fire real multi-silo traffic
and assert on committed journals.

What it is being built toward:

> **A brain you program by writing ordinary C#, and that can program itself.**

```csharp
// production: builder.AddDigitalBrainClient(owner); inject IDigitalBrain
await brain.SendAsync<IAnalyst>(
    "incident-42",
    new SummaryRequested("Summarize the incident."));
```

The owner-bound `IDigitalBrain` facade enters through a session; neurons call typed capabilities such
as `ILlama32` inside the brain. The same vocabulary will later support approved C# behaviors generated
from natural language. See [docs/architecture.md](docs/architecture.md) for what is built versus
designed.

## The shape of it

- **A synapse is a fact** — a thin record, broadcast, no reply. **An interface method is a request** —
  directed at a capability, and it replies. Both are journaled; neither is privileged.
- **Modules own their domain** — contracts, neurons, dependencies, authentication, and Aspire
  resources. Kernel stays domain-neutral.
- **Namespaces and type names are architecture** — `DigitalBrain.AI.Ollama.ILlama32` is identity,
  not a lookup result from a model descriptor.
- **Behavior SDK foundation** — `DigitalBrain.Behaviors` supplies public program/context/manifest and
  artifact-identity contracts. Human-approved proposal, installation, and execution remain Designed,
  not Built.
- **Capability is the contracts package a script compiles against**, enforced where it resolves one.
- **Every install is a human-approved proposal**, journaled and reversible.

## Status

See the module status lines in [docs/architecture.md](docs/architecture.md) for what is built versus
designed.

[`docs/architecture.md`](docs/architecture.md) is the plan of record: ratified architecture, known
limitations, and remaining build order.

## Repository shape

```text
src/       domain-neutral framework packages
modules/   independently shipped domains (AI, Tasks, Time, Google, Salesforce, …)
hosts/     AppHost, silo, MCP, Ui, TestingAppHost, Quickstart hosts
samples/   Quickstart greeter module; AccountEnrichment process sample; Compositions (pre-Behavior-rail logic, not NuGet, not installed Behaviors)
tests/     L0 contracts; L1 suites (incl. Quickstart/Compositions); L2 HostTests
docs/      VitePress documentation and the published specification
```

Earlier prototype generations were retired to git history rather than kept on disk. Recover any of
them with `git log --diff-filter=D --summary` and `git show <sha>^:<path>`.

## Gate

The root gate, every phase, no exceptions:

```powershell
dotnet build DigitalBrain.slnx -c Release
dotnet test DigitalBrain.slnx -c Release --logger "console;verbosity=minimal"
```

Never `--filter` for the completion gate. The documentation gate:

```powershell
npm --prefix docs test
npm --prefix docs run build
```

Run these gates before asserting a commit is green; this page is not evidence that an arbitrary
commit has passed them.

Published docs: **https://digitalbrain.tech** (GitHub Pages via `.github/workflows/docs-pages.yml`).
Domain and DNS steps live in [docs/contributing.md](docs/contributing.md#documentation-site-on-github-pages).

## Way of working

[CLAUDE.md](CLAUDE.md) is the canonical working discipline for every agent and contributor, and
`AGENTS.md` points there. The contributing guide is [docs/contributing.md](docs/contributing.md).
