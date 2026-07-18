# ROADMAP — the v4 → v5 cut sequence

> Order matters. Each step is independently shippable, each leaves
> tests green, and each *deletes more code than it adds*. If a step
> grows the line count, redesign it.

---

## The five cuts (sequenced)

### C1 — kill `MapCatalog`  (1 day)

**Delete:**
- `MapCatalog.Empty().With(...)` chain in `digitalbrain.cs` lines 92–98.
- All boot-time port-existence validation in `InoCompiler.Compile`.

**Add:**
- Lazy `GrainRegistry.Resolve(fqn)` call at neuron activation.
- `Neuron.UnresolvedReference` synapse + default-surface fallback.

**Net:** −~50 lines in `InoCompiler`, +~15 lines in
`DigitalBrain.Runtime/GrainRegistry.cs`. Tests still green because the
existing happy-path resolves at runtime anyway; only the eager-fail
path is gone.

**Done when:** `digitalbrain.cs` no longer mentions `MapCatalog`, and
removing one `With(...)` line still boots successfully.

---

### C2 — collapse `Signal` into `Synapse`  (2 days)

**Delete:**
- The `ContractKind.Signal` enum value.
- The separate `signal` keyword in `.ino` grammar.
- The `BroadcastSignal` vs `RouteSynapse` code paths in
  `DigitalBrain.Runtime`.

**Add:**
- One `Synapse` record envelope with a `routing` field
  (`PointToPoint(target)` | `Broadcast`).
- Update `.ino` parser to treat `emit X(...)` (broadcast) and
  `ask alias to ...` (P2P) as the only two surface forms.

**Migrate:**
- Existing v4 `signal(...)` declarations rewritten to `synapse ...`
  with `emit` semantics. Mechanical sed of `signal\s+(\w+)` → `synapse $1`
  across `*.ino` + manual review of handlers.

**Net:** ~−300 lines across runtime + grammar + generated code.

**Done when:** `digitalbrain.ino` reads cleanly with one keyword
(`synapse`) and the genesis flow still passes its scenario.

---

### C3 — fold scenarios into `.ino`  (3 days)

**Delete:**
- The `.feature` + `.Steps.cs` pattern from `DigitalBrain.Domains.Dynamic`
  and any sample neurons that use it.
- Reqnroll dependency from `DigitalBrain.Runtime` and from the dynamic
  domain. (Keep it transiently in `DigitalBrain.Test` for legacy
  triplet neurons until C4 retires them.)
- `DigitalBrain.NeuronTesting` as a separate project.

**Add:**
- `scenario` block to Ino grammar (already specified in INO.md §6).
- `ScenarioRunner` class in `DigitalBrain.Runtime`, replacing the Reqnroll
  hooks for `.ino` neurons. Reads scenarios from the parsed AST.
- `dotnet test` integration: the runner exposes scenarios as `xUnit`
  facts so a single `dotnet test` command runs them all.

**Migrate:**
- Genesis neuron's scenario (`digitalbrain.ino:51–56`) already works
  as the canonical example. Anything else in v4 that mixed `.feature`
  files gets ported case-by-case during C4.

**Net:** −Reqnroll (~XL dependency tree), −one project, +~200 lines of
runner code.

**Done when:** `dotnet test` runs scenarios from `.ino` files and the
Reqnroll NuGet reference is gone from `Directory.Packages.props`.

---

### C4 — flatten the kernel projects  (4 days)

**Merge:**
- `DigitalBrain.Core` + `DigitalBrain.Core.Hosting` + `DigitalBrain.Core.SourceGen` +
  `DigitalBrain.Hosting` + `DigitalBrain.Boot` + `DigitalBrain.NeuronTesting` +
  `DigitalBrain.ServiceDefaults` → **`DigitalBrain.Runtime`**.
- `DigitalBrain.AppHost` → folds into `digitalbrain.cs` (Aspire composition
  inline; `aspire start` still works because Aspire CLI reads
  `digitalbrain.cs` as the AppHost via the v4 file-based-host
  mechanism).
