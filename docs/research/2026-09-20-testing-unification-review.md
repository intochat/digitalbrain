# Testing and module configuration: research and proposed direction

Date: 2026-09-20. Status: research and proposal, not an approved implementation spec.

## Recommendation

Unify **composition and lifecycle contracts**, while retaining three explicit execution boundaries. Preserve deep, typed module settings; separate browser/debug preferences from the application being tested. Keep the four public libraries requested by the user. Do not promise four physical projects until the external runner and Aspire build responsibilities have an equally clear home.

The desired outcome is short ordinary tests with explicit escape hatches: choose modules or the actual application, override their typed settings, substitute external providers, observe real behavior, and optionally watch a browser. This report evaluates the current working tree, including existing uncommitted changes; it does not supersede them or implement a new framework.

Official-source research and version limitations are in [the companion note](2026-09-20-testing-primary-sources.md). Context7 resolution failed due to its monthly quota; official documentation/source was used instead. The earlier [testing design](../superpowers/specs/2026-09-20-testing-framework-design.md) already distinguished four public packages from support projects. Several current behaviors diverge from that design, and its implementation-status text is stale.

## 1. What the test layers actually mean

| Layer | Execution and dependencies | What it proves | What it cannot prove |
|---|---|---|---|
| Ordinary unit test | Plain object/function, controlled dependencies | Logic, validation, mapping | Orleans or host wiring |
| Current `Testing.Unit` | In-process Orleans cluster, memory storage, optional reminders, local provider replacements | Neuron contracts, signal delivery, activation and behavior interactions | Real HTTP, external process loading, durable storage across process death |
| `Testing.Integration` | Selected modules in external runner; Aspire-managed storage; real HTTP and remote Orleans client | Module endpoints, serialization, dependency loading, provider protocols; persistence when explicitly tested across restart | Actual application's middleware/composition or browser behavior |
| `Testing.E2E` | Actual application AppHost and processes; optional browser | Application wiring and an entire declared scenario | Live third-party correctness when providers are substituted |

The existing Unit package is more precisely a **neuron/component testing harness**. Retaining its name is reasonable for compatibility; document the meaning rather than claiming that every in-process test is an isolated unit test. Plain unit tests need no `StartAsync` at all.

Use the cheapest layer that crosses the seam being asserted. A button's action/state belongs in a neuron test; HTTP binding and response behavior belong in module integration; a user's tap reaching the neuron and updating the display belongs in application E2E. Do not repeat every widget permutation through the full AppHost.

Concrete samples inspected:

- [ButtonFacts](../../src/Modules/Flutter/Tests.Unit/Button/ButtonFacts.cs) calls the neuron, observes its signal and reads state.
- [ButtonHttpFacts](../../src/Modules/Flutter/Tests.Integration/Button/ButtonHttpFacts.cs) sends HTTP to an external runtime and observes a signal.
- [UiKitTwoWayWebFacts](../../src/Applications/IntoChat/Tests/UiKitTwoWayWebFacts.cs) seeds via HTTP, clicks browser controls, observes signals and reads state. That is a useful bidirectional E2E assertion.
- [IsolationFacts](../../src/Testing/DigitalBrain.Testing.Framework.IntegrationTests/IsolationFacts.cs) tests isolation between two runs and persistence after restart. This is distinct from activation-level memory-storage checks.
- [GmailOAuthCallbackFacts](../../src/Modules/Google/Tests/Integration/GmailOAuthCallbackFacts.cs) already substitutes a real external HTTP protocol with a local token endpoint. Keep this pattern.

## 2. The reported failure and the deeper contract problems

### Current test configuration is incompatible with opening a browser

The checkout's two-way test selects `FlutterHostKind.None`, not `Web`. Running it produced `InvalidOperationException: This application configuration exposes no browser endpoint` at `E2EBrain.cs:19` in 15.656 seconds. It does not currently reproduce the supplied 60-second placeholder timeout. `None` means no Flutter host; it does not mean a hidden browser.

