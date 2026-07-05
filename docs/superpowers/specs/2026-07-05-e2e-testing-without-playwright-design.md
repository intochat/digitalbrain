# DESIGN — E2E Testing Without Playwright + Flutter Test Scope Cleanup

Status: APPROVED (conversational, this session). Proceeding straight to implementation per user
instruction — no separate spec-file review gate. Repo: `E:\brain`. Date: 2026-07-05.

## 1. Why

A CI/deploy caching cleanup pass (separate, already landed this session: NuGet restore caching in
`ci.yml`/`deploy.yml`) surfaced that `DigitalBrain.Tests.csproj` downloads Chromium via Playwright on
every build even though CI never runs E2E tests. Investigating *why* Playwright is there at all
surfaced a bigger question: does a neuron/synapse architecture need browser-driven DOM assertions for
"real" E2E at all, or is that testing the wrong layer?

Answer, confirmed by reading the actual fixtures: no. `DigitalBrainAppHostFixture` (Aspire.Hosting.Testing,
real kernel, real Orleans cluster, zero browser) is already the base class Playwright's
`DigitalBrainBrowserFixture` extends. `NativeGrpcGalleryDeliveryE2ETests` already proves the pattern:
real Aspire stack + real gRPC wire + assert on delivered payload, no DOM. Two more facts confirmed this
is safe to generalize:

- `DIGITALBRAIN_WEBROOT` (`DigitalBrain.Kernel/Program.cs:286-293`) is fully optional — the kernel boots
  fine and just skips static-file serving if the directory doesn't exist. The Flutter web build is not
  actually required for the backend to function.
- The one test whose entire point is browser-specific (`LoginRendersE2ETests`, guarding that gRPC-Web
  only supports unary RPCs, not bidi) can be reproduced faithfully without a browser: `Grpc.Net.Client.Web`'s
  `GrpcWebHandler` already does this in `DigitalBrain.Tests/Kernel/KernelGrpcWebTests.cs`.

Separately, the user raised a second, related concern: Flutter's own test suite (`app/test/**`) has grown
beyond ui_kit widget tests into testing app-level business logic (routing, gRPC envelope construction,
experience/hop matching, stream retry logic). Given the architectural stance that "Flutter must stay
thin — only ui_kit components," those tests are out of place there.

## 2. C# E2E test changes

**Delete:**
- `DigitalBrain.Tests/E2E/DigitalBrainBrowserFixture.cs`
- `DigitalBrain.Tests/E2E/ExperienceFlowDriver.cs` (the `[Obsolete]` `LiveRenderVerifier` alias) and the
  live parts of `LiveRenderVerifier.cs` itself — folded into the fixture removal
- `Microsoft.Playwright` `PackageReference` in `DigitalBrain.Tests.csproj`
- The `EnsurePlaywrightBrowsersInstalled` MSBuild target (this obsoletes the `SkipPlaywrightInstall`
  gate + `-p:SkipPlaywrightInstall=true` flags added to `ci.yml`/`deploy.yml` earlier this session —
  nothing left to gate once the target is gone)
- `E2EPrerequisites.cs`'s web-bundle machinery: `WebBundlePresent`, `EnsureWebBundleFresh`,
  `ComputeSourceFingerprint`/`IsWebBundleStale`, and `E2EPrerequisitesFreshnessTests.cs` (tests for logic
  being deleted)
- Dead stub files (zero test methods remaining, confirmed via repo sweep):
  `HelloWorldRendersE2ETests.cs`, `SimpleColorPickerRendersE2ETests.cs`, `UiGalleryRendersE2ETests.cs`
  (E2E folder), `Distribution/BundleManifestEmbodimentTests.cs`, `Ui/BundleHarnessTests.cs`,
  `Ui/SimpleColorPickerHarnessTests.cs` (class inside is actually named `UiTestingFrameworkExamples` —
  stale rename, also dead)
- The `flutter build web` step from `docs/authoring-a-bundle.md`'s render-loop section

**Rename:** `RUN_FLUTTER_E2E` → `RUN_REAL_STACK_E2E` in `e2e.runsettings`, `E2EPrerequisites.OptedIn`,
`RenderRunSettingsTests.cs`, and doc references — the old name stops describing what the flag does once
Flutter itself is no longer built or driven.

**Rewrite to real-wire-only** (`DigitalBrainAppHostFixture`, real gRPC, assert on delivered payload,
no DOM):
- `TravelPlanTripRendersE2ETests` — assert each hop's marker arrives over `WatchHomeFeed`.
- `PackEmbodimentRendersE2ETests` — already sends synapses over gRPC; drop browser navigation/screenshot,
  assert the `RfwCardEnvelope`'s `CorrelationId`/payload directly.
