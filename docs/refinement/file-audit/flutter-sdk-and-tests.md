# Subsystem Audit: flutter-sdk-and-tests

- **Subsystem**: flutter-sdk-and-tests — `app/packages/digital_brain_sdk_flutter` (perf SDK package), `app/tool` (dev scripts), `app/test` (Dart/Flutter tests), `app/assets`, and app-root config files (`pubspec.yaml`, `analysis_options.yaml`, `devtools_options.yaml`, `.gitignore`, `.metadata`, `Flutter.proj`, `pubspec.lock`).
- **Commit**: `72400e3ebbec27e17af4ae6b5b2c4158c2797fa4` (branch `docs/refinement-audit`)
- **Date**: 2026-07-13
- **Documentation gap**: Context7 was unavailable during this audit (monthly quota exceeded on both configured instances). Flutter/Dart framework-usage assessments below are grounded in training knowledge plus one pub.dev web check (bloc 9.2.0 current; bloc_test 10.x pairs with bloc 9 — no incompatibility with `flutter_bloc ^9.1.1`). Where a claim depends on very recent package changes, confidence is marked accordingly.

## Subsystem overview

This slice contains three distinct things:

1. **`digital_brain_sdk_flutter`** — a small path-dep package whose whole scope is client-side performance self-instrumentation: frame-timing sampling (`PerfProbe`), a widget census, a retrying sample/hint pump (`PerfStream`), a tier signal (`PerfTier*`), and tier-dependent render throttles (`throttle.dart`). Its transport boundary is a closure-injected adapter (`PerfGatewayClient`) so the package itself has zero gRPC/proto dependency. In today's wiring (`app/lib/main.dart:32-38`) both closures are no-ops because the kernel does not implement the perf RPCs — the SDK's gateway path is currently speculative end-to-end.
2. **`app/test`** — the client test suite. The `runtime/` tests (session, transport, protocol, controller, shell, surface view) are genuinely strong: fail-closed identity checks, token-leak assertions, reconnect/reset/idempotency races. The `ui_kit/` tests are thin smoke/interaction tests. There are **zero** tests for the SDK package, telemetry, rfw_host, digital_brain_ui, features, shell, or router.
3. **`app/tool` + assets + root config** — a boundary-check script that is real CI value, two "challenger" demo-era stress scripts (one of which targets deleted files and can only fail), a hand-rolled smoke script that should be a unit test, demo RFW assets that are referenced nowhere in the repo (including a **13.3 MB** Lottie binary bundled into every build), and a pubspec with `widgetbook: any` in production `dependencies` plus at least nine declared-but-unused packages.

Connection to the rest of the OS: the runtime tests are the client-side enforcement of the OS trust model (tenant/workspace/principal scoping, signed action tokens, fail-closed refresh); they directly support the OS model. The SDK and the demo assets/tools are peripheral and mostly weaken it through dead weight.

---

## Per-file review

### `app/packages/digital_brain_sdk_flutter/pubspec.yaml` (1-16)
Perf SDK manifest. `publish_to: 'none'`, version 0.1.0, deps: `flutter` + `uuid ^4.5.1`, dev: `flutter_lints ^6.0.0`. Minimal and clean; SDK constraint matches the app (`sdk ^3.11.0`, `flutter >=3.41.0`). No test framework declared — consistent with the package having no tests (TEST-901). Name promises "SDK", content is perf-only (ARCH-902). **Verdict: retain** (rename or fold into app if the perf pipeline stays a no-op — see ARCH-900).

### `app/packages/digital_brain_sdk_flutter/analysis_options.yaml` (1-28)
Stock `flutter_lints` include, all customization commented out. No strict language modes. Same file verbatim as app root. (FRAME-902) **Verdict: simplify** (adopt one shared, stricter analysis config).

### `app/packages/digital_brain_sdk_flutter/.gitignore` (1-2)
`.dart_tool/`, `build/`. Correct and minimal. **Verdict: retain.**

### `app/packages/digital_brain_sdk_flutter/lib/digital_brain_sdk_flutter.dart` (1-11)
Barrel export of all 11 `src/` files. Public API = everything; no visibility layering. Exports the dead `perf_tier_thresholds.dart` (CLEAN-903). **Verdict: retain** (drop dead export).