### The placeholder is a transient activation control

[E2EBrain](../../src/Testing/DigitalBrain.Testing.E2E/E2EBrain.cs) adds `?semantics=true`. [main.dart](../../src/Modules/Flutter/app/shell/lib/main.dart) then calls and retains `ensureSemantics()`. The harness separately checks placeholder count and evaluates a click through a locator. Flutter can remove the placeholder between those operations when semantics activates. The locator then waits for it to reappear.

This race was reproduced in the existing web-configured `UiKitWebFacts.ChartVideoAndBrowserAppearInFlutterUi` through the same browser startup path. Its trace shows the initial selector finding the placeholder, `queryCount` returning 1, then the Evaluate operation waiting 60 seconds for it. The timeout snapshot has no placeholder, but does have the semantics tree and the expected `E2E BTC chart` text. The UI had loaded while the harness waited for an obsolete activation control. This establishes the failure mechanism in the reproduced run; it cannot retroactively prove every detail of the user's earlier run. Increasing the timeout cannot make a successfully removed activation control return.

Local trace: `src/Applications/IntoChat/Tests/bin/Debug/net11.0/e2e-artifacts/test-5456b873bccd473ba4eacc6af381f4a0/browser-95939e95e3a14e45b983c24e38f4a1b9.zip`. Trace `call@13` returned count 1 at 1826.987 ms; `call@15` ended at 62044.899 ms with the reported timeout. This is an ignored build artifact and may disappear on clean; the observations above preserve the key evidence.

Proposed correction: for this owned application, use its programmatic semantics opt-in, wait for the semantics tree, then assert a meaningful application state. Do not require the transient placeholder. Preserve an optional, disappearance-tolerant fallback only if another supported frontend actually needs it.

The generic E2E package also currently knows Flutter tags and query parameters. Put that preparation in a Flutter-owned browser adapter/application hook; generic browser startup should navigate to the advertised endpoint and provide native Playwright capabilities.

### Readiness has several stages

Storage healthy, runtime healthy, Flutter HTTP server responding, frontend loaded, semantics available, and application/session ready are different conditions. [ShellHostingExtensions](../../src/Modules/Flutter/Aspire.Hosting/ShellHostingExtensions.cs) explicitly says HTTP health can precede the Flutter build finishing. `flutter-view` attached is also weaker than a usable application.

Return failures with their stage, elapsed budget, resource/URL, and relevant diagnostics. Use a bounded navigation/build-ready retry only where needed; do not reload indefinitely or retry scenario actions. Keep browser console/page errors, failed requests, screenshot and trace. Avoid allowing screenshot failure to skip trace collection: the current [BrowserSession](../../src/Testing/DigitalBrain.Testing.E2E/BrowserSession.cs) puts both in one try block.

### Browser options do not currently mean what callers request

[E2ETest](../../src/Testing/DigitalBrain.Testing.E2E/E2ETest.cs) derives headlessness from Flutter hosting kind and replaces `execution.Browser`, ignoring its requested `Headless`. [BrowserOptions](../../src/Testing/DigitalBrain.Testing/BrowserOptions.cs) resolves debugger/environment preferences again at browser launch. Explicit zero slow motion cannot reliably override the headed default. Subsequent `OpenBrowserAsync` launch settings are ineffective after the shared browser has launched.

Make browser launch preferences immutable per brain/session and resolve them once. Explicit per-test values should override environment/debugger defaults; nullable override fields or a small `Default/Headless/Headed` enum can distinguish unspecified values. Reject incompatible later launch requests, or deliberately support separate engines.

| Setting | Meaning |
|---|---|
| Flutter `Web` | Serve the web frontend |
| Browser headless | Run that frontend in a browser without a visible window |
| Browser headed | Show the browser so a developer can watch |
| Flutter `Headless` | Existing pure-Dart host, a different execution path from web rendering |
| Flutter `None` | No Flutter host; module HTTP/neuron tests can still run |
| Flutter `Window` | Native desktop host; Playwright browser driving does not cover this |

