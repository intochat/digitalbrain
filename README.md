# DigitalBrain

DigitalBrain is an open-source .NET framework for durable agents on Orleans and Aspire. Its paradigm
is **neurons and synapses**: neurons are durable Orleans-journaled agents, synapses are typed facts
with full lineage, and method-scoped `TestBrain` fixtures fire real multi-silo traffic and assert on
committed journals.

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
from natural language.

## The shape of it

- **The typed interface is the surface, the synapse is the substrate, the generator is the bridge.**
- **A synapse is a fact** — a thin record, broadcast, no reply. **An interface method is a request** —
  directed at a capability, and it replies. Both are journaled; neither is privileged.
- **Modules own vocabulary** — synapse records and neuron interfaces. Compile-time, needs a rebuild.
- **Behaviors own OS logic** — an approved single-file C# program runs on behalf of an owner-scoped
  `BehaviorNeuron`.
- **Namespaces and type names are architecture** — `DigitalBrain.AI.Ollama.ILlama32` is identity, not
  a lookup result from a model descriptor.
- **Journals are the audit source.** They record causal facts only — never arguments, prompts or
  secrets. Telemetry is a projection and never replaces them.
- **Every install is a human-approved proposal**, journaled and reversible.

## Status — Built versus Designed

This section is the plan of record. Nothing below may be described as shipped unless it says Built.

**Built and proven.** The durable neuron and synapse foundation; owner-scoped client facade;
generated module activation; one-call durable AppHost composition; the public testing path; and the
typed AI, Tasks, Google, Salesforce, Flutter and Quickstart families.

**Built — Flutter first vertical.** Shell and scene vocabulary, the Ui HTTP/SSE edge, module-owned
`WithUiEdge`/`WithFlutterHost`, the headless Dart host, and Windows Material shell chrome. Full
product chrome polish, a multi-principal IdP edge, and product journal observation on `IDigitalBrain`
remain Designed — do not re-open Built Windows chrome as Designed.

**Partly built — Time.** Only the durable one-shot `ICountdown` capability and its deterministic
recovery tests. Reminder, recurring interval and calendar scheduling, DST records, and
recurrence-library selection remain Designed or open.

**Designed, not built — the Behavior install rail.** Proposal, approval, installation, execution and
rollback do not exist. `DigitalBrain.Behaviors` is a packable SDK foundation for authoring interfaces,
constrained context, manifests and revision/artifact identities; the nonpackable
`DigitalBrain.Behaviors.Runtime` contains only the canonical artifact codec. Neither is a compiler,
builder, worker, broker or execution rail. Chat today is *behaviour-shaped, not behaviour-installed*:
the program is a real `IIntentProgram` composed at build time. Pre-rail OS activation
(`DigitalBrainActivated`, pull compositions such as `ActivateDigitalBrain`) may be Built samples — it
is still not an installed Behavior and not the install rail.

**Not built — the observability spine.** No host configures OpenTelemetry, no chat client is
instrumented, and the Aspire dashboard shows no application traces or structured logs for this
product. This is the top open defect; see `CLAUDE.md` §4 for what verification can and cannot rely on
until it lands.

**Unmeasured and load-bearing:** that a model can reliably emit behaviour scripts. That benchmark sits
deliberately outside the built foundation.

## Repository shape

```text
src/       domain-neutral framework packages
modules/   independently shipped domains (AI, Tasks, Time, Google, Salesforce, Chat, Flutter)
behaviors/ OS behaviour programs composed at build time
hosts/     AppHost, silo, MCP, Ui, TestingAppHost, Quickstart hosts
clients/   Flutter shell and the Dart wire package
samples/   Quickstart greeter; AccountEnrichment; Compositions
tests/     L0 contracts; L1 suites; L2 HostTests
```

`CLAUDE.md` and this file are the only prose the repository carries. The public site lives in
[intochat/digitalbrain.docs](https://github.com/intochat/digitalbrain.docs) and publishes
**https://digitalbrain.tech**; its gates are that repository's, not this one's.

Earlier prototype generations were retired to git history rather than kept on disk. Recover any of
them with `git log --diff-filter=D --summary` and `git show <sha>^:<path>`.

## Running it

From a completely clean tree, two commands must produce a working system:

```powershell
git clean -fdx
aspire run
```

## Gate

```powershell
dotnet build DigitalBrain.slnx -c Release
dotnet test DigitalBrain.slnx -c Release --logger "console;verbosity=minimal"
```

Never `--filter` for the completion gate. Passing it is necessary, not sufficient — a green suite
proves the code holds, not that a behaviour works. `CLAUDE.md` §4 has the full verification ladder.

## Way of working

[CLAUDE.md](CLAUDE.md) is the canonical working discipline for every agent and contributor.
