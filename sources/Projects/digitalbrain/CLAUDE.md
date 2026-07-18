# CLAUDE.md

> Guidance for Claude (and other coding agents) when working in this repository.
> Read this **first** every session.
>
> **Canonical vision (latest first):**
> - `docs/v5plan/VISION.md` — **v5 (The Cut)**, the current canonical vision.
>   Supersedes v4 on conflict. v5 keeps the v4 product shape and cuts the
>   implementation by ~70%. The five v5 invariants are listed below.
> - `docs/v4/VISION.md` — v4 (Final Shaping), inherited where v5 is silent.
> - `docs/v3/VISION.md` — v3 (InoLang language freeze, spec-first runtime),
>   inherited where v4 and v5 are silent.
> - `docs/DIGITALBRAIN_RESEARCH.md` — architecture rationale for everything the
>   vision docs don't override.
>
> **The v5 cut list** (don't reinforce these — they are scheduled for deletion):
> - `MapCatalog` hand-build → replaced by lazy activation-time resolution
> - `Signal` as a distinct concept → all are synapses with a routing field
> - `.feature` + `.Steps.cs` triplet → scenarios live in the `.ino` file
> - `*.Contracts` projects → records inline next to their owning neuron
> - `DigitalBrain.Core` / `Core.Hosting` / `Core.SourceGen` / `Hosting` / `Boot` /
>   `NeuronTesting` / `ServiceDefaults` / `AppHost` → fold into one
>   `DigitalBrain.Runtime` project (see `docs/v5plan/ROADMAP.md` C4)
> - Per-neuron Flutter widget code → `rfw:` block in the `.ino`
>
> When writing **new** code, follow v5 shape (one `.ino` file, no signal
> keyword, no MapCatalog, RFW declared in Ino). When **touching existing**
> v4-era code, do not relitigate the cut — make the change v5-shaped if
> the surrounding code is on the cut list, otherwise leave it and note
> the slice for the relevant ROADMAP step (C1–C5).

## What this project is

DigitalBrain is the **substrate**: a spec-driven, AI-native runtime built on .NET
Aspire + Orleans + Microsoft.Extensions.AI + the InoLang interpreter.
**DigitalBrain** is the **operating system of new generation** installed on
DigitalBrain — the product, the brand, *digitalbrain.tech*. One running instance is
**a Brain**; a user owns N brains (one per project / persona). Behaviors live
as virtual actors called **neurons**; messages between them are **synapses**;
broadcast lifecycle/system messages are **signals**. New neurons are born from
voice or text intent through **Ino** (the personal assistant app every brain
ships with): Ino delegates to **Creator**, which authors an InoLang document,
runs its scenario red, generates the implementation, runs it green, and only
then the Runtime activates it.

## The canonical vision (read for the why)

The current canonical product vision is **`docs/v5plan/VISION.md`**
("DigitalBrain — Vision v5 (The Cut)"), with companion docs:

- `docs/v5plan/VISION.md` — the v5 invariants + the cut list
- `docs/v5plan/INO.md` — the unified language (neuron + synapse + RFW + scenario in one `.ino`)
- `docs/v5plan/DOMAINS.md` — GitHub-based domain install model, per-brain isolation
- `docs/v5plan/SDK.md` — the one place hand-written C# lives
- `docs/v5plan/ROADMAP.md` — the v4 → v5 cut sequence (C1–C5)

The v4 product-shape docs remain authoritative for anything v5 has not
yet rewritten:

- `docs/v4/VISION.md` — v4 north star, the five v4 invariants
- `docs/v4/NAMING.md` — DDD vocabulary + rename table (~90 neurons)
- `docs/v4/ARCHITECTURE.md` — multi-brain isolation mechanism
- `docs/v4/LAUNCH.md` — the `dotnet run digitalbrain.cs` story
- `docs/v4/SHELL.md` — Constellation / Brain Scene / Tools Dock / Ino / Creator
- `docs/v4/RFW.md` — RFW surface contract; Idle / Busy / Modal lock states
- `docs/v4/ROADMAP.md` — the v3 → v4 epics (E-* still load-bearing where v5 hasn't replaced)

### The five v5 invariants (don't relitigate)

1. **V5-1 One file per behavior.** A neuron is **one `.ino` file**
   containing contracts, handlers, RFW surface, and scenarios. No more
   `.cs` + `.feature` + `.Steps.cs` triplet. Hand-written `.cs` only
   exists in `DigitalBrain.Runtime`, `DigitalBrain.Kernel`, and `DigitalBrain.SDK`.
2. **V5-2 One message type.** `Signal` is deleted as a distinct concept;
   it's a synapse with broadcast routing. One envelope, two routing modes
   (`emit` = broadcast, `ask` = point-to-point).
3. **V5-3 No global catalog.** `MapCatalog` is gone. Ports resolve at
   activation; unresolved references emit `Neuron.UnresolvedReference`
   and park in modal lock.
4. **V5-4 UI is data.** Every neuron's RFW surface is declared in its
   `.ino` `rfw:` block. The Flutter shell is a generic RFW renderer with
   no neuron-specific Dart code.
5. **V5-5 Domains are repos.** A domain is a public GitHub repo of
   `.ino` files. `digitalbrain install <owner>/<repo>` clones into
   `%LocalAppData%\DigitalBrain\brains\{brainId}\domains\`. No central
   registry; discovery via GitHub topic `digitalbrain-domain`.

### The v4 invariants (inherited unchanged)

1. **V4-1** One process, one Aspire composition, one launch command —
   `dotnet run digitalbrain.cs`.
2. **V4-2** The **Constellation** is the only top-level screen.
3. **V4-3** One brain = one isolated runtime context — disjoint grain
   namespaces / storage paths / OAuth namespaces / install lists, prefixed
   by `BrainId`.
4. **V4-4** Every neuron has an RFW surface — default if not declared.
5. **V4-5** The shell respects neuron-declared RFW lock states (`Idle` /
   `Busy` / `Modal`).

### The v3 decisions v4 inherits unchanged

- **L1** InoLang is the universal authoring language.
- **L2** `DigitalBrain.Core` = the language spec + wire schema (public ABI),
  frozen at E-ABI.
- **L3** C# stays as the Engineering language under `DigitalBrain.SDK`.
- **L4** Native C# only for Kernel + Boot + Brain shell + SDK connectors.
- **L5** Determinism is in the language; non-determinism is a declared seam.
- **L6** Spec-first is a **runtime invariant** (no green ⇒ no activation).
- **L7** Signals are broadcast Synapses; v4 adds the `RfwLock` envelope as a
  Signal subtype.
- **D-A** The permanent two-layer naming split — **DigitalBrain** = substrate,
  **DigitalBrain** = product domain. **No mass-rename, ever**; the repo,
  branch, and this file keeping *DigitalBrain* for the substrate is the design,
  not a pending migration.

> **The principles, build/test commands, and "what you must not do" below
> govern the codebase as it exists today** and stay authoritative until the
> roadmap explicitly retires them. Where v4 introduces a new convention
> (e.g., the `rfw` block, the BrainId key prefix, the new naming rules), it
> applies to **new** code; the migration of existing code is scheduled in
> the v4 ROADMAP epics (E-MULTIBRAIN, E-NAMES, E-SHELL, E-SURFACE). **Do not
> mass-rename ahead of E-NAMES, and do not swap the Reqnroll /
> `.feature`+`.cs` triplet for InoLang ahead of E-INO completing for that
> slice.** InoLang exists in-tree (`inolang/`, plus the
> `examples/inolang-orleans-proto` prototype); treat it as **in progress
> toward E-INO**, not as the current default authoring path.

## The non-negotiable principles

When writing code, you must respect these. They're settled — don't relitigate.

1. **Spec first.** Every neuron has a `.feature` file co-located with its `.cs`.
   No neuron without a green test. The `.feature` is the source of truth for
   behavior, dependencies (`@requires:` tags), and telemetry
   (`@telemetry:counter:...` tags).
2. **AI-native, not bolted on.** LLMs and Whisper are first-class neurons in
   `DigitalBrain.Domains.Ai`. They wrap `IChatClient` / `ISpeechToTextClient`; they
   are not parallel hierarchies.
3. **Aspire is the only composition root.** Everything wires up through
   `builder.AddDigitalBrain(...)` in `DigitalBrain.AppHost/Program.cs`. Domain
   `Program.cs` files are one line: `builder.AddDigitalBrainDomain(); Run();`.
4. **Orleans for state.** Neurons are virtual actors. Don't roll your own
   lifecycle. Let `[GrainType]` and the registry handle activation.
5. **Microsoft.Extensions.AI for portability.** Never depend on a specific
   provider SDK in domain code. Always go through `IChatClient`,
   `IEmbeddingGenerator<,>`, or `ISpeechToTextClient`.
6. **Reqnroll, not custom DSL.** Standard Gherkin parsed by upstream
   `Gherkin` 39.x and Reqnroll 3.3+. Tags carry our own metadata.
7. **One test command.** `dotnet test`. There is no `flutter test`. UI
   assertions are made on RFW payloads over gRPC in
   `DigitalBrain.E2E.Tests`.
8. **L4 carve-out — write C# only where the platform requires it.** v3 §L4 /
   §3: hand-written C# is reserved for `DigitalBrain.Kernel` (Interpreter +
   Runtime), `DigitalBrain.Boot` + `interpreter.cs` (the bootstrap floor),
   `DigitalBrain.Brain` (the OS shell), and **`DigitalBrain.SDK`** (consolidated
   SDK and its contracts) native connector / perf neurons. Every other behavior
   is authored as InoLang (`.ino`) by the Creator, gated red->green at the L6
   invariant. A new C# neuron should be either a Kernel/Boot/Brain-shell concern
   or a `DigitalBrain.SDK` connector component — if it is neither, the Creator
   should author it in `.ino` instead. (The `DigitalBrain.Domains.*` triplets that
   predate v3 stay where they are — v3 §Naming permanently keeps the two-layer
   split, D-A.)

## Repository layout (cheat sheet)

```
kernel/                          # platform host, core engine, bootstrapper, and dynamic domain
├── DigitalBrain.AppHost/             # the manifest — domains and providers
├── DigitalBrain.ServiceDefaults/     # AddServiceDefaults / AddDigitalBrainDomain / AddDigitalBrainClient
├── DigitalBrain.Core/                # Synapse, Neuron, IHandle, INeuronMetadata, capability markers — no Orleans runtime
├── DigitalBrain.Core.Hosting/        # Orleans grains, Roslyn compiler, registry
├── DigitalBrain.Core.Tests/
├── DigitalBrain.Kernel/              # Creator, Cortex, Gateway, BrainWatch
├── DigitalBrain.Kernel.Contracts/
├── DigitalBrain.Kernel.Tests/
├── DigitalBrain.Boot/                # L4 bootstrap floor
├── DigitalBrain.Boot.Tests/
├── DigitalBrain.NeuronTesting/       # in-process test harness for neurons
└── DigitalBrain.Domains.Dynamic/     # runtime-generated dynamic neurons
sdk/                             # native C# connectors & SDKs (DigitalBrain.SDK)
├── DigitalBrain.SDK/            # Unified SDK C# assembly containing core connectors
│   ├── Ai/                      # Microsoft.Extensions.AI abstraction integration
│   ├── Aspire/                  # Aspire system integration
│   ├── Canvas/                  # Canvas 3D rendering components
│   ├── Google/                  # OAuth & Google APIs integration
│   ├── INO/                     # InoLang integration floor
│   ├── Identity/                # Identity management
│   ├── Sqlite/                  # Local database contexts & SQLite factory
│   ├── Stripe/                  # Stripe billing & payment webhook processing
│   ├── Telegram/                # Telegram alerting client
│   ├── Visuals/                 # Visual overrides catalog neuron
│   ├── Windows/                 # OS Seam integration (Process.Start, metadata)
│   └── XAI/                     # XAI integrations (including Grok/)
├── DigitalBrain.SDK.Contracts/  # Synapse, neuron and signal record contracts for all sub-domains
├── DigitalBrain.SDK.Mcp/        # Model Context Protocol integration
└── digital_brain_sdk_flutter/   # Flutter client-side Dart SDK interfaces
inolang/                         # InoLang parser, interpreter, and tests
├── DigitalBrain.InoLang/        # plain-English neuron compiler
└── DigitalBrain.InoLang.Tests/
UI/                              # user interface & E2E tests
├── flutter/                     # Flutter client shell (Home, Live tabs)
└── DigitalBrain.E2E.Tests/           # Playwright & gRPC E2E tests
samples/                         # reference domains (Travel, Engineering, Onboarding)
```

Domains in scope: `ai`, `google`, `data`, `travel`, `dynamic`.

## Code Intelligence & MCP Tools — Always use context7, aspire, codegraph, and digitalbrain-mcp

This repository leverages Model Context Protocol (MCP) servers and code intelligence plugins. Agents MUST prioritize and always use these specialized tools:

- **Always use `codegraph` tools** (`codegraph_search`, `codegraph_context`, `codegraph_trace`, `codegraph_node`, etc.) for any workspace navigation, symbol lookups, call graph analysis, and dependency/impact checking. Indexing is initialized under the `e:\digitalbrain` project path. Using `codegraph` is far faster and more accurate than manual grepping or raw reading.
- **Always use `context7` tools** (`resolve-library-id`, `query-docs`) to query third-party library documentation (like Next.js, React, Flutter, etc.) and retrieve dynamic code samples or vector context details.
- **Always use `aspire` tools** (`mcp__aspire__list_resources`, `mcp__aspire__execute_resource_command`, etc.) to orchestrate local distributed AppHosts and control Orleans domain silos or Flutter resources.
- **Always use `digitalbrain-mcp` tools** to query runtime catalog states, introspect active neurons, check AI cluster health, and handle dynamic synapses.

### Traditional LSP Tool fallback

This repo also ships an in-repo Claude Code plugin (`tools/claude-lsp/`, installed at project scope) that activates the built-in **`LSP` tool** for C# (`csharp-ls` via the pinned local dotnet tool) and Dart (`dart language-server`):

- **Navigate with LSP when codegraph is silent.** Go-to-definition, find-references, hover / type info, implementations, call hierarchy — use the `LSP` tool for compiler-level AST precision across `DigitalBrain.slnx`.
- **Trust post-edit diagnostics.** After an Edit/Write the language server reports errors and warnings automatically — fix what it flags in the same turn.
- **First C# call is slow.** `csharp-ls` loads all of `DigitalBrain.slnx` before it answers; wait and retry if needed.
- **No `LSP` tool in your session?** It is plugin-gated: run `/reload-plugins` or restart Claude Code.

## Hard rules when touching code

### Project references

- A domain implementation project (`DigitalBrain.Domains.X`) **must not**
  reference another domain implementation project. Reference its
  `.Contracts` instead.
- The kernel may reference any `.Contracts`. Domains may reference
  `DigitalBrain.Kernel.Contracts`. Never the other way around for the silo
  projects themselves.
- The Flutter client only knows about the gateway proto in
  `DigitalBrain.Kernel.Contracts/Protos`. It never references domain projects
  directly.

### Neuron files

Every neuron is a triplet: `Foo.cs`, `Foo.feature`, `Foo.Steps.cs`, in the
same folder. If you create a new neuron, create all three. Never split them
across projects.

### Synapse contracts

Every public synapse type lives in a `.Contracts` project. The record's name
becomes the synapse type name (`AiSynapseTypes.LlmRequest = "ai.LlmRequest"`).
Add the constant to the domain's `<Name>SynapseTypes.cs`.

### Telemetry

Don't sprinkle `Meter` calls in neurons. Declare counters / histograms in the
`.feature` file with `@telemetry:` tags; the Reqnroll hooks register them and
the neuron base class exposes them via `Counter("name").Increment(n)`.

### Generated neurons (Dynamic domain)

When you write code that generates code (Creator path), produce:

- A `.feature` file validated by `GherkinValidator.Validate(text)`.
- A `.Steps.cs` file validated by `RoslynCompiler.ParseAndDiagnose(text)`.
- An implementation file validated the same way.
- A `manifest.json` with `id`, `icon`, `capabilities`, `requires`,
  `createdAtUtc`, `creator-llm-model`.

Persist all four under
`kernel/DigitalBrain.Domains.Dynamic/Generated/<neuron-id>/`. Mark
that subtree `.gitignored` and rely on grain storage for cross-restart
persistence.

### OAuth (Google)

The auth broker (`GoogleAuthBroker`) is **internal infrastructure**, not a
neuron. Each Google neuron asks the broker for the *minimum* scope it needs.
Tokens persist via `DigitalBrainGrainDataStore`, which encrypts at rest using
DPAPI / libsecret / Keychain. **Never** write tokens to plain files in the
user profile (the default `FileDataStore` location).

### SQLite

`SqliteNeuron` is parameterised by a `databaseId` slug. Don't share connection
state across neurons. The `IDatabaseContextFactory` resolves a per-id path
under `%LocalAppData%\DigitalBrain\databases\` and runs the schema declared in the
calling neuron's `.feature` `Background`.

## When the user asks for a feature

1. **Identify the domain.** Is this an LLM thing (`ai`)? A Google thing? A
   storage thing? Cross-cutting? If cross-cutting, it's probably a neuron in
   `dynamic` that orchestrates existing ones.
2. **Find or write the contracts.** The synapse record lives in
   `DigitalBrain.Domains.<Name>.Contracts`. If it doesn't exist, add it and add
   the type-name constant.
3. **Write the `.feature` first.** Then the step definitions. Then the
   implementation. Run the test. It should fail. Then make it pass.
4. **Add the icon if it's a new neuron.** `clients/flutter/assets/icons/`.
   The neuron class's `static abstract string Icon` (from `INeuronMetadata`)
   references the asset key.
5. **Wire it in AppHost only if it's a domain-level concern.** Within a
   domain, neuron registration is automatic via assembly scanning of
   `IHandle<TSynapse>` declarations by `NeuronCatalogScanner`.

## When changing tests

- Run all tests without stage filters. The standard command is `dotnet test` from the root.
- For Google integration tests, look for `GOOGLE_TEST_CREDENTIALS` in environment; skip with a clear message if missing.
- Never use `flutter test`. Period. UI tests live in
  `DigitalBrain.E2E.Tests` and assert on the RFW payload contract.

## Style

- C#: file-scoped namespaces, `record` for value types, `sealed` everywhere,
  primary constructors when they help.
- Async: always `CancellationToken`-pass-through.
- Naming: `<Verb><Noun>Request` / `<Verb><Noun>Response` for synapse pairs.
- Error handling: emit a synapse, don't throw across the cortex. Internal
  invariants are still `throw`s.
- Logging: `ILogger<T>` injected; structured logging via message templates
  (`{Variable}` placeholders). No string concatenation in log calls.

## Build commands (PowerShell, since the repo is on Windows)

```pwsh
# Restore + build everything
dotnet build

# Run the full Aspire app — ALWAYS via `aspire start` / `aspire stop`
aspire start                                # boots the AppHost
aspire stop                                 # cleanly stops it + child resources
# NEVER `dotnet run --project src/DigitalBrain.AppHost` directly: it produces
# orphan AppHost processes and orphan `orleans-redis-*` containers that
# the next session has to clean up. Same rule for `tasklist` / `taskkill` /
# `Get-Process` / `Stop-Process` — these are forbidden. Use Aspire MCP tools
# (`mcp__aspire__list_apphosts`, `mcp__aspire__list_resources`,
# `mcp__aspire__execute_resource_command`) for inspection and resource
# lifecycle.

# Tests
dotnet test                                # run all tests from the root

# Generate Flutter gRPC stubs (run after editing .proto files)
dart run build_runner build --delete-conflicting-outputs   # in clients/flutter
```

## Applying & verifying changes in the running app (agents)

The AppHost is normally already running. Don't `aspire stop`/`start` to pick
up a code change unless you changed `DigitalBrain.AppHost/Program.cs` itself (that
requires a full `aspire stop` then `aspire start` — the only case). Otherwise:

- **To apply a source change to a running resource**, call
  `mcp__aspire__execute_resource_command` with the **`rebuild`** command on
  that resource (`rebuild` = stop → recompile from source → restart;
  `restart` does *not* recompile). Note `rebuild` on the `kernel` fails with
  MSB3021 DLL-lock errors while the domain silos run (it rebuilds their
  outputs which the running silos hold) — for kernel/AppHost source changes
  use the `aspire stop` → `aspire start` cycle instead.
- **The kernel no longer serves Flutter.** It is a pure gRPC/Orleans host
  (`StaticWebAssetsEnabled=false`, no `wwwroot`, `GET / → 404`). The Flutter
  client runs as **three standalone Aspire resources** (all decoupled from
  the kernel, all injected with `--dart-define=KERNEL_ENDPOINT`; gRPC-Web is
  cross-origin, allowed by the kernel CORS policy `flutter-web` applied
  per-gRPC-endpoint via `RequireCors`, origins `http://localhost:5800` &
  `:5801`):
  - **`flutter-web`** — `flutter run -d web-server --release` on
    `http://localhost:5800`. **Autostart.** A *release* build (no DDC/DWDS)
    so it renders in any plain browser and is Playwright-inspectable. Dev
    loop: `rebuild` the `flutter-web` resource. **No Dart-MCP widget tree**
    (browsers have no Dart VM — inherent). NB: a *debug* `-d web-server`
    build paints blank (its DWDS client needs the Chrome extension) — that
    mistake is why this is `--release`.
  - **`flutter-chrome`** — `flutter run -d chrome` on
    `http://localhost:5801`. **Explicit-start** (pops Chrome). Renders, and
    its console log prints a **Flutter DevTools** URL
    (`http://127.0.0.1:5811/...devtools...`) a human can open. On the
    current Flutter toolchain it does **not** emit a Dart-MCP DTD (only a
    VM Service over DWDS), so the Dart MCP cannot drive it — human DevTools
    only.
  - **`flutter-windows`** — `flutter run -d windows --print-dtd
    --vm-service-port=5821`. **Explicit-start** (pops a desktop window).
    The real Dart VM ⇒ **this is the only resource the Dart MCP can attach
    to** for `get_widget_tree` / `hot_reload`.
- **Dart-MCP widget-tree recipe (flutter-windows only):** Aspire MCP
  `start` the `flutter-windows-…` resource → wait for the debug build →
  read its **on-disk DCP stdout** for the DTD line (Aspire's
  `list_console_logs` ring buffer truncates and often misses it):
  `ls -t "$TEMP"/aspire-dcp*/flutter-windows*_out_* | head -1` then grep for
  `Dart Tooling Daemon is available at: ws://…` → that `ws://` URI into
  `mcp__dart__connect_dart_tooling_daemon` → `get_widget_tree` /
  `get_runtime_errors` / `hot_reload`. (`flutter-windows` prints both a
  `Dart VM Service` and a `Dart Tooling Daemon` line; web targets print
  only the VM Service.)
- **Inspect the web UI** with the Playwright MCP at `http://localhost:5800`
  (or `:5801`) — discover via `mcp__aspire__list_resources`, don't hard-code
  if the suffix changed. Flutter web's a11y snapshot is just an "Enable
  accessibility" button when working (canvas-rendered) — an *empty* snapshot
  means the app didn't mount.
- **Release web builds swallow exceptions and paint blank** instead of a red
  error box — check `get_runtime_errors` (via `flutter-windows`), don't guess.
- Loops are heavy (~2–4 min) and bounce the app. **Batch UI changes and
  verify once.** Don't run per tweak.
- See `.claude/skills/flutter` for the full Flutter workflow.

## When in doubt

- The canonical roadmap is the **v4 critical path** in
  `docs/v4/ROADMAP.md` (**E-MULTIBRAIN → E-LAUNCH → E-NAMES → E-SHELL →
  E-INO / E-CREATOR → E-SURFACE**), slotted into the inherited **v3 spine**
  in `docs/v3/VISION.md` §11 (**E-ABI → E-INO → E-RUN → E-SDK → E1
  Marketplace / E2 Memory / E3 TripRadar**, parallel Brain-shell **E4–E6 /
  E-IDENT / E-SET / E-BRAND**, then **E8 / E9**). The older v2-era plan in
  `docs/final_phase/` — the six milestones (**M1 STORE → … → M6 SCRIBE**),
  architecture in `01-architecture.md`, conventions in `03-conventions.md`,
  test stages in `04-test-stages.md` — is **superseded by the spine** but
  kept for that still-accurate operational detail; treat its milestone
  sequencing as historical, not the plan.
- The architecture decisions are in `docs/DIGITALBRAIN_RESEARCH.md` and (for the
  v4 layer model + multi-brain isolation) `docs/v4/ARCHITECTURE.md`. Each
  has alternatives considered + the recommended choice with rationale.
  Don't re-decide things that document already decided; if you genuinely
  think a decision is wrong, propose an amendment to that doc.
- The end-to-end PoC scenario is now the **v4 north star** in
  `docs/v4/VISION.md` §8 (the Constellation → Brain Scene → Ino + Creator
  flow). The v3 §12 / `docs/DIGITALBRAIN_RESEARCH.md` §17 / `docs/final_phase/00-vision.md`
  scenarios are inherited where v4 is silent. If your change doesn't help
  (or actively breaks) the v4 scenario, reconsider.
- Keep `Program.cs` thin. If you're adding more than a line or two to a
  domain's `Program.cs`, the logic belongs in a `ServiceDefaults`
  extension method.

## What you must not do

- Don't reference another domain's silo project from a domain silo project.
- Don't introduce a custom DSL — extend Gherkin via tags (and for new code,
  prefer InoLang per v3 L1 once E-INO covers your slice).