## 3. Why configuration feels unfinished

These are concrete inconsistencies, not merely verbose syntax:

1. **Three composition paths.** [UnitTest](../../src/Testing/DigitalBrain.Testing.Unit/UnitTest.cs) accepts `IModule` instances; [IntegrationTest](../../src/Testing/DigitalBrain.Testing.Integration/IntegrationTest.cs) resolves transportable definitions; E2E transports only definition configuration while [IntoChat AppHost](../../src/Applications/IntoChat/AppHost/AppHost.cs) separately hard-codes module registrations. Passing an E2E module list does not replace the application's module graph.
2. **Incomplete unit dependency expansion.** Unit startup resolves definitions to apply configuration, but invokes `Configure` over the original module list. Declared dependencies can contribute settings without their runtime registrations. Duplicates can also be configured more than once. Use one resolved list for both steps.
3. **Defaults disagree.** `new DigitalBrainConfiguration()` defaults Flutter to Window; parameterless E2E startup defaults it to Web inside the AppHost. Explicit default configuration and omitted configuration therefore mean different things.
4. **Typed settings cover too little.** `FlutterHostingOptions` contains only Kind, while Aspire `FlutterHostOptions` also contains working directory, shell/chat, executable and device settings. Google typed options expose PublicOrigin; the OAuth integration test bypasses them with configuration strings for its token endpoint.
5. **AppHost reconstructs settings.** It binds only selected leaves, creates another options object, manually switches Flutter host methods, then forwards strings to the runtime. Configuration knowledge is scattered across application, harness, module and hosting adapter.
6. **Execution settings are partly decorative.** `AssertionTimeout` is only declared/validated; `SignalProbe` and behavior waits use `TestLimits.Timeout`. Browser waits hard-code different limits. Unit lifetime has its own resource list and cleanup policy instead of using the common session lifetime.

The useful existing foundation is `ModuleDefinition` plus `ModuleComposition`; extend and correct that seam instead of creating a second registry. Preserve immutable snapshots, dependency/conflict validation, native `IDigitalBrain`, and direct Playwright access.

## 4. Proposed common composition contract

The production composition layer should describe **which modules and which typed settings**, without owning browsers, test timeouts or xUnit. Each module maps and validates its own options. Both Aspire and runtime registration consume the same resolved snapshot.

Conceptual flow:

```text
Typed application/module settings
             |
     resolve + validate snapshot
       /            |             \
Unit runtime    Integration host    Actual application AppHost
local fakes     process-safe fakes  process-safe overrides
       \            |             /
          IDigitalBrain + probes
```

Keep `AddModule<T>` as a familiar authoring entry point. A typed options overload/module-owned extension should produce the same definition that `Module.Define(options)` produces. The callback receiving `DigitalBrainModuleBuilder<T>` is currently an Aspire hosting customization; do not pretend it is transportable runtime configuration. Host-specific customization remains an explicit advanced extension.

For application E2E, the real AppHost owns the application graph. Apply typed overrides to existing module slots before graph construction. Reject unknown module replacements rather than accepting and silently ignoring them. Arbitrary module selection belongs to Integration; an application may deliberately expose optional modules in its own configuration. This distinction keeps the E2E claim honest.

Configuration rules:

- One binding/validation implementation per module, shared by development and tests.
- Known deterministic defaults for a test run; ambient developer configuration should not silently fill application-owned values. Secret references are resolved separately.
- Resolve overrides once, freeze, then register runtime and hosting projections from that snapshot.
- Explicit dependency resolution and replacement semantics; never use accidental registration order as the public override contract.
- Test-owned identity, storage, endpoints and ports cannot be overwritten by ordinary module options.
- No arbitrary objects/delegates across a process boundary. No resolved credentials in command-line configuration or diagnostic dumps. The OAuth test's current literal credentials are fake; do not generalize that transport to real credentials.

### Provider substitutions

