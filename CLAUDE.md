# DigitalBrain

DigitalBrain is a personal "alive OS": an Orleans-based brain whose every capability is a
**neuron** (a stateful, addressable grain) publishing **signals** (typed facts stamped with their
publisher). Users program it at runtime by talking to it — the system writes itself — and what
they build is shareable, forkable and verified before it reaches anyone else. IntoChat
(`src/IntoChat`) is the product host; the Flutter shell
(`src/Modules/Google/Flutter/app/shell`) is its workspace UI.

Everything is made of four words. Anything that looks like a fifth concept is a design smell.

- **Neuron** — charts, timers, buttons, twitter accounts, packages. State lives here, never in a
  process.
- **Signal** — published only by its source neuron (`Signal.Publisher` is stamped by the kernel);
  provenance is inviolable, there is no publish-as-someone-else anywhere.
- **Behavior** — a C# script (`dotnet run app.cs` in the sandbox) that subscribes and acts through
  neuron contracts over the script edge. The unit of user-programmed logic.
- **Connector** — the bridge between an edge neuron and its external system: Gmail, Twitter, and
  also the Flutter shell, which is the *human's* connector. UI is not a layer; a button is an edge
  neuron whose external system is the person, so `On<Clicked>(button)` and `On<Posted>(elon)` are
  the same mechanism.

A **brain** is itself a neuron (`IBrain`); a user has one or more. The registry and every scope are
per brain: `BrainScope.CurrentId()` reads the brain from the kernel-stamped caller, and module
endpoints mount under `BrainRoutes.Group` → `/brains/{brainId}` (id validation + membership filter
applied once). "Workspace" survives only as the Flutter window-layout vocabulary.

The full ratified standard: `docs/superpowers/specs/2026-09-29-programmable-brain-vision-design.md`.

## The self-programming model

A user app is a **package** (`IPackage`, keyed `owner/name`) with git semantics — commit, fork,
pull, proposals, publish — containing:

```
app.spec.md      the program, in free natural language; "## Scenario: <name>" headings
tests.cs         the spec's deterministic meaning: a C# file-based app driving scratch installs
behaviors/*.cs   the implementation: one script per concern (plus legacy single Source as app.cs)
```

- **Spec is the program.** The Author agent turns a request into `app.spec.md`; the Builder agent
  compiles it into `tests.cs` and behaviors (`AppDraft` in DigitalBrain.Apps, prompts in
  `Apps/Authoring/AgentPrompts.cs`). The LLM is a compiler, never a runtime interpreter: no model sits in the
  publish gate or the signal path. There is no step vocabulary or sentence grammar — **the type
  system is the grammar**: a scenario is expressible exactly when its test compiles against the
  installed contracts (`ScriptContracts` exposes the composed modules' contracts assemblies).
- **Tests are the publish gate.** `IAppVerification` runs `tests.cs` through `ITestScriptRunner`
  (default: an ordinary sandbox script). The script installs GUID-scoped scratch apps, drives them
  through real contracts (`IApp`, `IScriptedLLM`, `IGroupChat`, …), prints one
  `dbtest:pass <name>` / `dbtest:fail <name>\t<message>` line per scenario and exits non-zero on
  failure. Green requires exit 0 AND ≥1 scenario AND all passed — silence never passes. Tests play
  the connector (scripted models, real timers); never a live model in the gate. `Publish` refuses
  any revision carrying a spec or tests until its verification is green. Verification runs
  scripts for every runtime, so it needs the host's C# sandbox (composing `CSharpModule` in a
  repository declares it).
- **Subscriptions are code, durability is the brain's.** A script's `brain.On<T>(source)` registers
  a durable, grain-side subscription on its `ICSharpFile` with a buffered watermark (64/subscription,
  oldest dropped). Processes are caches: idle scripts are reaped, a signal for a reaped script
  wakes a fresh run, which re-runs from the top and resumes at the watermark. Setup code before
  loops must be cheap and idempotent; run-to-run state lives in neurons; delivery is at-least-once,
  handlers dedup through neuron state. Derived state (a window arrangement, a summary) is re-derived
  wholesale, never incrementally patched. `Arm`/`Trigger` (run-per-signal with
  `brain.Trigger<T>()`) still exists for single-trigger files.
- **Install is per brain** (`IApp`): settings plus account slots bound to the installer's own
  connected accounts — a shared email summarizer runs against each user's Gmail. Each
  `behaviors/*.cs` becomes its own `ICSharpFile`; failure isolation is per behavior. First-party
  apps ship through the same commit→verify→publish pipeline (`ShippedAppPublisher`, folders under
  `src/IntoChat/Apps/`).
- **Module contract:** a module ships contracts + signals + connector + fakes (Testing package) +
  renderables. Modules never ship step definitions or test-only methods on production interfaces.
  A module declares `IModule<TOptions>`: options are ordinary configuration — code-declared values
  compile to flat keys under `DigitalBrain:Modules:{Name}:Options:{Property}`, so the AppHost's own
  configuration (args, env, files) overrides any option by standard precedence and tests need no
  side channel; `Validate()` runs at compile and at bind; no secrets in options (credential-shaped
  names are refused at compile). Modules also ship `IntegrationDefinition`s; their registrations are deployment-scoped
  `[PlatformOnly]` neurons seeded from `DigitalBrain:Integrations:{id}:{Field}` (catalog:
  `GET /integrations`). A user's connectors are integration accounts, per brain, under
  `/brains/{id}/integrations/accounts`.

Open design fronts (do not improvise these): cross-app subscription scopes/permissions; the
renderable palette v1 / UiPart; run-token tightening.

## Working in this repo

- Layout: Kernel = the privileged ring of projects under `DigitalBrain/Kernel/`; Core
  (`DigitalBrain.Kernel`) = the neuron runtime; Platform (phase 2) = the credential ring scripts can
  never see.
- Build/tests per project (never the `.slnx` — Windows handshake bug):
  `dotnet test src/<path-to-test-project>`. After changes: build, run the relevant unit suites,
  and smoke with `aspire run` from `src/IntoChat/IntoChat.AppHost` (all resources Healthy).
  Run module E2E suites alongside their module's unit suite; compile live-gated scenarios when
  their credentials are unavailable. IntoChat E2E contains composition, shipped-content routes,
  and the live-gated product golden journeys only. The former 18-minute monolith is dissolved.
- `src/IntoChat/tests/IntoChat.Tests.Unit` covers host composition and wiring; module behavior
  belongs in the module's test project. Packaging manifests are checked as one parsed module set.
- Flutter shell: `flutter analyze` and `flutter test` from `src/Modules/Google/Flutter/app/shell`.
  Wire-shape changes in C# records must be mirrored in the Dart screens in the same change.
- Persisted grain state: append `[Id(n)]`, never renumber; use concrete arrays, not interface
  lists — the memory grain storage's JSON serializer cannot round-trip C# collection-expression
  `IReadOnlyList` wrappers, and it only bites on reactivation.
- Code style: no boilerplate `/// <summary>`; names carry the meaning; small inline comments only
  for what the code cannot say (and keep them true — stale comments are bugs). Tests are facts
  with sentence-shaped names, `TestContext.Current.CancellationToken`, real grains over mocks
  (`UnitTest.Create().WithModule<...>()`, `FakeSandbox`, `RecordingCSharpFile`, scripted LLMs).
- Grain hazards: no self-calls on non-reentrant grains (deadlock); one-way self-references
  (`AsReference<T>()`) for signal-triggered work; `ObserverManager` watches need lease renewal.
