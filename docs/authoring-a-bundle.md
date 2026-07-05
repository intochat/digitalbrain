# Authoring a Bundle

A bundle is a `NeuroPack` + manifest (see `docs/PRODUCT_VISION.md`).
This is the test-first loop for building one. **Write the test first, then the bundle.**

## The single source of truth

A bundle is defined **once**, as its embodiable pack source string (a `KitExperience`
subclass). Both the fast in-memory test and the live-render E2E compile that same string —
never re-type the experience as a second C# type.

The copy-me starter lives at `DigitalBrain.Tests/Authoring/StarterBundleSource.cs`.

## Two speeds

### 1. Fast loop (milliseconds, no browser) — your daily loop

`BundleHarness` compiles your bundle's source in-process (the same Roslyn/ALC path the
kernel uses) and drives `ExperienceStep`s. Assert the emitted `UiWidgetTree` with the
`UiTreeAssertions` matchers (`ShouldHaveNodeOfType`, `ShouldHaveButtonWithLabel`,
`ShouldHaveSelect`, `ShouldContainText`, `ToGoldenSnapshot`, …).

```csharp
using var harness = new BundleHarness(MyBundleSource.Code, pack: "my-bundle", experienceId: "my-bundle");
var tree = harness.GetTree("ask");
tree.ShouldHaveButtonWithLabel("Go");
```

Run only your bundle's fast tests:

```sh
cd brain
dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~MyBundleTests"
```

### 2. Real-stack loop (tens of seconds, real Aspire + real gRPC) — run before publishing

Publish + install your bundle into the full Aspire stack and drive it over a real gRPC
`WatchHomeFeed`/`Send` call, asserting on the delivered `RfwCardEnvelope` payload — see
`DigitalBrain.Tests/E2E/TravelPlanTripRendersE2ETests.cs` for the pattern. No browser, no Flutter
build — Flutter's own rendering fidelity is covered separately by its `app/test/ui_kit/**` widget tests.

Gated by `E2EPrerequisites.RequireRealStackE2E()` so it skips unless you opt in:

```sh
cd brain
RUN_REAL_STACK_E2E=true dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~MyBundleRendersE2ETests"
```

**One-time Visual Studio setup (recommended):** Test > Configure Run Settings > Select Solution
Wide runsettings File > `e2e.runsettings`. After this, running any `E2E`-tagged test from Test
Explorer already has `RUN_REAL_STACK_E2E=true` set — no terminal needed.

Other useful env flag: `DIGITALBRAIN_E2E_REPLICAS=1` — kernel replicas for the test stack (default 1).

### Warm dev cluster (fastest — skips the 30-120s Aspire boot)

Start a long-lived kernel once, outside Aspire, and every render test attaches to it instead of
booting a fresh cluster:

```sh
cd brain
DIGITALBRAIN_WEBROOT=$(pwd)/app/build/web dotnet run --project DigitalBrain.Kernel
```

(PowerShell: `$env:DIGITALBRAIN_WEBROOT = (Resolve-Path app/build/web); dotnet run --project DigitalBrain.Kernel`)

Leave it running. Render tests probe `http://localhost:8081` at startup; if it responds, they
attach directly (a few seconds) instead of booting a fresh Aspire stack. If nothing is listening
there, tests fall back to today's behavior automatically — there is nothing to configure to opt
out.

State is in-memory. If a dev session's state ever gets confusing, just restart the process —
there is no persisted store to clean up.

## Write a new bundle in ~15 minutes

1. Copy `DigitalBrain.Tests/Authoring/StarterBundleSource.cs`; rename the type, `Pack`,
   `ExperienceId`, and hops.
2. Copy `DigitalBrain.Tests/Authoring/StarterBundleTests.cs`; write the failing fast test
   for your entry hop.
3. Edit your bundle source until the fast test is green.
4. Copy `DigitalBrain.Tests/E2E/StarterBundleRendersE2ETests.cs`; run it from Test Explorer
   (with `e2e.runsettings` wired up, per the real-stack loop section above) or with
   `RUN_REAL_STACK_E2E=true dotnet test --filter "~MyBundleRendersE2ETests"` to prove the real wire.
5. When both are green, the bundle is publishable.

## Lightweight automations complement bundles

Use reactive automations (RegisterReaction + small real C# via AutomationNeuron) for fast glue, personal reactions, "when Activated then ..." without full pack lifecycle. 

When an automation proves valuable, crystallize with MCP `promote_automations_to_pack` (emits stub + AutomationCrystallized signal). The stub can seed a real bundle source for KitExperience + BundleHarness authoring.

See `docs/LIGHTWEIGHT-REACTIVE-AUTOMATIONS-PLAN.md` (Writing Automations + promotion section) for examples. Automations stay orthogonal: no ALC, no publish, immediate.
