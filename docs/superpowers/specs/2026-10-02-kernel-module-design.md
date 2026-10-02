# DigitalBrain extraction and the Kernel module — ratified design

Status: ratified. Supersedes the `2026-10-02-digitalbrain-kernel-split-design.md` draft (the
pure/Orleans-free model and its synapse vocabulary were explored on branch `digitalbrain-v0.2`
and rejected). Companion to `2026-09-29-programmable-brain-vision-design.md`.

## What we are doing, in one paragraph

The core abstractions — neuron, signal, subscription, observer, brain — move out of the kernel
ring into a small top-level `DigitalBrain` project, unchanged and still Orleans-attributed.
Everything in today's `DigitalBrain.Contracts` that is *not* one of those words, plus the Core
runtime, Client, Sdk and Platform, becomes **Kernel: a regular module** with the same anatomy as
Gmail or Flutter (contracts + implementation + tests, composed like any module, just always on).
The kernel module's contracts gain two new reusable vocabularies, `Data/` and `Integrations/`,
which behaviors reach through `ScriptContracts` like any module contract — resolving the
original LiveTables problem ("queryable tabular data has no contract") without a homeless
shared library.

## Why

1. **"Queryable tabular data" has no contract.** The palette `ITable` (materialized rows), the
   LiveTables `ISupabaseTable` (query-backed view), the agent tools and any future Excel table
   each invented a private dialect for "rows you can filter, sort, page, group and aggregate".
   The leftovers were swept into `Libraries/DigitalBrain.LiveTables`, a homeless shared library
   fusing data source, view state, workspace window and agent tools into one hard-wired slice.
   Shared *vocabulary* must ride module contracts, not shared implementation libraries.
2. **Secrets must never cross the script edge.** The "connect my database" scenario needs a
   script-facing account vocabulary (opaque refs) so behaviors can *name* credentials the
   Platform ring holds, without ever holding them.
3. **The kernel is a privileged directory, not a module.** It should prove the module system by
   using it: the kernel's own capabilities ship through the same contract mechanism it gives
   everyone else.

## The split rule

A type lives in `DigitalBrain` **iff it is one of the words**:

> Neurons exchanging signals within a brain.

Everything else — names, enforcement, intent, catalogs, runtime, tooling, credentials — is
kernel vocabulary and lives in the Kernel module. Anything that looks like it needs a third
home is a design smell.

## Target tree

```
src/
├── DigitalBrain/                                  # THE ABSTRACTIONS — current code, lifted as-is,
│   ├── DigitalBrain.csproj                        #   references Microsoft.Orleans.Sdk; small
│   ├── INeuron.cs                                 #   Watch/Unwatch(INeuronObserver) — unchanged
│   ├── INeuronObserver.cs                         #   : IGrainObserver — unchanged
│   ├── Signal.cs                                  #   string Publisher, [GenerateSerializer] — unchanged
│   ├── ISignalSubscription.cs                     #   unchanged
│   ├── IDigitalBrain.cs                           #   Get<T> + SubscribeAsync<T> — unchanged
│   ├── IBrain.cs                                  #   the brain neuron — unchanged
│   └── README.md
│
└── Modules/
    └── DigitalBrain/
        └── Kernel/                                # A REGULAR MODULE
            │
            ├── DigitalBrain.Modules.Kernel.Contracts/   # script-visible; everything that was in
            │   │                                        # Contracts but is NOT a core abstraction:
            │   ├── DigitalBrainNames.cs, IntentContext.cs,
            │   ├── Enforcement/, PlatformOnlyAttribute.cs, PlatformAssemblyAttribute.cs,
            │   ├── Signals/                             #   the signal catalog (NeuronActivity, …)
            │   ├── Types/                               #   TypeCatalog
            │   ├── Data/                                #   NEW — the reusable data vocabulary:
            │   │   ├── IRowSource.cs                    #     : INeuron; schema + Read(RowQuery)
            │   │   ├── RowQuery.cs                      #     filter/sort/page/group-by/aggregate
            │   │   ├── RowSchema.cs, RowPage.cs
            │   │   └── SourceCapabilities.cs            #     sources declare; views degrade
            │   └── Integrations/                        #   NEW — script-facing accounts:
            │       ├── IIntegrationAccounts.cs          #     request-an-account, list kinds
            │       └── AccountRef.cs                    #     opaque ref — never the secret
            │
            ├── DigitalBrain.Modules.Kernel/             # the runtime: today's Core + Client
            │   ├── Neuron.cs                            #   grain base; sole Publisher stamper
            │   ├── Brain/  Signals/  Storage/           #   registry, LocalSignalHub,
            │   │                                        #   SignalSubscription, conventions
            │   └── ScriptEdge/                          #   ScriptContracts composition
            │
            ├── Sdk/DigitalBrain.Modules.Kernel.Sdk/     # today's Sdk, relocated:
            │   ├── Auth/ Secrets/ Integrations/ Vectors/ Http/ Webhooks/ Capacity/ Identity/
            │   └── Data/                                #   NEW: IRowSource support — in-memory
            │                                            #   stored-rows source, query helpers
            │
            ├── Platform/DigitalBrain.Modules.Kernel.Platform/   # credential ring (today's Platform)
            │
            ├── DigitalBrain.Modules.Kernel.Tests.Unit/  # today's Core.Tests.Unit
            ├── DigitalBrain.Modules.Kernel.Tests.E2E/   # today's Core.Tests.E2E
            ├── Aspire/                                  # today's Kernel.Aspire(.Hosting)
            └── Deployment/                              # today's Deployment(+Tests)
```

