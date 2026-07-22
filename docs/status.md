# Status

DigitalBrain is an unpublished alpha under a ground-up rebuild on `master`. Git history is the
archive; rejected generations are not kept as live scaffolding.

## Current completion

| Area | State |
| --- | --- |
| Durable neuron and synapse kernel | built; contract, simulation, and hosted coverage |
| Generated module catalog and activation | built |
| Typed AI contracts and model neurons | built for Ollama `Llama32` and OpenAI `Gpt56` |
| AI-owned Aspire resources and parameters | built |
| Production AppHost local Ollama selection | built |
| Typed client facade | built |
| `IAgent` and `IGroupChat` implementations | not built |
| Natural-language typed registry search | not built |
| Google, Salesforce, Flutter, Memory modules | not built |
| Script proposal, approval, install, rollback | not built |

## Gates

The root gate is:

```powershell
dotnet test --logger "console;verbosity=minimal"
```

Release verification additionally runs:

```powershell
dotnet test .\DigitalBrain.slnx -c Release
.\eng\pack.ps1
.\eng\verify-consumer.ps1
.\eng\verify-dependencies.ps1
```

The documentation gate runs:

```powershell
Set-Location website
node tools/render-specification.mjs
node --test tests/*.test.mjs
```

No packages are published to NuGet.

## Open debts

**An Orleans client is a trusted cluster peer.** Owner identity is a correctness boundary, not
authentication. Authenticate at the edge and do not publish Orleans clustering endpoints.

**Journal history is bounded.** Compaction retains a summary and recent window, not an eternal audit
log. Effectively-once processing is also windowed by the durable dedupe set.

**Delivery ordering is local.** Directed delivery is FIFO per target and at least once. There is no
cross-target ordering; one blocked receiver does not stall another.

**Broadcast addressing.** Broadcast targets handler **types** and creates correlation-derived
instances. A future identity-wide feed must account for those instances.

**Client observation is not the final timeline stream.** Testing can watch neuron journals and resume
from cursors. A durable per-owner timeline and reconnect lifecycle are not built.

**`AsClient()` needs a production credential audit.** The client projection must never inherit
silo-only storage or module secrets. Current AI resource projection is applied only to
`WithReference(brain)`, not `brain.AsClient()`.

**DevUI is not part of the current architecture.** No interactive agent UI is wired.

## Where this is going

The next useful vertical slice is an `IAgent` neuron that consumes one typed LLM contract, followed by
`IGroupChat`. After that, the framework needs the generated canonical neuron registry and a measured
natural-language resolver. The scripting assumption remains load-bearing and unmeasured until a real
model passes a predeclared benchmark.
