# DigitalBrain / Kernel split — design

Status: ratified design, phase 1 approved for implementation.
Companion to `2026-09-29-programmable-brain-vision-design.md`; this spec restructures the kernel
ring and names the direction that dissolves `Libraries/DigitalBrain.LiveTables`.

## Diagnosis (why)

`DigitalBrain.LiveTables` is the largest symptom of two missing words in the system's grammar:

1. **"Queryable tabular data" has no contract.** The palette `ITable` (materialized rows), the
   LiveTables `ISupabaseTable` (query-backed view), the agent tools and any future Excel table
   each invented a private dialect for "rows you can filter, sort, page, group and aggregate".
   A missing noun in a grammar gets smuggled in as ad-hoc phrases everywhere it is needed; the
   leftovers were swept into a homeless shared library that fuses data source, view state,
   workspace window and agent tools into one hard-wired vertical slice.
2. **The durable connection has no name.** `brain.On<T>(source)` registers a grain-side,
   watermarked subscription that survives process death — a first-class concept with no word.

The fix is structural: state what DigitalBrain *means* in one dependency-free package, make the
Orleans hosting an implementation ring beneath it, and grow shared vocabulary (data, integrations)
through the ordinary module-contract mechanism instead of shared implementation libraries.

## The model

> **Neurons, connected by synapses, exchanging signals, within a brain.**

- **Neuron** — the publishing endpoint: stateful, addressable; all state lives here, never in a
  process. Synapses form *at* the neuron: `Watch(observer)` forms one, `Unwatch` severs it.
- **Observer** — the receiving endpoint (the dendrite): what a signal arrives at. When the
  observer is itself a neuron, the synapse lives in that neuron's state and survives every
  process — that, and nothing more, is a durable subscription.
- **Synapse** — the formed connection, returned by `Watch`: holding it is being connected,
  disposing it severs it, and the signals that cross it are readable from it as a stream. Push
  (an observer you bring) and pull (the stream) are the same synapse, not two mechanisms.
- **Signal** — the fact that crosses: a pure value. `Publisher` is a `NeuronId` stamped only by
  the kernel at publish. Provenance is inviolable; an identity is a value, never a capability.
- **Brain** — the space neurons live in: resolution only (`Get<T>(NeuronId)`). The brain
  resolves, it does not switch — synapse formation is local to neurons, as in biology.

Typed subscription (`brain.On<T>(source)`) is ergonomics the script SDK supplies over `Watch`,
not a concept of the model. Behavior and Connector remain patterns expressed *in* this model,
not types of it.

## Project segregation

```
DigitalBrain                   THE MODEL  — zero dependencies (netstandard-friendly), no Orleans
DigitalBrain.Kernel.Contracts  THE ABI    — the Orleans binding of the model; what every module
                                            *.Contracts assembly references
DigitalBrain.Kernel            THE KERNEL — Orleans runtime: grain base Neuron, brain/registry,
                                            synapse tables, signal pump (sole envelope stamper),
                                            script edge, storage conventions
DigitalBrain.Kernel.Sdk        TOOLBOX    — module-builder machinery (auth, secrets, integrations,
                                            vectors, data-source support); never script-visible
```

Reference rules (one direction, no exceptions):

```
DigitalBrain          ◀── everything
Kernel.Contracts      ◀── all module *.Contracts    (and Kernel, Kernel.Sdk)
Kernel                ◀── hosts, Kernel.Sdk
Kernel.Sdk            ◀── module implementations only
```

Scripts see: `DigitalBrain` + `Kernel.Contracts` + module contracts (via `ScriptContracts`).
Scripts never see: `Kernel`, `Kernel.Sdk`, `Platform`.

### DigitalBrain (pure) — contents

Implemented at `src/DigitalBrain/` — top level, above `Modules/`: the model sits above the
kernel ring, not inside it. The files are the normative definition. One role per type:

- `INeuron` — `Task<ISynapse> Watch(INeuronObserver)` / `Task Unwatch(INeuronObserver)`.
  Unwatch exists beside disposal so a connection can be severed by anyone cleaning up (an app
  uninstalling its script must not need the handle), and severing is safe mid-delivery.
- `INeuronObserver` — `Task OnSignalAsync(Signal)`. No `IGrainObserver` here.
- `ISynapse` — `IAsyncEnumerable<Signal> Signals(ct)`, `Task Completion`, `IAsyncDisposable`.
- `Signal` — record with `NeuronId Publisher` (kernel-stamped). No Orleans attributes;
  serialization is bound in the ABI via surrogates.
- `NeuronId` — `readonly record struct` over a string; identity as a value.
- `IDigitalBrain` — `T Get<T>(NeuronId) where T : class, INeuron`; resolution only.
- `README` — the role table, the four laws, the admission rule.

**Admission rule:** a member enters `DigitalBrain` only if changing it changes what DigitalBrain
*means* on any runtime. If a change only alters how Orleans hosts or delivers, it belongs in the
Kernel ring. (`INeuronObserver` passes; `IGrainObserver` and `[AlwaysInterleave]` do not.)

### DigitalBrain.Kernel.Contracts — contents

- `IKernelNeuron : INeuron, IGrainWithStringKey` with `Watch`/`[AlwaysInterleave] Unwatch` — the
  bridge module contracts inherit so Orleans proxy codegen fires.
