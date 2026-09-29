# The Programmable Brain — vision and standard

Ratified 2026-09-29 in conversation with the owner. This document sets the one standard for how
users program DigitalBrain at runtime, how the system writes itself, and how what they build is
shared. It supersedes the step-vocabulary direction of the specs prototype and closes the question
of "many ways to build apps" — there is one way, and first-party apps use it too.

## The idea

A person talks to the brain: *"when elonmusk posts something about crypto, check the bitcoin price
and add a point to a chart."* The system writes itself: it turns the request into a package the
person can read, edit, run, fork and publish. Someone else installs the published app against their
own accounts, changes one sentence, and republishes. First-party apps — the assistant included —
are built and shipped on the same engine, so users can fork those as well.

Everything in the standard is made of four words:

- **Neuron** — a stateful, addressable unit. Charts, timers, buttons, twitter accounts, packages.
- **Signal** — a typed fact a neuron publishes. `Posted`, `TimerTick`, `Clicked`. Signals carry
  provenance: a signal comes from its source neuron and from nowhere else.
- **Behavior** — a C# script that subscribes to signals and acts through neuron contracts. The unit
  of user-programmed logic.
- **Connector** — the bridge between an edge neuron and the external system it mirrors: the Gmail
  connector, the Twitter poller, and — this was the last unification — the Flutter shell, which is
  the connector for the human.

There is no separate UI layer, no separate automation layer, no separate testing DSL. Anything that
looks like a fifth concept is a design smell.

## The package: spec is the program, the LLM is the compiler

A user app is a package (`owner/name`, the existing `IPackage` with commit/fork/pull/propose/
accept/publish) containing:

```
app.spec.md          the program, in free natural language; scenario headings for structure
tests.cs             the spec's deterministic meaning: xUnit facts, one per scenario
behaviors/*.cs       the implementation: one script per concern, each one On<> loop
```

The fundamental decision: natural language and deterministic execution are incompatible in a single
artifact, so we do not make the spec executable. The **Author** agent turns conversation into
`app.spec.md`; the **Builder** agent compiles it — once, at build time — into `tests.cs` and the
behaviors. The LLM is a compiler, never a runtime interpreter: no model sits inside the publish
gate or the signal path. Imprecise language ("also watch those two") is resolved where the context
lives — in the conversation, at authoring time.

There is no step vocabulary, no Gherkin binding, no sentence metadata on signals. **The type system
is the grammar**: a scenario is expressible exactly when a test compiles against the installed
contracts, and the Author/Builder are prompted with the contract surface (the `ScriptContracts`
set) instead of a sentence list. Modules extend the language by shipping contracts and signals —
which they already do.

Editing is conversational: revising the spec re-runs the Builder until the tests pass again.
Common variations (which account to watch, keywords, thresholds) are extracted into manifest
settings by the Author, so most "forks" are just installs with different settings; forking is for
logic changes.

## Execution: durable subscriptions, processes as cache

Behaviors run in the C# sandbox exactly as `ICSharpFile` runs scripts today (`dotnet run app.cs`
against the brain client, `ScriptEdge` tokens, contract allowlist). The reactive standard:

- Subscriptions are **code**: `await foreach (var post in brain.On<Posted>(elon, ct))`. Topology is
  never declared outside the program — no install-time trigger manifests. A behavior can compute
  what it subscribes to.
- `On<T>(source)` registers a **durable, grain-side subscription** (the existing watch machinery of
  `CSharpFileNeuron`, initiated by the script instead of by `Arm`) with a watermark. The
  subscription survives the process, the sandbox and the silo.
- The **process is a cache** of the running program. Idle scripts are reaped (IdleShutdown); a
  signal for a reaped script wakes it; the script re-runs from the top, `On<>` resumes from the
  watermark and immediately yields the pending signal. Nothing is missed while no process runs.
  Hot mode is the same code kept warm (e.g. while a window it serves is open).
- Restart-from-top is the contract: setup code before loops must be cheap and idempotent (proxies,
  reads, wholesale re-derivation). Delivery is at-least-once; handlers dedup through neuron state.
  The Builder writes generated behaviors in this style.
- Run-to-run state lives in neurons, never in process memory.
- Failure isolation is per behavior file: a crashing behavior disarms itself after
  `MaximumFailures`; the rest of the app keeps running.

An app with complex logic is many small behaviors, not one large script. The agent extends an app
by adding a file. Cross-behavior coupling is forced through neurons.

## Verification: an ephemeral brain and our own testing framework

The main source of complexity in every earlier verification design was running tests inside the
shared production brain — scratch prefixes, injection doors, permission guards and cleanup were all
hand-built isolation. The standard removes the cause: **verification runs in an ephemeral brain**,
the same in-process `UnitBrain` the platform's own tests use.

- `tests.cs` is an ordinary xUnit facts file on `DigitalBrain.Testing`: boot a brain with the
  app's modules, install the package (`WithApp(...)`, the one new extension), drive it, assert
  with `Eventually`. One `[Fact]` per scenario heading; fact results map to per-scenario verdicts
  on the marketplace spec page. The platform's own test suite is the Builder's style corpus.
- **The test plays the connector.** Internal neurons (timers, charts) are driven through their
  real contracts — `timer.Start(TimeSpan.Zero)` produces a genuine tick. Edge neurons are fed
  through the same ingest surface their connector uses, so mirror state and signal stay
  consistent. Model calls hit the scripted provider. The human's connector is played the same
  way: a test delivers `Clicked` exactly as the shell would.