- Don't add a synapse type without a `.Contracts` entry and a constant.
- Don't write tokens to disk outside `DigitalBrainGrainDataStore`.
- Don't write a `flutter test`.
- Don't `throw` to cross the cortex; emit a failure synapse.
- Don't pollute `kernel/` with neurons that are domain-specific.
- Don't put runtime-generated neurons anywhere except
  `kernel/DigitalBrain.Domains.Dynamic/Generated/`. (Per-brain isolation under
  `Generated/{brainId}/` lands with E-MULTIBRAIN.)
- Don't reason over large swaths of source to locate or trace a symbol when
  the `LSP` tool can answer exactly — that's slower and less reliable.
- **v4 naming rules** (`docs/v4/NAMING.md`): when writing **new** neurons,
  pick one of the three shapes — `<Verb><Noun>Neuron`,
  `<NounPhrase>Neuron` (role), or `<Connector>Neuron` (external service).
  No `*Handler`, `*Manager`, `*Helper`, `*Util`, `*Service` suffixes; no
  `*Grain` as a public suffix (grain is a runtime concept). When writing
  **new** synapse contracts, use one suffix per role: `*Request`/`*Response`
  for RPC, `*Command` for write-side intent, `*Query`/`*Result` for reads,
  `*Event` (or past-tense) for broadcasts — and don't mix roles in one
  handler.
- **v4 RFW invariant** (`docs/v4/RFW.md`): when writing a new neuron that
  the user can land the camera on, give it an RFW surface. If you don't
  override, the Kernel renders the default surface; that's fine for
  bookkeeping neurons but explicit for connector / authored ones.
- **v4 multi-brain rule** (`docs/v4/ARCHITECTURE.md` §4): when writing new
  grain keys / storage paths / OAuth namespaces, prefix by `BrainId`. The
  retrofit lands in E-MULTIBRAIN; new code should not add un-prefixed
  keys.