- `IKernelNeuronObserver : INeuronObserver, IGrainObserver`.
- Serialization surrogates for the pure types.
- Everything Orleans-flavored currently in `DigitalBrain.Contracts` (names, enforcement,
  platform attributes, intent context, signal catalog types).
- **Phase 2+** kernel-module vocabulary: `Data/` and `Integrations/` (below).

### Declared debt (open, named, not smuggled)

Module contract records keep `[GenerateSerializer, Id(n), Alias]` and interfaces keep Orleans
metadata. Full Orleans independence of module contracts would need an own attribute scheme plus a
source generator — a future front, not a phase of this spec. The kernel's ABI leaks into
contracts, v1, on purpose.

Phase 1 additionally defers the `Contracts → DigitalBrain` reference itself (namespace
shadowing, see Phase 1 item 2); the migration phase's first step is Signal unification —
`Publisher` becomes `NeuronId` at its thirteen string-comparison call sites — which unlocks
that reference and the `IKernelNeuron`/`IKernelNeuronObserver` re-derivation.

## Phase 1 — the split (approved; this change)

Create the structure without migrating modules:

1. New project `src/DigitalBrain/` (`DigitalBrain.csproj`), package-shaped, no Orleans
   reference — done; contents as described above. Where the existing Orleans-attributed types
   cannot yet re-derive cleanly, the pure layer documents the concept and the type stays in the
   ABI with a `// ABI-bound, see spec` note — no type is duplicated with diverging meaning.
2. Existing `DigitalBrain.Contracts` takes the **role** of `Kernel.Contracts`, but the project
   reference to `DigitalBrain` is deferred: the pure package's `DigitalBrain` namespace is
   resolved from enclosing namespaces *before* `using DigitalBrain.Contracts;`, so a transitive
   reference would silently rebind `Signal`/`INeuron`/`INeuronObserver` in every module type
   declared under a `DigitalBrain.*` namespace — worst case rebasing a module's signal record
   onto the attribute-free pure `Signal` and breaking Orleans serialization at runtime. The
   reference edge lands together with Signal unification in the migration phase; until then the
   seam is named by `// ABI-bound` notes in `INeuron.cs`, `Signal.cs` and the Contracts csproj.
3. Project renames (`Contracts` → `Kernel.Contracts`, `Core` → `Kernel`, `Sdk` → `Kernel.Sdk`)
   are deferred to the module-migration phase; phase 1 establishes the dependency shape, not the
   final file names.
4. Tests: kernel unit suites stay green; a new fact asserts `DigitalBrain.csproj` has zero
   package/project references (purity is enforced, not hoped for).

## Phase 2 — kernel-module vocabulary (designed, not yet scheduled)

`Kernel.Contracts` gains the vocabulary two or more modules already speak (**admission rule: no
word enters until ≥2 modules need it**):

- `Data/` — `IRowSource : IKernelNeuron` (schema + `Read(RowQuery)`, `IAsyncEnumerable` pages),
  `RowQuery` (filter / sort / page / **group-by** / aggregate), `RowSchema`, `RowPage`,
  `SourceCapabilities` (a source declares what it can answer; views degrade instead of sources
  faking SQL).
- `Integrations/` — script-facing account vocabulary: `IIntegrationAccounts` (request-an-account,
  list kinds) and `AccountRef` (opaque; the secret never crosses the script edge). The
  conversational flow "user gives the assistant a connection string" routes to the integrations
  surface, never through behavior code or neuron state.

`Kernel.Sdk/Data/` gains implementation support: base classes, query-plan helpers, a generic
in-memory/stored-rows `IRowSource`.

## Phase 3 — views over sources; LiveTables dissolves (designed, not yet scheduled)

- Palette `ITable` and `IChart` become **view neurons**: a `SourceNeuronId` plus a saved
  `RowQuery` plus presentation (visible columns / chart kind). Push mode (`Replace`/`Append`)
  survives as the stored-rows source. Same pattern is open to Calendar/Map/Collection later.
- Postgres and Supabase modules each implement `IRowSource` natively (RowQuery → SQL), absorbing
  LiveTables' `Query/` engine; they share through `Kernel.Sdk/Data`, never through a library
  between two modules.
- The shell keeps one table controller and one chart controller; the `supabase`-table special
  case disappears. Agent tools become one generic `table_read`/`table_refine` pair speaking
  `RowQuery`.
- `Libraries/DigitalBrain.LiveTables` is deleted: Query → Sdk/Data + database modules; Table →
  module `IRowSource`s + view neurons; Windows → the generic window mechanism over `ITable`;
  Tools → the generic pair.

## Testing (phase 1)

- Purity fact: `DigitalBrain` has no references (asserted from the parsed csproj).
- Existing `DigitalBrain.Core.Tests.Unit` suites pass unchanged — proof the re-derivation of
  `INeuron`/`INeuronObserver` is observationally invisible to modules.
- Build the kernel projects and smoke `aspire run` from the IntoChat AppHost.

## Out of scope

Module migration and project renames (phase 1), the Orleans-free serialization generator, cross-
app subscription scopes, run-token tightening, renderable palette beyond Table/Chart.