- Scratch installs bind no accounts (already true in `AppVerificationNeuron`), so verification can
  never reach an external service; determinism is constructional, not policed.
- A module ships its fakes in its Testing package — the `FakeSandbox` / scripted-model pattern,
  made an official clause of the module contract.
- `AppVerificationNeuron` keeps its role and its publish gate; its body becomes "run the facts,
  read the results". Green scenarios remain the only path to publish, including for the shipped
  first-party apps (`ShippedAppPublisher`).

Rejected: Reqnroll and any Gherkin binder (the sentence grammar they bind is the layer we
deleted; the repo already removed its BDD lane once); LLM-judged steps inside the gate (a judge
may appear only as an explicit call inside a test, e.g. against the scripted provider).

## There is no UI layer

A button is an edge neuron whose external system is the person; the shell is that person's
connector. `On<Clicked>(button)` and `On<Posted>(elon)` are the same mechanism, so UI has no
special verbs, files, lifecycle or test story:

- Creating UI is creating neurons: configure a button, a chart, a window-arrangement neuron the
  same way you start a timer. No `Compose` API, no render behavior category.
- Rendering is projection: the shell draws the neurons whose contracts it recognizes, from their
  state, reactively. Windows render with zero app processes running and survive restarts because
  they were never anything but neuron state.
- Derived state — a window arrangement, a summary, an aggregate — is re-derived wholesale from
  source state in an idempotent behavior, never incrementally patched. This is generated-code
  style (a Builder rule), not platform machinery, and it applies to all derived state, not just
  what happens to be visible.
- The palette is the projection's vocabulary and its trust boundary: apps compose recognized
  contracts and cannot draw arbitrary pixels, which is why a forked stranger's app cannot imitate
  the shell. New renderables ship with modules.
- UI neurons are ordinary signal sources: any behavior (within scope) may subscribe to another
  app's `Clicked` or `PointHovered`. Composition across apps is the point, and provenance still
  holds — you can listen to anything you can see, but you can only publish as yourself.

The module contract, complete: **a module ships contracts + signals + connector + fakes +
renderables.** Nothing else feeds the language, the tests or the screen.

## Sharing

Already built and kept as-is: packages with git semantics (fork, pull, proposals as PRs,
publish), the publish gate on green scenarios, per-workspace install with settings, and account
slots binding a package's declared needs to the installer's own connected accounts — a shared
email summarizer runs against each user's own Gmail. First-party apps flow through the same
commit/verify/publish pipeline at startup.

## What this consolidates

Build (small, mostly generalization):

- Script-initiated durable subscriptions with watermarks behind `brain.On<T>(source)`; wake-on-
  signal for reaped scripts; hot/cold policy.
- Multiple behavior files per app (`AppSnapshot.CSharpFile` becomes a list; package `Files`
  already carries them).
- `UnitTest.WithApp(...)`; verification rewired to run generated facts in the sandbox; per-fact
  verdict mapping.
- Ingest surfaces as the standard connector→edge-neuron path where they aren't grain-callable yet;
  module Testing packages as the official fakes home.
- Renderable contracts reachable from scripts (button/chart/window as neurons), continuing the
  UiPart direction.
- Author/Builder prompt changes: contract surface instead of vocabulary; settings extraction;
  restart-from-top and re-derivation style rules.

Delete or demote:

- The step vocabulary path: `StepLibrary` bindings, `IFeature` vocabulary/bound-step machinery,
  the Author's unbound-step retry loop, `AppSpecSteps`. (The `Specs` module survives only where
  the platform itself still wants it, until verification is rewired.)
- Never build: `ISignalOutlet`, `Simulate` methods on contracts, `[Sentence]` attributes,
  install-time trigger manifests, any UI markup format.
- The raw workspace C# file manager stops being a user-facing way to "build apps" — it remains
  the power-user/debug surface over the same `ICSharpFile` substrate.
- `IApplication`/`IAppBuilder` narrows to host/module composition; the app-shaped parts of
  first-party apps (UI arrangement, behaviors) migrate onto the standard over time.

## Decisions log

1. **Spec is the program** — users read/edit/fork the natural-language spec; implementation is a
   build artifact. (Settings absorb the most common variations.)
2. **Subscriptions are code, durability is the brain's** — no install-time triggers; `On<>`
   registers grain-side; processes are ephemeral caches; restart-from-top.
3. **The LLM compiles, it never interprets at runtime** — determinism where the gate is,
   flexibility where the conversation is.
4. **No third language** — no step vocabulary, no sentence metadata, no Gherkin binder; the type
   system is the grammar; tests are ordinary facts.
5. **Verification is constructionally isolated** — ephemeral brain, tests play the connectors, no
   accounts bound, exit code gates publish.
6. **Provenance is inviolable** — signals only from their source; no forge-as door at any layer;
   tests get honesty by owning every source in their private brain.
7. **UI is not a category** — the shell is the human's connector; UI elements are edge neurons;
   the palette lives in the projection and is the UI trust boundary.

## Next designs (explicitly out of this document's scope)

- **Scopes and permissions for cross-app subscription** — what a behavior may observe and call
  across app and workspace boundaries; consent surfaced from real subscriptions (the durable
  watch set is the app's true wiring and can be shown at install). This is the guard that keeps
  "interconnected any way possible" from becoming "surveilled by anything installed".
- Module boot-list derivation for verification brains from the manifest.
- The renderable palette v1 and how the shell discovers renderable contracts.
- Compute metering/limits for wakes and model calls inside behaviors.