In-process: retain local `ConfigureSilo`/`ConfigureServices` callbacks. Prefer explicit replacement of the selected interface; retain module-owned fakes for controllable time, chat, GitHub and storage providers. Do not force local fakes through serialization.

External processes: prefer a local HTTP stub for HTTP providers and a typed endpoint override (the current OAuth test already demonstrates this). If a provider has no network seam, permit a test support module/adapter whose implementation is compiled into the runner's dependency closure and loaded at startup. Validate that it exists in the child, not merely in the test process. Avoid a universal `Replace<T>(object)` that cannot deliver its promise.

## 5. What simpler tests could look like

The following is **proposed syntax, not currently compiled interfaces**. It illustrates a consistent options shape, not a requirement to introduce a fluent DSL.

```csharp
await using var brain = await E2ETest.StartAsync<Projects.IntoChat_AppHost>(new()
{
    Application = new DigitalBrainConfiguration
    {
        Flutter = new() { Hosting = new() { Kind = FlutterHostKind.Web } },
    },
    Browser = new() { Headless = false, SlowMoMilliseconds = 250 },
}, ct);

await using var browser = await brain.OpenBrowserAsync(ct);
```

The application object would implement a small production snapshot contract (or use a strongly typed application-owned adapter); it must not be accepted as an untyped `object`. Its defaults must match the AppHost. E2E options belong to E2E, so Browser does not leak into the common execution type.

```csharp
await using var brain = await IntegrationTest.StartAsync(new()
{
    Modules = [FlutterModule.Define(new()
    {
        Hosting = new() { Kind = FlutterHostKind.None },
    })],
    Execution = new() { StartupTimeout = TimeSpan.FromSeconds(45) },
}, ct);

await using var local = await UnitTest.StartAsync(new()
{
    Modules = [GoogleModule.Define(new())],
    ConfigureSilo = silo => silo.Services.AddSingleton<IGmailTokenExchange>(fakeTokens),
}, ct);
```

Use common `Execution` for startup, assertion and cleanup budgets, diagnostics and artifacts across all three. Retain separate capability-specific options. A module builder is worth adding only if it reduces repeated setup after these contracts are correct; record initializers remain a good deep-configuration escape hatch.

Watching a test should normally require only an environment/IDE execution preference, not changing its application composition. Web + headless is the same frontend scenario as Web + headed. Switching to the pure-Dart host changes the scenario and should be explicit.

## 6. Project count: what to consolidate and what costs remain

| Current project | Responsibility | Recommendation |
|---|---|---|
| `DigitalBrain.Testing` | Probes, waits, shared lifetime/execution | Keep lightweight; move browser preferences out |
| `.Unit` | In-process Orleans host | Keep |
| `.Integration` | Selected modules, bundle validation, hosted runtime | Keep |
| `.E2E` | Real AppHost and browser driving | Keep; remove Flutter-specific initialization |
| `.Hosting` | Shared Aspire lifecycle/client connection | Keep as hidden implementation initially; evaluate relocation with dependency costs visible |
| `.ModuleRunner` | Child-process executable | Keep as internal tool until a replacement preserves external-process guarantees |
| `.ModuleAppHost` | Selected-module Aspire graph | Candidate for programmatic construction after a packaging feasibility test |
| `.Framework.Tests` | Framework tests without containers | Move under framework tests folder, not public library group |
| `.Framework.IntegrationTests` | Framework process/isolation tests | Move alongside framework tests; preserve separate fast/slow execution |

There are nine physical projects, not nine public testing layers. The existing spec deliberately chose four public packages plus support projects. Some implementation types are nevertheless public, so simply calling those projects internal is insufficient; hide what consumers do not need.

Three approaches:

