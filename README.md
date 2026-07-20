# DigitalBrain

DigitalBrain is an open-source .NET framework for durable agents, built on Orleans and Aspire. Its
paradigm is **neurons, synapses, and simulations**: neurons are durable Orleans-journaled agents,
synapses are typed messages with full lineage, and simulations fire synapses into a real in-process
cluster and assert on the timeline.

What it is being built toward:

> **A brain you program by writing ordinary C#, and that can program itself.**

```csharp
var brain = DigitalBrainClient.Connect();
var gpt   = brain.Get<IGpt56>();

await brain.On<NewMail>(async mail =>
{
    var verdict = await gpt.AskAsync($"Is this urgent? {mail.Body}");
    if (verdict.IsUrgent) await brain.Emit(new Escalation(mail.From));
});
```

That file is both a script you run against a cluster and a behavior you install inside one. See
[website/architecture.md](website/architecture.md) for the design and what is built versus designed.

## The shape of it

- **A synapse is a fact** — a thin record, broadcast, no reply. **An interface method is a request** —
  directed at a capability, and it replies. Both are journaled; neither is privileged.
- **Modules own vocabulary** — synapse records and neuron interfaces. Compile-time, because Orleans
  freezes its grain type manifest at silo startup. Adding a noun needs a rebuild.
- **Behaviors own logic** — single-file C# scripts carried as durable state by one registered grain
  type. Adding a verb needs only approval.
- **Capability is the contracts package a script compiles against**, enforced where it resolves one.
- **Every install is a human-approved proposal**, journaled and reversible.

## Status

The v2 foundation is complete and unpublished. No packages are on NuGet. The scripting rail described
above is **designed and not yet built** — see [website/status.md](website/status.md) for the milestone
table, the gates, the open debts, and the proofs deliberately held red.

[`REFINED-ARCHITECTURE-AND-NEXT-STEPS.md`](REFINED-ARCHITECTURE-AND-NEXT-STEPS.md) is the plan of
record: current status, ratified architecture, hard deletion manifest, and ordered implementation.

## Repository shape

```text
src/       framework packages (Abstractions, Kernel, Client, Testing, Aspire, Aspire.Hosting, DevTools)
hosts/     runnable silo, AppHost, and the test-only probe hosts
samples/   package-only consumers proven against an empty package cache
tests/     contract tests, simulations, hosted proof
eng/       pack and verification scripts
website/   VitePress documentation and the published specification
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
cd website
node tools/render-specification.mjs
node --test tests/*.test.mjs
```

Every commit keeps the gate green.

## Way of working

[CLAUDE.md](CLAUDE.md) is the canonical working discipline for every agent and contributor, and
`AGENTS.md` points there. The contributing guide is [website/contributing.md](website/contributing.md).