- `LoginRendersE2ETests` — use `GrpcWebHandler(GrpcWebMode.GrpcWeb, ...)` (same pattern as
  `KernelGrpcWebTests.cs`) for a real unary gRPC-Web login call; assert signed-in state arrives over
  `WatchHomeFeed`. Preserves the actual regression guard (unary vs. bidi dispatch).
- `NativeGrpcGalleryDeliveryE2ETests` — retype fixture parameter from `DigitalBrainBrowserFixture` to
  `DigitalBrainAppHostFixture` (never touched `Page`/`Browser`).
- `DigitalBrainE2ECollection`'s `ICollectionFixture<DigitalBrainBrowserFixture>` →
  `ICollectionFixture<DigitalBrainAppHostFixture>`.

**Net effect:** real E2E becomes `RUN_REAL_STACK_E2E=true dotnet test --filter "~E2E"` — no Flutter SDK,
no browser, no build-web step, no staleness fingerprinting.

## 3. Flutter test scope cleanup (`app/test/**`)

Surveyed all 22 files against "does this test a presentational ui_kit widget (props in, tree out), or
app-level business logic?"

**Keep as-is (11 files, genuine ui_kit widget tests):** `ui_kit/ui_display_a_test.dart`,
`ui_display_b_test.dart`, `ui_feedback_test.dart`, `ui_inputs_a_test.dart`, `ui_inputs_b_test.dart`,
`ui_layout_test.dart`, `ui_nav_a_test.dart`, `ui_nav_b_test.dart`, `ui_overlays_test.dart`,
`ui_registry_test.dart`, `ui_kit_widgets_test.dart`.

**Delete (6 files, pure business/plumbing logic, zero widget pumping):**
`features/experience/experience_match_test.dart` (routing predicate), `rfw_host/inline_rfw_surface_test.dart`
and `rfw_host/rfw_semantics_test.dart` (RFW runtime-host plumbing), `grpc/endpoint_test.dart` (env/URI
resolution), `grpc/action_dispatch_test.dart` (envelope construction), `perf/perf_stream_test.dart`
(stream retry/backoff).

**Rewrite (5 files, currently mixed):** strip business-logic assertions, rebuild rendering assertions by
constructing `ui_kit` widgets directly instead of routing through `ExperienceHopView`/`RfwRuntimeHost`:
`features/experience/config_form_tree_test.dart`, `experience_hop_view_test.dart`,
`experience_hop_view_tree_test.dart`, `ui_kit/ui_gallery_hop_render_test.dart`. `shell/forui_app_shell_test.dart`
splits into a `ShellChatComposer`-only ui_kit-style test (kept) plus deletion of the
routing/classification/file-intake function tests (`classifySurface`, `autoSwitchTargetForKind`,
`shellChatIsSelected`, `ingestDroppedFilesForShell`, `appendTranscriptToComposer`).

**Accepted coverage trade-off (explicit, not silent):** after this cleanup, the following production
logic has no automated test anywhere: `PerfStream` retry/backoff, `experienceHopMatches` predicate, RFW
host DSL compile/semantics-id wiring, gRPC endpoint resolution, and the Dart-side action-envelope-building
functions (`buildActionEnvelope`/`buildPanelEventEnvelope`) — though the envelope *wire contract* these
last two exercised is now proven for real by the rewritten C# E2E tests (§2), which build and send real
envelopes over the real wire rather than asserting a hand-built JSON shape in Dart. This is a deliberate
result of "Flutter stays thin," not an oversight — flagged here so it's easy to revisit (e.g. as plain-Dart,
non-widget tests) if any of this logic proves to need its own coverage later.

## 4. Verification

- `dotnet build Brain.slnx -c Release -p:SkipFlutterBuild=true` builds clean (no more
  `SkipPlaywrightInstall` property — it no longer exists).
- `dotnet test Brain.slnx -c Release -p:SkipFlutterBuild=true --filter "FullyQualifiedName!~E2E"` still
  green (fast loop unaffected).
- `RUN_REAL_STACK_E2E=true dotnet test DigitalBrain.Tests --filter "FullyQualifiedName~E2E"` passes
  against a real Aspire-hosted stack, with no Flutter build and no browser involved.
- `flutter test` in `app/` green with the reduced/rewritten suite.
- Confirm `Microsoft.Playwright` no longer appears anywhere in `DigitalBrain.Tests.csproj` or
  `Directory.Packages.props` usage for this project.

## 5. Approval trail

- Design proposed section-by-section this session; user selected "Remove entirely" for Playwright scope,
  "Yes, drop it" for the web-build prerequisite chain, "Rename it" for `RUN_FLUTTER_E2E`.
- User then expanded scope: "get rid of all outdated tests as well as flutter tests with business logic -
  only the ui kit tests in flutter" — folded into §3 above via a fresh file-by-file survey (not assumed).
- User said "go to implementation" — proceeding directly to `writing-plans` without a separate
  spec-file-review round-trip.