- `DigitalBrain.Kernel.Contracts` → deleted; records move inline next to
  their consumers.
- `DigitalBrain.Domains.Dynamic` → folder convention under
  `brains/{brainId}/generated/`. The project is gone.

**Keep:**
- `DigitalBrain.Kernel` (Creator, Navigator, Gateway, Ino, BrainRegistry).

**Net:** kernel projects 11 → 2. Roughly −15 `.csproj` files,
−20 `using` chains across the codebase, −several thousand lines of
project-to-project plumbing.

**Done when:** `DigitalBrain.slnx` lists 5 projects total (Runtime,
Kernel, SDK, InoLang, InoLang.Tests) and `dotnet build` is green.

---

### C5 — UI as data  (5 days)

**Delete:**
- All per-neuron Flutter widget files. The brain scene's neuron-card
  carousel uses one widget; the per-neuron variation comes from the
  RFW payload.
- The `UIKit/` top-level folder. Its widgets move into
  `UI/flutter/lib/rfw_kit/` and become RFW-vocabulary-callable.

**Add:**
- `rfw:` block parser in Ino (already specified in INO.md §5).
- `InoRfwEmitter` that converts an `rfw:` block to a `BlobNode` RFW
  payload at handler execution time.
- The matching RFW widget library in `UI/flutter/lib/rfw_kit/`:
  `Column`, `Row`, `Stack`, `Heading`, `Markdown`, `Bullets`,
  `Table`, `Button`, `Field`, `Chart`, `Canvas` — one widget per
  token from INO.md §5.

**Net:** Flutter `lib/` shrinks substantially; the shell becomes a
generic RFW renderer with no neuron-specific code. New neurons get UI
for free.

**Done when:** the genesis neuron and at least one SDK neuron
(`Ai.Chat`) render their RFW surface entirely from their `.ino`'s
`rfw:` block, with no Dart code touched.

---

## Critical path & dependencies

```
C1 ─┐
    ├──► C3 ──► C4 ──► C5
C2 ─┘
```

- C1 + C2 are independent — could run in parallel.
- C3 depends on C2 (scenarios reference synapses, not signals).
- C4 depends on C3 (folding `NeuronTesting` requires scenarios to
  already live in `.ino`).
- C5 depends on C4 (Flutter shell talks to the runtime gateway whose
  shape changes during C4).

Total wall-clock estimate: **~3 weeks** of focused work, sequential.
Faster if C1 and C2 happen in parallel (and they should).

---

## What stays unchanged through v5

These are *not* cut. They're already at their minimum:

- The `dotnet run digitalbrain.cs` launch (v4-1 unchanged).
- The Aspire composition root in `digitalbrain.cs` (simplified, not
  replaced).
- Microsoft.Extensions.AI for LLM/embedding/voice (the right
  abstraction; no SDK-specific replacement needed).
- Orleans grains for neuron activation (the right primitive; v5 just
  changes how grain types are *generated*).
- gRPC between kernel and Flutter (one protocol; nothing simpler
  available).
- The Constellation → Brain Scene → Neuron Scene shell flow (v4 SHELL
  unchanged).

---

## Verification at each step

After every cut:

```pwsh
dotnet build
dotnet test --max-parallel-test-modules 1   # all 440+ green
aspire start                                # boots
dotnet run digitalbrain.cs                  # launch story still works
```

If any of the four fail, **revert and rethink the cut**. v5 is not a
ground-up rewrite; it's a deletion pass with green tests at every step.

---

## After v5

Once C1–C5 land:

- **E-INO continues** from where v4 left off — converting v4-era
  C# neurons to `.ino` form, one slice at a time. The new shape is now
  the only shape.
- **Marketplace** (v4 §10) becomes a thin wrapper over the GitHub
  topic discovery model (DOMAINS.md §5). Billing layered on later.
- **Creator** gets smarter — better prompt context, fewer LLM calls,
  cached domain-catalogs per brain.
- **Global Brain sync** layers on as a `digitalbrain/sync` domain — same
  as any other domain, no special infrastructure.

Everything after v5 is additive. The hard cuts happen now.
