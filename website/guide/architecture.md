# Architecture

The implemented architecture is one universal execution path with module behavior supplied by strategies.

<NeuronGraph />

## Implemented layers

| Layer | Current responsibility |
| --- | --- |
| Experiences | Flutter workspace, MCP tools, UI gateway, behavior tooling |
| Modules | Workspace, AI, Web, Connections, Google, Salesforce, Behaviors |
| Kernel | `NeuronGrain`, kind dispatch, invocation replay, journal updates, effect approval proof |
| Runtime | Orleans activation, Aspire composition, telemetry defaults |

`NeuronGrain` resolves the address kind, selects an `INeuronKind`, replays a prior receipt when the same command identifier returns, invokes domain behavior, and appends returned events.

## One address space

The current grain key is:

```text
owner|space|kind/instance
```

For example:

```text
local-owner|main|chat/main
```

`NeuronAddress.Parse` divides the key into owner, space, and neuron identifier. The text before the first slash in the neuron identifier selects the registered kind.

## Typed outside, universal inside

Module contracts implement `INeuronContract`. `NeuronProxy` translates a one-argument `Task<TResult>` method bearing `[NeuronContract]` into `INeuron.InvokeAsync`. MCP and HTTP construct the same envelope directly.

The universal envelope is therefore current kernel behavior, not merely a transport detail. Restricting it to edges is a **Target**.

## Persistence today

The kernel journals neuron events through Orleans Journaling. The local kernel host registers `VolatileJournalStorageProvider`, so a process restart loses that journal. Durable production storage is a **Target**.

## Effects today

The kernel implements:

```text
propose → approve or decline → claim approval proof
```

Google and Salesforce kinds can propose effects and require a matching approval proof before their connector path proceeds. A complete executor lifecycle with deterministic provider idempotency keys, reconciliation, and `OutcomeUnknown` handling is a **Target**.

## Trust boundaries today

MCP and UI edges inject hard-coded development caller identities. Owner checks and effect-state checks exist, but end-user authentication and a complete grant model do not. Treat authenticated identity and production authorization as **Targets**.