## Reference arrows (one direction, no exceptions)

```
DigitalBrain                 ◀── everything
Kernel.Contracts             ◀── module *.Contracts needing names/enforcement/Data/Integrations
Kernel (runtime)             ◀── hosts, Sdk
Sdk                          ◀── module implementations only
Platform                     ◀── kernel ring internals only

scripts see:  DigitalBrain + Kernel.Contracts + module contracts (via ScriptContracts)
scripts never see:  Kernel runtime, Sdk, Platform
```

Behaviors reuse `Data/` and `Integrations/` for free: the kernel module's contracts ride
`ScriptContracts` exactly like Flutter's or Gmail's.

## Decisions that keep this cheap

- **Namespaces do not change.** The lifted project is *named* `DigitalBrain` but its types keep
  `namespace DigitalBrain.Contracts`; kernel-module contracts keep theirs too. The extraction
  is a `ProjectReference` retarget, not a source churn, and no new namespace means no name
  shadowing anywhere. A namespace sweep is a separate, optional, later decision.
- **Orleans stays.** `DigitalBrain` references `Microsoft.Orleans.Sdk`; the abstractions keep
  their attributes. Orleans independence is explicitly a non-goal.
- **Kernel module is always composed.** Every repository composes it; its contracts are always
  in `ScriptContracts`; persisted-state `[Id(n)]` discipline applies doubly.

## The Data vocabulary (what finally answers LiveTables)

- `IRowSource : INeuron` — schema plus one read shape. A Postgres table, a Supabase table, an
  Excel sheet, a behavior-fed stored-rows table are all the same contract.
- `RowQuery` — filter / sort / page / **group-by** / aggregate. One small query algebra; a pie
  chart is `group by country, count(*)` over the same contract a table scrolls.
- `SourceCapabilities` — a source declares what it can answer; views degrade gracefully instead
  of every source faking SQL.
- Implementations live where querying natively lives: Postgres/Supabase modules compile
  `RowQuery` to SQL (absorbing LiveTables' `Query/` engine), a Files module evaluates in
  memory, `Sdk/Data` ships the generic stored-rows source. Palette `ITable`/`IChart` become
  view neurons: a source `NeuronId` + a saved `RowQuery` + presentation. Then
  `Libraries/DigitalBrain.LiveTables` dissolves: Query → modules + Sdk/Data; Table → module
  sources + view neurons; Windows → the generic window mechanism; Tools → one generic
  `table_read`/`table_refine` pair speaking `RowQuery`.

## The Integrations vocabulary (what answers "connect my database")

- `IIntegrationAccounts` — request-an-account, list kinds. The assistant or a behavior asks for
  a connection of a kind; the shell renders the integration's form; the module stores the
  credential in the Platform ring.
- `AccountRef` — the opaque result a behavior holds and binds sources with
  (`brain.Get<IRowSource>(…@account)`). The secret never crosses the script edge; installing a
  stranger's app can never exfiltrate credentials, which is what keeps apps shareable.

## Migration order

1. **Extract `DigitalBrain`.** New `src/DigitalBrain` project; move the word-types out of
   `DigitalBrain.Contracts`; `Contracts` references `DigitalBrain`; nothing else changes.
   Every suite stays green — the extraction is observationally invisible.
2. **Carve the Kernel module.** Rename/move `Contracts` (what remains of it) →
   `Modules/DigitalBrain/Kernel/DigitalBrain.Modules.Kernel.Contracts`, `Core` + `Client` →
   `DigitalBrain.Modules.Kernel`, tests alongside. Module csprojs retarget their references.
3. **Relocate Sdk and Platform** under the kernel module (`Sdk/`, `Platform/` folders),
   unchanged content, retargeted references.
4. **Introduce `Data/`** in kernel contracts + `Sdk/Data` support + first two implementations
   (Postgres, Supabase), then migrate palette `ITable`/`IChart` to view neurons and dissolve
   `Libraries/DigitalBrain.LiveTables`.
5. **Introduce `Integrations/`** in kernel contracts, routed to the existing integration
   accounts surface (`/brains/{id}/integrations/accounts`), Platform-backed.

Each step ships green on its own: build per project, kernel + touched module unit suites, and
an `aspire run` smoke from the IntoChat AppHost (all resources Healthy).

## Admission rules (guard rails)

- A type enters `DigitalBrain` only if it is one of the words. The package stays countable on
  two hands.
- A vocabulary enters `Kernel.Contracts` only when two or more modules already speak it
  (`Data`: Postgres, Supabase, Files, palette views. `Integrations`: every connector).
  One-module vocabulary stays in that module.
- No shared implementation libraries between modules. Modules share through `Sdk` or through
  kernel contracts — `Libraries/` ends empty and is deleted.

## Out of scope

Orleans-free abstractions, namespace renames, cross-app subscription scopes, run-token
tightening, renderable palette beyond Table/Chart.