### `app/packages/digital_brain_sdk_flutter/lib/src/gateway/perf_gateway_client.dart` (1-13)
The closure-injected adapter: two fields, `pushSamples(Stream<PerfSample>)` and `watchHints(String clientId)`. FACT: this keeps gRPC/proto out of the SDK and breaks the circular path-dep, as its comment says. Assessment of the abstraction: the *direction* is clean (domain types in, closures out), but it is currently a seam to nothing — the only production construction (`app/lib/main.dart:34-37`) injects `samples.drain()` and `Stream.empty()` (ARCH-900). The contract is also under-specified: nothing states whether `pushSamples` should complete (reconnect semantics live implicitly in `PerfStream`'s retry loop), and errors have no channel. **Verdict: retain** the pattern; specify the contract when a real gateway exists.

### `app/packages/digital_brain_sdk_flutter/lib/src/gateway/perf_tier_hint.dart` (1-7)
Tier + free-text `reason`. Trivial DTO. **Verdict: retain.**

### `app/packages/digital_brain_sdk_flutter/lib/src/perf/perf_probe.dart` (1-105)
StatefulWidget that samples `FrameTiming` via `SchedulerBinding.addTimingsCallback`, flushes p50/p95/jank% once per `samplePeriod`, and runs a widget census every Nth flush. Correctness issues: `_rebuildAccumulator` increments once per `FrameTiming` (one per *frame*), so `rebuildsPerSecond` is actually frames/second — a mislabeled metric (PROD-900). Jank threshold is hard-coded `16.0` ms and ignores both display refresh rate and the existing `PerfTierThresholds` type (PROD-901). Entirely disabled in release mode (`kReleaseMode` early return), meaning the "self-instrumenting perf SDK" collects nothing where it matters most; comment acknowledges hints flow independently, but hints would be derived from samples that never exist. Lifecycle handling (init/dispose symmetry) is correct. Platform detection collapses everything non-web/non-Windows to `'other'`. **Verdict: simplify** (fix metric name or count real rebuilds; parameterize jank budget).

### `app/packages/digital_brain_sdk_flutter/lib/src/perf/perf_sample.dart` (1-26)
Immutable sample DTO, 10 fields, no serialization (mapping is the adapter's job — good boundary). **Verdict: retain.**

### `app/packages/digital_brain_sdk_flutter/lib/src/perf/perf_stream.dart` (1-75)
Owns the outbox (`StreamController.broadcast`), client id (uuid v4 per launch), and two unawaited retry pumps. Issues: bare `catch (_)` swallows every error with no logging/metric (REL-900); samples pushed while the push pump is between retries are silently dropped (broadcast stream, no buffer — acceptable for telemetry but undocumented); backoff cap logic overshoots — `backoff < _maxRetryDelay ? backoff * 2 : backoff` yields 250ms→…→4s→**8s** steady-state despite `_maxRetryDelay = 5s`, and backoff never resets after a successful long-lived connection (PERF-900). With today's no-op wiring the watch pump loops forever against an always-empty stream, waking every ≤8s for the app's lifetime doing nothing (PERF-900). `dispose()` is idempotent and closes the outbox before disposing the controller — reasonable. **Verdict: simplify.**

### `app/packages/digital_brain_sdk_flutter/lib/src/perf/perf_tier.dart` (1-7)
3-value enum + lenient `perfTierFromString` (unknown → `smooth`, i.e. fail-open to full quality; for a *downgrade* hint channel fail-open is the safe direction for UX, unsafe for perf — acceptable). **Verdict: retain.**

### `app/packages/digital_brain_sdk_flutter/lib/src/perf/perf_tier_controller.dart` (1-13)
`ChangeNotifier` with change-suppressed `update`. Correct. **Verdict: retain.**

### `app/packages/digital_brain_sdk_flutter/lib/src/perf/perf_tier_scope.dart` (1-19)
`InheritedNotifier` with `of`/`maybeOf`; `of` uses `assert` + `!` so it hard-crashes in release if unmounted — standard Flutter idiom. **Verdict: retain.**

### `app/packages/digital_brain_sdk_flutter/lib/src/perf/perf_tier_thresholds.dart` (1-5)
Two-field const class. FACT: referenced nowhere in the SDK, app `lib/`, tests, or tools (repo-wide grep). Dead/speculative (CLEAN-903). **Verdict: delete** (or actually use it in `PerfProbe`).

### `app/packages/digital_brain_sdk_flutter/lib/src/perf/widget_census.dart` (1-46)
Iterative element-tree DFS counting widgets and `GlowIcon`s. Two leaks of app knowledge into the SDK: global mutable `static Type? glowIconType` (set from `app/lib/main.dart:22`) and a hard-coded `'GlowIcon'` string fallback — the SDK knows the app's widget by name (ARCH-901). Full-tree DFS on the UI isolate is bounded by `censusEveryNFlushes` and debug/profile-only (PERF-901, Note). **Verdict: simplify** (generalize to a registered predicate/counter set; remove the string fallback).

### `app/packages/digital_brain_sdk_flutter/lib/src/tier_throttle/throttle.dart` (1-23)
Pure functions mapping tier → glow dot count, blur sigma, rim-glow toggle, scene-tick interval. Comment references app internals (`SynapseStreamFeed.publish`, `_LiveScreenState._onTick`) — app-specific render tuning living inside the "SDK" (ARCH-901). Consumed by `app/lib/digital_brain_ui/glow/glow_icon.dart`. **Verdict: move** (these belong in the app's design layer; the SDK should expose the tier only).

### `app/assets/ino-catalog.json` (1-47)
Static contract catalog (Fqn/Kind/Fields) consumed by `app/lib/rfw_host/digitalbrain_rfw_library.dart:483` for the Creator prompt experience. Contains `Acme.Submit`/`Acme.Worker` demo entries and unexplained magic `Kind` integers (0/1/2 = synapse/signal/neuron per `app/tool/challenger_m4_stress_test.dart` comment — the only place the encoding is documented). `DB.Google.Auth` lists a raw `token` field as an ordinary catalog field (SEC-901, Note). A hard-coded client asset duplicating what the kernel's contract registry should serve. **Verdict: replace** long-term (serve catalog from kernel); prune demo entries now (CLEAN-904).

### `app/assets/rfw/activity_overlay.rfwtxt` (1-1408)
Large RFW library with per-"neuron kind" boolean-flag branches (`...for x in data.isX` loops emulating conditionals) and compact/medium adaptive variants. FACT: referenced by no Dart, C#, or config file in the repo; the runtime path renders server-sent `libraryText` instead (see `surface_view_test.dart:1140-1161`). Also contains hard-coded fake metrics presented as live data ("42 / 100", "COMMISSION RATE 20%", "TOTAL BUNDLES 16", "LICENSES ISSUED 94") and demo theater ("Parallel Universes", forked timelines). Dead demo asset bundled into every build (CLEAN-900). **Verdict: delete.**

### `app/assets/rfw/sample_neuron.rfwtxt` (1-54)
Small avatar/title RFW sample. Same status: referenced nowhere (CLEAN-900). **Verdict: delete.**

### `app/assets/lottie/orbit.lottie` (binary, 13,349,278 bytes — not line-auditable)
FACT: 13.3 MB binary bundled via `pubspec.yaml` `assets: - assets/lottie/`, referenced by name nowhere in the repo. `Lottie.asset(src)` in `palette_primitives.dart` takes a dynamic `src`, so a server-driven surface *could* name it, but nothing in the repo does. 13.3 MB of dead weight in every web download and desktop install (CLEAN-900). **Verdict: delete** (or move behind deferred loading with a named consumer).

### `app/assets/shaders/glass_refract.frag` (1-44)
Flutter runtime-effect fragment shader (glow + specular + prismatic border) consumed by `app/lib/digital_brain_ui/glass/glass_material.dart:65`. Uses `#include <flutter/runtime_effect.glsl>` and float/vec uniforms only — consistent with Flutter fragment-shader constraints (uniforms are float-packed; no samplers used). Border math is per-fragment branchy but trivial. **Verdict: retain.**

### `app/test/grpc/endpoint_test.dart` (1-38)
Two cases for `resolveEndpointFrom` (explicit kernel endpoint wins; web-host fallback). Thin: no non-web branch, no port/scheme edge cases. Embeds real Azure Static Web Apps / Container Apps hostnames of an actual deployment (SEC-900). **Verdict: simplify** (synthetic hostnames, add non-web cases).

### `app/test/runtime/test_fixtures.dart` (1-221)
Shared builders for identities, session bundles, surface JSON, INO payloads, OAuth connection actions. Well-factored; fixed `testNow` keeps time deterministic; uses Dart 3.x null-aware map entries (`'safeReason': ?safeReason`). Note: `testSurface()` decodes through a `SurfaceEnvelopeDecoder` with a trusted origin — fixtures encode the real protocol, not shortcuts. **Verdict: retain.**

### `app/test/runtime_test.dart` (1-246)
SessionController + FeedController unit tests: bootstrap/sign-out, fail-closed on refresh identity change, duplicate/gap/reset feed semantics, tenant/workspace/principal scope violations without mutation, cross-workspace non-delivery, invalid reset snapshot atomicity. Even asserts `credentials.toString()` cannot leak tokens. High-quality, OS-model-supporting tests. **Verdict: retain.**

### `app/test/runtime/grpc_ui_transport_test.dart` (1-473)
Transport-port tests via `GrpcClientPort` fake: metadata exactness (audience header, no session header on bootstrap/refresh), timeout policy, TLS-only production channel, timeline-logging kill switch, malformed external-identity token rejection *before* any RPC, feed resume/reset mapping, cancel propagation, close-cancels-inflight-unary, anonymous-call rejection, private-field rejection in action input, and error mapping that provably drops server detail strings (`must-not-escape`). Excellent trust-boundary coverage. One note: `production channel disables metadata-bearing timeline logging` mutates the global `isTimelineLoggingEnabled` — global state across tests, order-sensitive if extended. **Verdict: retain.**

### `app/test/runtime/runtime_configuration_test.dart` (1-82)
Endpoint parsing (HTTPS-only, no path/query/userinfo) and OIDC external-identity config normalization/rejection (scope count bound, control chars, plaintext issuer). Good fail-closed configuration tests. **Verdict: retain.**

### `app/test/runtime/runtime_controller_test.dart` (1-963)
The strongest file in the suite: authentication races (older bootstrap cannot clobber newer session), scope-epoch rebind clears state *before* notifying listeners (asserted via listener-observed invariants), reconnect resume semantics, server reset atomicity, terminal error preservation, stop-vs-inflight-ACK deadlock guard, action receipt rejected after scope change, invalid-surface terminal states without ACK/reconnect. Uses polling `_eventually` (1ms×100) rather than `fakeAsync` — real-time waits, slight flake surface but bounded. **Verdict: retain.**

### `app/test/runtime/runtime_shell_test.dart` (1-396)
Widget tests for `RuntimeShell`: bootstrap render, transport ownership/close on unmount, construction-failure message hiding internals, typed secret not retained, terminal error copy without protocol detail leakage, surface expiry suppression with injected clock, INO draft retention across reconnect/terminal states. Direct mutation of `runtime.status` (`runtime.status = RuntimeStatus.reconnecting`) reaches into controller internals — pragmatic but couples tests to a settable status field. **Verdict: retain.**

### `app/test/runtime/session_state_test.dart` (1-250)
Session race-hardening: stale bootstrap/refresh completions cannot clobber newer sessions, concurrent access-token callers share one refresh, refresh blocked after reauthentication begins. Directly encodes the fail-closed auth invariants. **Verdict: retain.**

### `app/test/runtime/surface_protocol_test.dart` (1-449)
Decoder tests: capability negotiation, envelope byte cap, action-revision binding, credential/PII key rejection in payloads (11 key spellings), legacy INO operation shape gating, and an extensive OAuth `openUrl` target matrix (origin pinning, flow-token format bounds, no extra query params/fragments/userinfo, per-provider paths). This is the client half of the "auth on-demand, fail-closed" OS rail and it is thoroughly tested. **Verdict: retain.**

### `app/test/runtime/surface_view_test.dart` (1-1205)
Rendering + interaction tests: widget-tree payloads, action token routing, optimistic INO send with clientSubmissionId, prompt length bound, draft/focus preservation across revision churn, lost-receipt reconciliation, retryable-only retry affordance, approval decisions requiring the current signed binding (and asserting `actionToken` never travels in input), a11y live region, scroll-follow behavior, scope-key teardown clearing drafts, RFW `libraryText` rendering through the fixed dictionary. Long but organized. **Verdict: retain.**

### `app/test/ui_kit/ui_display_a_test.dart` (1-21), `ui_feedback_test.dart` (1-34), `ui_nav_b_test.dart` (1-35), `ui_layout_test.dart` (1-41), `ui_inputs_a_test.dart` (1-46), `ui_nav_a_test.dart` (1-51), `ui_display_b_test.dart` (1-58)
Smoke/interaction tests per ui_kit widget family: renders text, fires `ExperienceStep` events with expected props, form-scope value capture (checkbox/switch/textarea). Shallow but proportionate to the widgets' complexity; consistent ForUI host harness. No golden tests, no theme/dark-mode or semantics assertions anywhere in ui_kit (TEST-900). **Verdict: retain.**

### `app/test/ui_kit/ui_gallery_hop_render_test.dart` (1-72)
Regression test reproducing a real layout failure (sidebar unbounded height blanking the screen); asserts `takeException()` is null and trailing content builds. Good targeted regression. **Verdict: retain.**

### `app/test/ui_kit/ui_inputs_b_test.dart` (1-130)
Registry mapping for inputs-b + slider drag and datefield ISO-normalization capture tests. The drag test depends on FSlider hit geometry (offset math) — brittle to forui upgrades (forui pinned at 0.21.3, so contained). **Verdict: retain.**

### `app/test/ui_kit/ui_kit_widgets_test.dart` (1-225)
Form controller unit tests (unmodifiable values map), screen/form-scope integration, button event payload including captured form values, brand icon rendering. **Verdict: retain.**

### `app/test/ui_kit/ui_overlays_test.dart` (1-89)
Dialog present-once guard (with a rebuild harness deliberately preserving State), open:false renders nothing, toast/sheet presentation. Thoughtful. **Verdict: retain.**

### `app/test/ui_kit/ui_registry_test.dart` (1-304)
`buildUiNode` mapping tests including case-insensitivity, unknown-type fallback to `SizedBox.shrink` (silent-drop of unknown server widgets — a deliberate lenient default worth noting), table/graphcanvas parsing and rendering, `UiSurfaceTreeRenderer` routing. **Verdict: retain.**

### `app/tool/breaker_smoke.dart` (1-25)
Hand-rolled assertion script for `ExportCircuitBreaker` (trip-at-3, permanent trip). This is a unit test living outside the test runner; the breaker has no `flutter_test` coverage (CLEAN-902). **Verdict: move** (rewrite as `test/telemetry/export_circuit_breaker_test.dart`, delete the script).

### `app/tool/challenger_m2_3_stress_test.dart` (1-345)
Demo-era "challenger" script: greps `lib/widgets/brain_canvas_2d_graph.dart` and `lib/features/neuron_constructor/neuron_constructor_view.dart` for allocation patterns — FACT: **neither file exists**; the script's first two sections can only record failures, and it exits 1. Sections 3-4 stress-test mock data structures that duplicate no production code path. Dead tooling (CLEAN-901). **Verdict: delete.**

### `app/tool/challenger_m4_stress_test.dart` (1-255)
Same genre: "replicated exactly" copies of `CatalogContractSchema`, `parseSynapses`, and wildcard logic from production files — duplicated authority that drifts silently from the real implementations; fallback asset path `UI/flutter/assets/` is stale. The regex behaviors it pins (e.g. multi-parameter emit fails to parse) belong in real unit tests against the *actual* production functions (CLEAN-901). **Verdict: delete** (port any still-valuable cases to `test/` against production code).

### `app/tool/check_ui_imports.dart` (1-73)
Boundary checker: fails if `lib/digital_brain_ui/**` imports app layers (grpc, features, telemetry, …). Real architectural value — this is the only mechanical enforcement of the UI-package boundary. Two quibbles: allowlist includes `package:material/` and `package:cupertino/` "3.41 standalone" — no such standalone packages are known to exist (FRAME-903, unverifiable via Context7 today); and the check is not wired into any CI/test entry point visible in this slice. **Verdict: retain** (wire into CI; prune speculative prefixes).

### `app/pubspec.yaml` (1-69)
App manifest. Problems, in order of severity: (a) `widgetbook: any` sits in **`dependencies`** (production) with a fully unpinned constraint, while `lib/widgetbook.dart:9` says "widgetbook is intentionally a dev_dependency" — it is not (FRAME-900). (b) FACT, by repo-wide import grep: `youtube_player_iframe`, `markdraw`, `graphic`, `desktop_drop`, `file_picker`, `cross_file`, `clock`, `shared_preferences`, and `media_kit_video` are imported nowhere in `lib/`, `test/`, or `tool/`; `media_kit_libs_video` exists only to serve the unused `media_kit_video`; the `file_picker` pin comment justifies itself via `markdraw`, which is itself unused (FRAME-901). (c) `bloc_test ^10.0.0` declared, zero bloc tests exist (TEST-902). Overlapping media stacks (media_kit + youtube_player + lottie) reduce to: media_kit init-only, youtube dead, lottie one consumer. Version pins otherwise deliberate and current-looking (grpc ^5.1.0, go_router ^17, forui pinned 0.21.3 with a documented reason). **Verdict: simplify aggressively** (delete dead deps; move widgetbook to dev_dependencies with a caret pin).

### `app/analysis_options.yaml` (1-28)
Identical stock `flutter_lints` file as the SDK's; no enabled extra rules, no strict modes (FRAME-902). **Verdict: simplify.**

### `app/devtools_options.yaml` (1-3)
Empty extensions list. Harmless tool scaffolding. **Verdict: retain.**

### `app/.gitignore` (1-60)
Standard Flutter ignore plus Windows `obj/` and agent-artifact ignores (`.superpowers/`, `sdd/`). Fine. **Verdict: retain.**

### `app/.metadata` (1-45)
Flutter tool-managed migrate metadata (generated; "should not be manually edited"). Checked in correctly. **Verdict: retain (excluded-generated).**

### `app/Flutter.proj` (1-101)
MSBuild coordination project so the Flutter client participates in the .NET solution. FACT: `SkipFlutterBuild` defaults to `true`, so `FlutterPubGet`/`FlutterBuildWeb` never run in a default build — the machinery is dead-by-default; when enabled, both `Exec` calls use `IgnoreExitCode=true`, so a failed `flutter build web` produces at most a warning and a silently stale web bundle (REL-901). Incremental Inputs/Outputs are declared but moot while skipped. `net11.0` TargetFramework on a NoTargets project is a label only. **Verdict: simplify** (either wire it truthfully — fail on nonzero exit when enabled — or delete the build targets and keep only solution membership).

### `app/pubspec.lock` (1574 lines)
**Excluded-generated** (lockfile; generator: `flutter pub get`, source of truth `pubspec.yaml`). Checked in — correct for an application. Staleness risk: refreshing it while `widgetbook: any` exists can jump widgetbook arbitrarily (see FRAME-900).

---

## Answers to subsystem-specific questions

**1. SDK public API and boundary; is the closure-injected gateway clean or leaky? Independently publishable?**
Public API = the full barrel export (11 files): tier model, probe widget, stream pump, census, throttles, and the gateway adapter. The closure injection in `perf_gateway_client.dart` is directionally clean — the SDK stays free of gRPC/proto and the app maps `PerfSample`/`PerfTierHint` to wire types. But the abstraction leaks **the other way**: `widget_census.dart` hard-codes the app's `GlowIcon` (string fallback + settable global `Type`), and `tier_throttle/throttle.dart` encodes app-specific render tuning and references app internals in comments. The adapter contract is also unspecified (completion/reconnect/error semantics live implicitly in `PerfStream`). It is *nominally* publishable (no path deps of its own, `publish_to: none`, own version) but practically tangled: it exists for exactly one app, is wired to no-op closures in production (`main.dart:32-38` — kernel perf RPCs unimplemented), and is disabled in release mode. Verdict: keep the pattern, but either implement the kernel side or shrink the package to the tier scope + throttles the app actually uses.

**2. Test quality: real coverage or thin? What is untested?**
Two-tier reality. The `runtime/` suite (~4,400 lines) is excellent, adversarial, and directly encodes the OS trust model: fail-closed session refresh, scope-epoch isolation, signed action tokens, OAuth target pinning, token-leak assertions, race hardening (stale bootstrap/refresh/receipt), reconnect/reset idempotency. The `ui_kit/` suite is shallow smoke coverage (render + event payload), with one good layout regression test. **Untested**: the entire SDK package (no test dir at all); telemetry (OTLP exporters, gRPC interceptor; circuit breaker covered only by a tool script); `rfw_host` (the RFW dictionary/library, ino-catalog parsing); `digital_brain_ui` (glass/glow/adaptive); `features/`, `shell/`, `router.dart`; the external identity (openid_client) redirect flow; non-web endpoint resolution. `flutter_test` usage is idiomatic (pump/pumpUntil patterns, fakes over mocks). `bloc_test` is declared and never used — there are no bloc tests despite `flutter_bloc` + a `TelemetryBlocObserver` (verified against pub.dev: bloc 9.2.0 / bloc_test 10.x pairing is fine; the problem is non-use, not incompatibility).

**3. pubspec dependency hygiene?**
Bad in specific, fixable ways: `widgetbook: any` unpinned **and** in production `dependencies` (FRAME-900); nine dead packages (`youtube_player_iframe`, `markdraw`, `graphic`, `desktop_drop`, `file_picker`, `cross_file`, `clock`, `shared_preferences`, `media_kit_video` + transitively-motivated `media_kit_libs_video`) (FRAME-901); a pin comment (`file_picker` ← `markdraw`) justified by a dead package; `bloc_test` unused. The single path dep (`digital_brain_sdk_flutter`) is appropriate. No git deps. Remaining pins look deliberate (forui 0.21.3 with documented Flutter-version reason).

**4. analysis_options lint strictness?**
Both files are the untouched `flutter_lints` template — nothing disabled, but nothing added: no `very_good_analysis`-grade rules, no `language: strict-casts/strict-raw-types/strict-inference`, no `public_member_api_docs` for the SDK. For a client that enforces security invariants in code, the analyzer is doing the minimum (FRAME-902).

**5. app/tool scripts — what, safe, belong?**
`check_ui_imports.dart`: safe (read-only), valuable, belongs — should run in CI. `breaker_smoke.dart`: safe but belongs in `test/`. `challenger_m2_3_stress_test.dart`: broken (targets two deleted files, always exits 1) — delete. `challenger_m4_stress_test.dart`: safe but duplicates production logic by copy-paste and tests the copies — delete or port to real tests. None are dangerous (no network, no writes), but two of four are dead demo-era weight.

---

## Findings

### ARCH-900: Perf SDK gateway path is a production no-op end to end
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `app/lib/main.dart:32-38` — "The kernel does not implement the perf RPCs yet"; `pushSamples: (samples) => samples.drain<void>()`, `watchHints: (_) => const Stream<PerfTierHint>.empty()`. `app/packages/digital_brain_sdk_flutter/lib/src/perf/perf_probe.dart:44` — probe fully disabled in release mode.
- **Current behavior**: Samples are collected (debug/profile only), pushed into a stream that is drained to nowhere; tier hints never arrive; `PerfTier` stays `smooth` forever. (FACT)
- **Why it matters**: (INFERENCE) The package's entire value proposition — adaptive quality tiers driven by server analysis — does not exist; the code is speculative scaffolding carrying maintenance cost and a false sense of instrumentation.
- **OS/product consequence**: No OS primitive is broken, but the "self-instrumenting client" story is currently fictional; perf regressions in production are invisible.
- **Recommendation**: (PROPOSAL) Decide: implement the kernel perf RPCs and wire a real adapter, or shrink the SDK to `PerfTierScope` + local-only heuristics and delete the gateway/stream layer until needed.
- **Deletion/simplification opportunity**: yes — `perf_stream.dart`, `perf_gateway_client.dart`, `perf_tier_hint.dart` deletable today with zero behavior change beyond removing idle timers.
- **Dependencies**: PERF-900, CLEAN-903, TEST-901.
- **Tests/measurements required**: If kept: an integration test with a fake gateway proving samples flow and hints downgrade the tier.
- **Effort**: M
- **Migration/rollback concern**: none (no persisted state).

### ARCH-901: SDK leaks app internals (GlowIcon name, app render tuning) — reverse-direction abstraction leak
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `app/packages/digital_brain_sdk_flutter/lib/src/perf/widget_census.dart:17,36` — `static Type? glowIconType` global + `type.toString() == 'GlowIcon'` string fallback; `app/packages/digital_brain_sdk_flutter/lib/src/tier_throttle/throttle.dart:17-18` — comment references `SynapseStreamFeed.publish` / `_LiveScreenState._onTick`; app-specific dot counts/blur sigmas hard-coded.
- **Current behavior**: The SDK counts one specific app widget by name and hosts the app's visual tuning constants. (FACT)
- **Why it matters**: (INFERENCE) The dependency arrow points the wrong way: the "reusable" package embeds knowledge of one consumer, so it can never serve a second client, and renames in the app silently break census counts (the string fallback fails open to 0 matches).
- **OS/product consequence**: Undermines the SDK-as-boundary story; packs/other clients could not reuse this package.
- **Recommendation**: (PROPOSAL) Replace `glowIconType`/string fallback with a registered `Set<Type>` or predicate passed to `PerfProbe`; move `throttle.dart` into `app/lib/digital_brain_ui/`.
- **Deletion/simplification opportunity**: yes — string fallback branch deletable once registration is required.
- **Dependencies**: ARCH-900.
- **Tests/measurements required**: unit test for census with registered predicate.
- **Effort**: S
- **Migration/rollback concern**: none.

### ARCH-902: Package named "SDK" is a perf-only helper, not a client SDK
- **Severity**: Note
- **Confidence**: High
- **Evidence**: `app/packages/digital_brain_sdk_flutter/pubspec.yaml:2` — "Self-instrumenting perf SDK"; barrel export contains only perf/tier/gateway types.
- **Current behavior**: The one thing named `digital_brain_sdk_flutter` contains no session, transport, protocol, or surface code — all of that lives in `app/lib/runtime/`. (FACT)
- **Why it matters**: (INFERENCE) Misleading name invites future contributors to grow it into a grab-bag or to look here for the actual client contract.
- **OS/product consequence**: Naming/architecture clarity only.
- **Recommendation**: (PROPOSAL) Rename to `digital_brain_perf_flutter`, or make it the real SDK by moving `app/lib/runtime/` protocol code into it.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: ARCH-900.
- **Tests/measurements required**: n/a.
- **Effort**: S
- **Migration/rollback concern**: import churn only.

### PROD-900: `rebuildsPerSecond` actually reports frames per second
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `app/packages/digital_brain_sdk_flutter/lib/src/perf/perf_probe.dart:58-63` — `for (final t in timings) { _frameTimes.add(t.totalSpan); _rebuildAccumulator++; }`; `:93-94` — `rebuildsPerSecond: (_rebuildAccumulator * 1000) ~/ ...`.
- **Current behavior**: `_rebuildAccumulator` increments once per `FrameTiming` entry (one per rendered frame), so the published `rebuildsPerSecond` equals `frameCount` normalized per second — it measures nothing about widget rebuilds. (FACT)
- **Why it matters**: (INFERENCE) A mislabeled metric is worse than a missing one: any future server-side tiering logic keyed on "rebuild storms" would silently key on FPS instead.
- **OS/product consequence**: Corrupts the telemetry contract the perf rail would be built on.
- **Recommendation**: (PROPOSAL) Either rename the field to `framesPerSecond` (and drop the duplicate `frameCount`) or count real rebuilds (e.g. a `WidgetsBindingObserver`/build-hook counter).
- **Deletion/simplification opportunity**: yes — field is redundant with `frameCount` today.
- **Dependencies**: ARCH-900.
- **Tests/measurements required**: unit test asserting semantics of whichever metric is kept.
- **Effort**: S
- **Migration/rollback concern**: wire-schema rename must be coordinated if kernel ever implements the RPC.

### PROD-901: Jank threshold hard-coded at 16 ms; `PerfTierThresholds` unused
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `app/packages/digital_brain_sdk_flutter/lib/src/perf/perf_probe.dart:72` — `m > 16.0`; `perf_tier_thresholds.dart:1-5` referenced nowhere.
- **Current behavior**: Jank% assumes a 60 Hz frame budget on all displays; the thresholds type that would parameterize this is dead. (FACT)
- **Why it matters**: (INFERENCE) On 120 Hz desktop/mobile displays, frames from 8.4-16 ms are janky but counted smooth; jankPct systematically under-reports.
- **OS/product consequence**: Wrong tier decisions if the pipeline ever goes live.
- **Recommendation**: (PROPOSAL) Derive budget from `SchedulerBinding.instance.window`/display refresh (or accept `PerfTierThresholds` in `PerfProbe`).
- **Deletion/simplification opportunity**: yes — delete `perf_tier_thresholds.dart` if not adopted (CLEAN-903).
- **Dependencies**: PROD-900, CLEAN-903.
- **Tests/measurements required**: unit test with synthetic timings at 120 Hz budget.
- **Effort**: S
- **Migration/rollback concern**: none.

### SEC-900: Real deployment hostnames embedded in test code
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `app/test/grpc/endpoint_test.dart:9-17` — `gentle-sand-0f4081803.7.azurestaticapps.net`, `digitalbrain-jobs.agreeablefield-fcde995f.westeurope.azurecontainerapps.io`.
- **Current behavior**: Tests hard-code what appear to be live Azure Static Web App and Container App endpoints of the actual deployment. (FACT)
- **Why it matters**: (INFERENCE) Leaks environment topology to anyone with repo access and couples tests to infrastructure that will be rotated.
- **OS/product consequence**: Information disclosure hygiene at the trust boundary; no direct exploit.
- **Recommendation**: (PROPOSAL) Use `example.com`-style synthetic hosts.
- **Deletion/simplification opportunity**: yes — trivial substitution.
- **Dependencies**: none.
- **Tests/measurements required**: existing tests still pass.
- **Effort**: S
- **Migration/rollback concern**: none.

### SEC-901: `ino-catalog.json` models a raw `token` as an ordinary catalog field
- **Severity**: Note
- **Confidence**: Medium
- **Evidence**: `app/assets/ino-catalog.json:2-6` — `"Fqn": "DB.Google.Auth", "Fields": ["token", "email", "scopes"]`.
- **Current behavior**: The Creator prompt autocomplete catalog presents an auth synapse whose schema carries a bearer token like any other field. (FACT)
- **Why it matters**: (INFERENCE) Normalizes tokens-as-message-fields in the authoring UX, contradicting the least-privilege/fail-closed auth rail (tokens should be capability references, not payload).
- **OS/product consequence**: Erodes the auth trust boundary at the design-language level.
- **Recommendation**: (PROPOSAL) When the catalog moves server-side, model credentials as opaque grant references.
- **Deletion/simplification opportunity**: yes — prune with CLEAN-904.
- **Dependencies**: CLEAN-904.
- **Tests/measurements required**: n/a.
- **Effort**: S
- **Migration/rollback concern**: none.

### PERF-900: PerfStream retry pump idles forever; backoff cap overshoots and never resets
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `app/packages/digital_brain_sdk_flutter/lib/src/perf/perf_stream.dart:54-67` — watch loop retries an immediately-completing empty stream for app lifetime; `:69-74` — `backoff < _maxRetryDelay ? backoff * 2 : backoff` yields steady-state 8 s despite `_maxRetryDelay = 5s`; backoff is never reset after a successful connection.
- **Current behavior**: With the current no-op wiring, a timer fires every ≤8 s forever doing nothing; if a real gateway existed, a long-lived healthy connection that drops would retry at whatever inflated backoff was reached earlier. (FACT)
- **Why it matters**: (INFERENCE) Wasted wakeups (battery/web) today; sluggish reconnect after transient blips tomorrow.
- **OS/product consequence**: Minor client resource waste.
- **Recommendation**: (PROPOSAL) Reset backoff to `_initialRetryDelay` after a successful pump iteration; clamp with `min(backoff * 2, _maxRetryDelay)`; don't start pumps when the gateway is a known no-op.
- **Deletion/simplification opportunity**: yes — via ARCH-900 deletion.
- **Dependencies**: ARCH-900.
- **Tests/measurements required**: unit test asserting backoff sequence and reset.
- **Effort**: S
- **Migration/rollback concern**: none.

### PERF-901: Widget census walks the full element tree on the UI isolate
- **Severity**: Note
- **Confidence**: High
- **Evidence**: `app/packages/digital_brain_sdk_flutter/lib/src/perf/widget_census.dart:20-45` — iterative DFS over every `Element`; `perf_probe.dart:25-26` documents subsampling (0.2 Hz default).
- **Current behavior**: Bounded, debug/profile-only, subsampled. (FACT)
- **Why it matters**: (INFERENCE) Acceptable now; would need re-evaluation before enabling in release.
- **OS/product consequence**: none today.
- **Recommendation**: (PROPOSAL) Keep subsampling; consider count caps if release-enabled.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: ARCH-900.
- **Tests/measurements required**: profile-mode timing if release-enabled.
- **Effort**: S
- **Migration/rollback concern**: none.

### REL-900: PerfStream swallows all pump errors with bare `catch (_)`
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `app/packages/digital_brain_sdk_flutter/lib/src/perf/perf_stream.dart:47-49,62-64` — `catch (_) { // Retry below. }`.
- **Current behavior**: Any error — including programming errors like `TypeError` in an adapter — is silently retried forever; samples emitted during backoff are dropped (broadcast stream, no buffer). (FACT)
- **Why it matters**: (INFERENCE) A permanently broken adapter is indistinguishable from a healthy idle one; telemetry loss is invisible.
- **OS/product consequence**: Perf observability channel can fail silently.
- **Recommendation**: (PROPOSAL) `debugPrint`/logger hook on failure, and rethrow non-transport errors (`on Exception catch`).
- **Deletion/simplification opportunity**: no.
- **Dependencies**: ARCH-900.
- **Tests/measurements required**: unit test: adapter throwing `Error` surfaces; throwing `Exception` retries.
- **Effort**: S
- **Migration/rollback concern**: none.

### REL-901: Flutter.proj build targets are skipped by default and swallow exit codes when enabled
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `app/Flutter.proj:21` — `<SkipFlutterBuild ...>true</SkipFlutterBuild>` default; `:74-79,92-95` — `IgnoreExitCode="true"` on both `flutter pub get` and `flutter build web`.
- **Current behavior**: Solution builds never run pub get / web build unless explicitly opted in; when opted in, failures degrade to warnings and stale `build/web` output can be consumed by the gateway/E2E. (FACT)
- **Why it matters**: (INFERENCE) "Participates in dotnet build" (file header) is not true by default, and when true it cannot fail — partial-write ambiguity for the web bundle consumers.
- **OS/product consequence**: E2E runs can silently test a stale client.
- **Recommendation**: (PROPOSAL) When `SkipFlutterBuild=false`, fail the build on nonzero exit; or delete the targets and keep only solution membership (delete-first).
- **Deletion/simplification opportunity**: yes — ~50 lines of dead-by-default MSBuild.
- **Dependencies**: none.
- **Tests/measurements required**: build with induced flutter failure must fail.
- **Effort**: S
- **Migration/rollback concern**: CI opt-in flags may rely on lenient behavior — check pipelines first.

### FRAME-900: `widgetbook: any` — unpinned dev tool declared as a production dependency
- **Severity**: High
- **Confidence**: High
- **Evidence**: `app/pubspec.yaml:54` — `widgetbook: any` under `dependencies`; `app/lib/widgetbook.dart:9` — comment claims "widgetbook is intentionally a dev_dependency".
- **Current behavior**: Widgetbook (a dev catalog tool) resolves to *any* published version on the next `pub get`/`pub upgrade` without lock, and sits in the production dependency graph of the client that renders authenticated surfaces. (FACT)
- **Why it matters**: (INFERENCE) `any` defeats reproducibility and widens supply-chain exposure (any future widgetbook release, including a compromised one, is acceptable to the resolver); the dependencies/dev_dependencies misplacement contradicts the in-repo documentation and ships dev tooling constraints into release resolution.
- **OS/product consequence**: Supply-chain and build-reproducibility risk on the primary user-facing client.
- **Recommendation**: (PROPOSAL) Move to `dev_dependencies` with a caret pin (e.g. `^3.x` current major); verify `lib/widgetbook.dart` is excluded from release entry points (it is a separate `-t` target).
- **Deletion/simplification opportunity**: yes — if the widgetbook catalog is unused, delete both the dep and `lib/widgetbook.dart`.
- **Dependencies**: FRAME-901.
- **Tests/measurements required**: `flutter pub deps` shows widgetbook absent from release graph; app builds.
- **Effort**: S
- **Migration/rollback concern**: pubspec.lock refresh.

### FRAME-901: Nine dead dependencies (plus a pin justified by a dead package)
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `app/pubspec.yaml:23-49`; repo-wide import grep over `lib/`, `test/`, `tool/` finds zero imports of `youtube_player_iframe`, `markdraw`, `graphic`, `desktop_drop`, `file_picker`, `cross_file`, `clock`, `shared_preferences`, `media_kit_video`; `media_kit_libs_video` exists to serve the unused `media_kit_video`; `pubspec.yaml:28` pins `file_picker` "because markdraw ^0.2.0 constrains" (markdraw unused); pubspec comments (`:25`, `:38-39`) describe attach/persistence features that have no code.
- **Current behavior**: The dependency graph, binary size, and native plugin registration include packages the code never touches. (FACT)
- **Why it matters**: (INFERENCE) Each dead package is supply-chain surface, resolution-conflict surface (file_picker pin already fossilized), and misleading documentation of intent.
- **OS/product consequence**: Bloats the client, slows resolution, obscures what the product actually does.
- **Recommendation**: (PROPOSAL) Delete all nine (+ `media_kit_libs_video` if `media_kit_video` goes); re-add with the feature that uses them. Re-evaluate `media_kit` itself (only `ensureInitialized()` in `main.dart`).
- **Deletion/simplification opportunity**: yes — the point.
- **Dependencies**: FRAME-900, TEST-902.
- **Tests/measurements required**: `flutter analyze` + full test run + web/windows build succeed after removal.
- **Effort**: S
- **Migration/rollback concern**: pubspec.lock churn only.

### FRAME-902: Analyzer config is the untouched template in both packages
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `app/analysis_options.yaml:1-28` and `app/packages/digital_brain_sdk_flutter/analysis_options.yaml:1-28` — identical stock `flutter_lints` include, empty rules block.
- **Current behavior**: Only the base recommended lints run; no strict-casts/strict-inference/strict-raw-types, no additional rules. (FACT)
- **Why it matters**: (INFERENCE) A client enforcing security invariants (token handling, scope checks) benefits materially from stricter static analysis; the current config catches the minimum.
- **OS/product consequence**: Weaker automated defense on the trust-boundary code.
- **Recommendation**: (PROPOSAL) Enable `language: strict-casts, strict-inference, strict-raw-types` and a curated rule set (or `very_good_analysis`); share one config.
- **Deletion/simplification opportunity**: yes — delete the duplicated comment boilerplate.
- **Dependencies**: none.
- **Tests/measurements required**: `flutter analyze` clean after adoption.
- **Effort**: M (fixing newly surfaced lints)
- **Migration/rollback concern**: none.

### FRAME-903: Import-boundary allowlist includes apparently nonexistent packages
- **Severity**: Low
- **Confidence**: Medium (Context7 unavailable to verify current Flutter packaging; no standalone `material`/`cupertino` pub packages are known)
- **Evidence**: `app/tool/check_ui_imports.dart:13-14` — `'package:material/'`, `'package:cupertino/'` with comment "3.41 standalone".
- **Current behavior**: The allowlist admits import prefixes that (as far as verifiable) no real package provides. (FACT that they are allowed; the nonexistence claim is best-effort.)
- **Why it matters**: (INFERENCE) Harmless today, but speculative entries in a security/architecture gate erode its credibility and could mask a future typo-squatting package name.
- **OS/product consequence**: Slight weakening of the only mechanical UI-boundary enforcement.
- **Recommendation**: (PROPOSAL) Remove until such packages exist; also wire this script into CI (nothing in this slice runs it).
- **Deletion/simplification opportunity**: yes — two lines.
- **Dependencies**: none.
- **Tests/measurements required**: script still passes on `lib/digital_brain_ui`.
- **Effort**: S
- **Migration/rollback concern**: none.

### CLEAN-900: Dead bundled assets, including a 13.3 MB Lottie binary
- **Severity**: High
- **Confidence**: High
- **Evidence**: `app/assets/lottie/orbit.lottie` (13,349,278 bytes), `app/assets/rfw/activity_overlay.rfwtxt` (1,408 lines), `app/assets/rfw/sample_neuron.rfwtxt` — bundled via `app/pubspec.yaml:64-67`; repo-wide grep (Dart + C# + config) finds zero references to any of the three; the runtime renders server-sent `libraryText`, not asset rfwtxt (`app/test/runtime/surface_view_test.dart:1140-1161`). `activity_overlay.rfwtxt:165,875-876,911-912,988-989` hard-code fake metrics ("42 / 100", "TOTAL BUNDLES 16", "LICENSES ISSUED 94", "COMMISSION RATE 20%") as UI copy.
- **Current behavior**: Every web download and desktop install ships ~13.4 MB of assets no code loads; the RFW files are demo theater with fabricated metrics baked in. (FACT)
- **Why it matters**: (INFERENCE) 13 MB is a material initial-load tax on the web client (a main user journey); the demo files invite accidental resurrection of fake-data UI.
- **OS/product consequence**: Degrades the primary client load path; contradicts "delete trash aggressively".
- **Recommendation**: (PROPOSAL) Delete all three assets and the `assets/rfw/`, `assets/lottie/` pubspec entries (keep `ino-catalog.json`, shader).
- **Deletion/simplification opportunity**: yes — ~13.4 MB + 1,462 lines.
- **Dependencies**: CLEAN-904.
- **Tests/measurements required**: `flutter build web` succeeds; bundle size delta measured.
- **Effort**: S
- **Migration/rollback concern**: confirm no kernel-side surface names these assets (repo grep found none).

### CLEAN-901: "Challenger" tool scripts are dead — one targets deleted files and always fails
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `app/tool/challenger_m2_3_stress_test.dart:61-62,124-127` reads `lib/widgets/brain_canvas_2d_graph.dart` and `lib/features/neuron_constructor/neuron_constructor_view.dart` — FACT: neither file exists in `app/lib`. `app/tool/challenger_m4_stress_test.dart:4,25,63` — logic "replicated exactly from" production files (duplicated authority); `:102` stale fallback path `UI/flutter/assets/`.
- **Current behavior**: m2_3 exits 1 unconditionally (grep targets missing); m4 tests hand-copied duplicates of production parsing that can drift silently from the real code. (FACT)
- **Why it matters**: (INFERENCE) Dead/dishonest tooling: a script that always fails trains people to ignore red; tests of copies validate nothing about the product.
- **OS/product consequence**: Noise in the repo, false confidence, reading waste.
- **Recommendation**: (PROPOSAL) Delete both. Port m4's regex edge cases (multiline emit, multi-parameter non-match, dedup) as unit tests against the real `parseSynapses`/wildcard functions in `lib/rfw_host/`.
- **Deletion/simplification opportunity**: yes — 600 lines.
- **Dependencies**: TEST-900.
- **Tests/measurements required**: new unit tests green against production functions.
- **Effort**: S
- **Migration/rollback concern**: none.

### CLEAN-902: Circuit-breaker coverage lives in a tool script instead of a test
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `app/tool/breaker_smoke.dart:1-25` — throw-based assertions for `ExportCircuitBreaker`; no `test/telemetry/` exists.
- **Current behavior**: The breaker's trip/permanence semantics are checked only by a manually-run script outside `dotnet test`/`flutter test`. (FACT)
- **Why it matters**: (INFERENCE) The one reliability guard on telemetry export is not exercised by any automated run.
- **OS/product consequence**: Telemetry-export failure containment is unverified in CI.
- **Recommendation**: (PROPOSAL) Rewrite as `app/test/telemetry/export_circuit_breaker_test.dart`; delete the script.
- **Deletion/simplification opportunity**: yes.
- **Dependencies**: TEST-900.
- **Tests/measurements required**: the new test itself.
- **Effort**: S
- **Migration/rollback concern**: none.

### CLEAN-903: `PerfTierThresholds` is exported dead code
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `app/packages/digital_brain_sdk_flutter/lib/src/perf/perf_tier_thresholds.dart:1-5`; repo-wide grep shows zero references outside its own file and the barrel export.
- **Current behavior**: Public API surface with no consumer. (FACT)
- **Why it matters**: (INFERENCE) Speculative API invites divergent use; it also marks the spot where PROD-901's hard-coded threshold should have been parameterized.
- **OS/product consequence**: none direct.
- **Recommendation**: (PROPOSAL) Delete, or adopt in `PerfProbe` (preferred with PROD-901).
- **Deletion/simplification opportunity**: yes.
- **Dependencies**: PROD-901.
- **Tests/measurements required**: build passes.
- **Effort**: S
- **Migration/rollback concern**: none.

### CLEAN-904: Demo entries and undocumented magic numbers in `ino-catalog.json`
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `app/assets/ino-catalog.json:38-46` — `Acme.Submit`, `Acme.Worker`; `Kind` 0/1/2 encoding documented only inside the (to-be-deleted) `app/tool/challenger_m4_stress_test.dart:7`.
- **Current behavior**: Production autocomplete catalog ships demo vendor entries; the Kind encoding has no in-asset or in-consumer documentation. (FACT)
- **Why it matters**: (INFERENCE) Demo FQNs surface in the real Creator UX; losing the only Kind documentation with CLEAN-901's deletion makes the asset opaque.
- **OS/product consequence**: Minor authoring-UX pollution; contract-catalog authority belongs server-side.
- **Recommendation**: (PROPOSAL) Remove Acme entries; document Kind values in the consuming Dart (`digitalbrain_rfw_library.dart`) or replace ints with strings.
- **Deletion/simplification opportunity**: yes.
- **Dependencies**: SEC-901, CLEAN-901.
- **Tests/measurements required**: rfw_host catalog parse test (currently none — TEST-900).
- **Effort**: S
- **Migration/rollback concern**: none.

### TEST-900: Coverage is siloed — runtime excellent, everything else untested
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `app/test/` contains only `grpc/` (1 file), `runtime/` (8 files), `ui_kit/` (12 files), `runtime_test.dart`. No tests exist for `lib/telemetry/` (OTLP exporters, gRPC interceptor), `lib/rfw_host/` (catalog parsing at `digitalbrain_rfw_library.dart:483`, palette primitives), `lib/digital_brain_ui/`, `lib/features/`, `lib/shell/`, `lib/router.dart`, or the external-identity (openid_client) flow beyond config parsing.
- **Current behavior**: The auth/session/transport/protocol core is deeply tested (fail-closed invariants, token-leak assertions, races); the rendering/telemetry/feature layers have zero automated coverage. (FACT)
- **Why it matters**: (INFERENCE) The tested core is exactly the OS trust boundary — good prioritization — but RFW rendering (server-driven UI, a mutation-adjacent surface) and telemetry (the observability rail) can regress invisibly.
- **OS/product consequence**: Server-driven UI rendering errors and telemetry loss ship undetected.
- **Recommendation**: (PROPOSAL) Next increments in order: rfw_host catalog/dictionary tests, telemetry exporter + breaker tests (with CLEAN-902), one router/shell smoke test.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: CLEAN-901, CLEAN-902, TEST-901.
- **Tests/measurements required**: coverage report per directory to track the gap.
- **Effort**: L
- **Migration/rollback concern**: none.

### TEST-901: SDK package has no tests at all
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `app/packages/digital_brain_sdk_flutter/` contains only `lib/`, `pubspec.yaml`, `analysis_options.yaml`, `.gitignore` — no `test/` directory; no test framework in dev_dependencies.
- **Current behavior**: PerfStream retry/backoff/dispose, PerfProbe percentile math, WidgetCensus counting, and tier mapping are unverified. (FACT)
- **Why it matters**: (INFERENCE) PROD-900 (wrong metric) and PERF-900 (backoff overshoot) exist precisely because nothing pins this code's behavior.
- **OS/product consequence**: The perf rail's client half has no safety net.
- **Recommendation**: (PROPOSAL) Add `flutter_test` + unit tests for stream lifecycle, percentile/jank math with synthetic `FrameTiming`-like inputs, census with a registered type.
- **Deletion/simplification opportunity**: partially — shrinking the SDK (ARCH-900) shrinks the test obligation.
- **Dependencies**: ARCH-900, PROD-900, PERF-900.
- **Tests/measurements required**: the tests themselves; `flutter test` wired for the package (root `dotnet test` does not run Dart tests).
- **Effort**: M
- **Migration/rollback concern**: none.

### TEST-902: `bloc_test` declared but never used
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `app/pubspec.yaml:59` — `bloc_test: ^10.0.0`; grep finds no `bloc_test` import anywhere in `test/`. `flutter_bloc` is used (`lib/main.dart:7,30`, `lib/telemetry/bloc_observer.dart`) but no bloc has tests.
- **Current behavior**: Dead dev dependency; the `TelemetryBlocObserver` is untested. (FACT — pub.dev check confirms bloc_test 10.x/bloc 9.x pairing is valid, so this is non-use, not incompatibility.)
- **Why it matters**: (INFERENCE) Either blocs matter (then test them) or the app barely uses bloc (then question `flutter_bloc` itself — only an observer registration was found).
- **OS/product consequence**: none direct.
- **Recommendation**: (PROPOSAL) Delete `bloc_test`, or add observer tests; audit whether `flutter_bloc` earns its place.
- **Deletion/simplification opportunity**: yes.
- **Dependencies**: FRAME-901.
- **Tests/measurements required**: resolution + build after removal.
- **Effort**: S
- **Migration/rollback concern**: none.