1. **Recommended: four public packages, explicit internal tools/tests.** Fix configuration/lifecycle first; organize solution folders as Testing/{four packages}, Testing/Infrastructure, Testing/Tests. This reduces the learning surface immediately but honestly does not meet a literal four-project cap. Investigate removing ModuleAppHost afterwards.
2. **Exactly four framework projects.** Move shared hosting into Integration with E2E depending on it, or source-share it; construct the module AppHost programmatically; absorb the external runner into an executable-capable Integration assembly or build-time asset. This requires validating Aspire metadata, entrypoint behavior, dependency closure, NuGet packing and child-process launch. It reduces project files but mixes responsibilities and may increase build complexity. Framework test projects still have to exist somewhere.
3. **In-process integration to eliminate runner infrastructure.** Simpler hosting but changes the coverage contract: loses independent-process assembly loading and process restart fidelity. Not recommended as a silent cleanup; only choose if those guarantees are intentionally dropped.

Aspire 13.5.3 has a programmatic testing builder, but still requires SDK-generated AppHost metadata and runtime assets. The primary-source note links the exact implementation. Therefore removing ModuleAppHost is a feasibility question, not a promised deletion. Removing all support projects is not automatically simpler.

## 7. Migration order and acceptance checks

1. Fix browser configuration/readiness separately: Web+headed and Web+headless must both run; None+browser fails clearly; no required placeholder click; preserve cancellation and artifacts. Prove the two-way UI scenario repeatedly with slow motion both zero and enabled.
2. Make module composition authoritative: the exact resolved dependency list configures unit runtime, integration runtime and hosting. Add tests where a dependency registers a required service; verify deduplication and explicit conflicts.
3. Unify typed options and application binding: stop manually remapping a subset in AppHost; exercise a non-default setting from authoring through host/runtime. Verify omitted/default options agree and unknown overrides fail.
4. Add consistent options wrappers and precedence tests. Ensure the common assertion budget actually changes probe/wait behavior. Define one browser startup budget separate from initial AppHost startup, and one cleanup budget rather than fresh budgets per nested owner.
5. Establish typed provider overrides, first by converting the existing OAuth endpoint stub case. Keep existing module fakes and test two parallel isolated runs.
6. Consolidate only after proving packaging: selected module without static runner reference, transitive/native dependencies, adapter loading, external PID, restart persistence, cleanup/cancellation. Move framework tests without deleting their coverage.
7. Update examples, package descriptions and the older design/status documents after implementation decisions are accepted. Avoid a second permanent API beside the old one; migrate callers and remove obsolete entry points.

Do not start with broad file moves or a universal builder. The highest-value repair is one truthful composition contract with execution settings that are actually honored.

## 8. Validation performed during this research

Exact commands and completed results are recorded below; these are targeted samples, not a full-suite certification.

- `dotnet test --project src/Applications/IntoChat/Tests/IntoChat.Tests.csproj -p:CodeGraphRefresh=false -- --filter-method '*NeuronChangeShowsInUiAndTapUpdatesNeuron*'`: one failure; no browser endpoint with the existing None configuration, 15.656-second test duration.
- `dotnet test --project src/Testing/DigitalBrain.Testing.Framework.Tests/DigitalBrain.Testing.Framework.Tests.csproj -p:CodeGraphRefresh=false`: 6 passed.

- `dotnet test --project src/Applications/IntoChat/Tests/IntoChat.Tests.csproj --no-build -- --filter-class '*UiKitWebFacts'`: one failure, exact 60-second placeholder timeout, 83.057-second test duration; browser trace confirms loaded semantics and expected chart text while the placeholder was absent.
- `dotnet test --project src/Modules/Flutter/Tests.Unit/DigitalBrain.Modules.Flutter.Tests.Unit.csproj -p:CodeGraphRefresh=false -- --filter-class '*ButtonFacts'`: 1 passed, 2.258-second test assembly duration.
- `dotnet test --project src/Modules/Flutter/Tests.Integration/DigitalBrain.Modules.Flutter.Tests.Integration.csproj -p:CodeGraphRefresh=false -- --filter-class '*ButtonHttpFacts'`: 1 passed, 16.641-second test assembly duration.

Totals: 8 targeted tests passed and 2 application tests failed as described. Full test suites, repeated race reproduction, restart/isolation execution, package consumption and the proposed API were not validated. No product code was changed by this research.
