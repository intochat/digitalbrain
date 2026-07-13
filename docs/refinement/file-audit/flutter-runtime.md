# Subsystem Audit: flutter-runtime

- **Subsystem**: Flutter client runtime — session/auth, gRPC transport, surface feed, telemetry, shell, features, widgets
- **Scope**: `app/lib` root files, `app/lib/runtime/**`, `app/lib/grpc/**`, `app/lib/telemetry/**`, `app/lib/shell/**`, `app/lib/features/**`, `app/lib/widgets/**` (52 files; 7 generated protobuf outputs excluded from line audit)
- **Commit**: `72400e3ebbec27e17af4ae6b5b2c4158c2797fa4` (branch `docs/refinement-audit`)
- **Date**: 2026-07-13
- **Versions in scope** (from `app/pubspec.yaml`): Dart ^3.11.0, Flutter >=3.41.0, grpc ^5.1.0, protobuf ^6.0.0, flutter_bloc ^9.1.1, go_router ^17.2.0, openid_client ^0.4.10+1, opentelemetry ^0.18.11, http ^1.3.0, record ^6.1.2, shared_preferences ^2.5.5

## Subsystem overview

The Flutter client contains **two client rails**:

1. **The v2 runtime rail** (`app/lib/runtime/**` + `app/lib/grpc/ui.pb*.dart`): `RuntimeShell` → `RuntimeSessionOwner` → `RuntimeController` → `GrpcUiTransport` over the `DigitalBrainV2Ui` gRPC service. It authenticates via a bootstrap secret (desktop, injected by Aspire env) or an OIDC id_token (web), holds session credentials **in memory only**, streams a server-driven "surface feed" with monotonic sequence numbers, decodes surfaces through a defensive JSON protocol layer (`surface_protocol.dart`), renders them (`SurfaceView` / `InoConversationView` / RFW), and submits typed actions with idempotency receipts. This rail is coherent, defensive, and well tested (~3.7k test lines).
2. **A legacy v1 gateway rail** (`app/lib/grpc/digitalbrain.pb*.dart`, `grpc_channel.dart`, `endpoint.dart`, `shell/digitalbrain_client_scope.dart`, `telemetry/grpc_interceptor.dart` identity headers, `features/brain/voice_input.dart`, `runtime/buses/**`): plumbing for the old `DigitalBrainGateway` synapse API. **`DigitalBrainClientScope` is never mounted anywhere in the widget tree**, so `DigitalBrainClientScope.of(context)` always returns null and every legacy RFW card path silently no-ops. This rail is effectively orphaned scaffolding kept alive only by `rfw_host` imports.

Telemetry (`app/lib/telemetry/**`) is desktop-only (`main.dart` guards `kIsWeb`), exports traces via the opentelemetry SDK and hand-rolls OTLP/JSON log and metric exporters (the Dart otel package has logs "unimplemented" and metrics in alpha — verified against pub.dev docs; Context7 was quota-exhausted during this audit, noted as a documentation-tooling gap).

Boundary posture: the runtime rail treats every server payload as untrusted (bounded strings, allow-listed keys, forbidden-sensitive-key rejection, scope/audience checks against the signed session identity) and treats the client as unable to assert identity (identity only ever arrives in a signed session reply). This matches the OS model. The legacy rail violates it (client-asserted `x-brain-id` header) but is dead.

## Per-file review

### `app/lib/runtime/session_state.dart` (reviewed 1-299)
- **Purpose**: session status machine, server-derived identity/credentials value types, refresh single-flight.
- **Layer**: runtime auth core; no Flutter imports. Callers: `RuntimeController`, `GrpcUiTransport` (implements `SessionTransport`), tests.
- **Correctness**: strong. Generation counter (`_bootstrapGeneration`) invalidates stale bootstraps; `_bundleVersion` + `identical` checks prevent a completed refresh from resurrecting a replaced bundle; refresh is single-flighted per bundle version; identity change across refresh is rejected (`ProtocolException`, session_state.dart:211-215); `_validate` fail-closes on empty fields and non-monotonic expiries.
- **Security**: `SessionIdentity`/`SessionCredentials`/`SessionBundle` all have redacted `toString` — tokens cannot leak via logs/exceptions/test failures. Refresh token never leaves the class except as a typed transport argument. Nothing persisted.
- **Tests**: `app/test/runtime/session_state_test.dart` (250 lines).
- **Verdict**: retain. Exemplary for the OS trust model.

### `app/lib/runtime/runtime_errors.dart` (reviewed 1-48)
- Typed transport error taxonomy with `safeMessage` (deliberately no server text pass-through) and `isTerminal` classification. `toString() => safeMessage` guarantees any accidental print is safe. Verdict: retain.

### `app/lib/runtime/feed_state.dart` (reviewed 1-219)
- **Purpose**: transport-facing feed contracts + `FeedController` (sequence tracking, scope enforcement, surface cache).
- **Correctness**: monotonic sequence with gap → `FeedReset('sequence-gap')` and `needsReset` latch; duplicate-sequence and stale-revision events downgraded to `FeedDuplicate`; `applyServerReset` validates snapshot scope, freshness, non-duplication, and snapshot-sequence ≤ resume-sequence.
- **Security**: `_demandScope` fail-closes: rejects surfaces outside the signed tenant/workspace and audience mismatches (`principal` id must equal principalId, `workspace` id must equal workspaceId, `public` id must be empty; unknown kinds rejected). This is the client-side tenant-isolation check the OS model requires.
- **Note**: `acknowledge()` only validates and stores nothing — it is an assertion hook, slightly misleading name.
- **Verdict**: retain.

### `app/lib/runtime/protocol/surface_protocol.dart` (reviewed 1-1246)
- **Purpose**: defensive decoder for the v2 surface envelope: bounded strings, positive ints, ISO timestamps, capability negotiation, payload kinds (`widgetTree`, `rfw`, `native`/`inoConversation`), typed action refs.
- **Security**: `_deepCopyJson` rejects forbidden sensitive keys (`accesstoken`, `refreshtoken`, `clientsecret`, `tenantid`, ... normalized) anywhere in payload data, caps nesting at 64, produces unmodifiable copies. `InoConversationAction` only permits `openUrl` targets rewritten onto the trusted runtime origin with an allow-list of exactly `/oauth/start/google` and `/oauth/start/salesforce` plus a strictly-bounded flow reference (`_isBoundedFlowReference`); arbitrary server-driven URLs cannot be launched. `contentHash` shape validated (though not recomputed client-side — integrity is TLS's job; note only). Approval invariants enforced in the decoder (`approvalId` required iff `awaitingApproval`; legacy operations cannot carry approval authority).
- **Concern**: hand-mapping of three near-identical state enums (`TurnState`/`OperationState`/`OperationPhase`) is verbose but explicit; acceptable.
- **Tests**: `surface_protocol_test.dart` (449 lines).
- **Verdict**: retain. This file is the client's trust boundary and takes it seriously.

### `app/lib/runtime/runtime.dart` (reviewed 1-453)
- **Purpose**: `RuntimeController` — owns auth, the feed connect/reconnect loop, ack, action submission; single async control path so a late `onDone` can't overwrite a stream error.
- **Correctness**: generation counters for loop (`_generation`) and auth (`_authenticationGeneration`); `_scopeEpoch` invalidates in-flight action submissions across scope changes (checked before *and* after the transport call, runtime.dart:355-367); gap-reset path forces a snapshot resume (`_forceSnapshot` → `afterSequence: 0`); auth errors trigger one forced refresh then fail-closed to `awaitingSignIn`.
- **Issues**: reconnect loop can skip backoff indefinitely when `reconnectImmediately` repeats (REL-700); one ack RPC per accepted event (PERF-700); `stop()` sets `RuntimeStatus.stopped` even when a terminal error was recorded — benign because dispose path only.
- **Tests**: `runtime_controller_test.dart` (963 lines).
- **Verdict**: retain.

### `app/lib/runtime/grpc_ui_transport.dart` (reviewed 1-563)
- **Purpose**: gRPC implementation of `UiTransport` + `ExternalSessionTransport` over `DigitalBrainV2UiClient`, with a test seam (`GrpcClientPort`).
- **Transport security**: `connect` **requires HTTPS** and throws otherwise (grpc_ui_transport.dart:66-73); `GrpcOrGrpcWebClientChannel.toSingleEndpoint(transportSecure: true)` → TLS on native, https gRPC-web on web (grpc 5.x API confirmed via pub.dev; Context7 unavailable). `isTimelineLoggingEnabled = false` is explicitly set because grpc-dart's timeline profiler would capture call metadata containing the signed session — a thoughtful, documented mitigation.
- **Auth metadata**: every authenticated call sends `x-v2-session` + `x-v2-audience`; empty access token fail-closes (`AuthenticationException`, line 318). Session bootstrap/refresh/logout send audience-only metadata. External bootstrap sends the OIDC id_token as `authorization: Bearer` after strict local shape validation (`_validateIdentityToken`, 3-part JWT regex, 32..8185 chars).
- **Error mapping**: `_safeTransportError` maps every `GrpcError` to a constant safe message — no server-controlled text reaches the UI or logs. `resourceExhausted` mapped to `invalidArgument` ("size limit"), which is terminal — deliberate fail-closed choice.
- **Input hygiene**: `_validateActionInput` rejects non-JSON-safe values, depth > 32, and forbidden key names (normalized), so client code can never smuggle tokens/identity into action input.
- **Issue**: unary calls are tracked and cancelled on `close()`, but the active *feed* stream call is not tracked here (the controller owns it) — acceptable division, noted.
- **Tests**: `grpc_ui_transport_test.dart` (473 lines).
- **Verdict**: retain.

### `app/lib/runtime/runtime_configuration.dart` (reviewed 1-124)
- Endpoint must be an absolute HTTPS origin with no path/query/fragment/userinfo (fail-closed parse). Bootstrap secret is desktop-only (`kIsWeb ? null : getEnv(...)`) so it can never be compiled into or accepted by the web build; OIDC issuer/clientId/scopes validated hard (https issuer, control-char rejection, `openid` scope required, caps on counts/lengths). Verdict: retain.

### `app/lib/runtime/runtime_session_owner.dart` (reviewed 1-172)
- Non-visual lifecycle owner: builds configuration/controller/external-identity source, auto-starts, exposes UI-triggered auth/sign-out, closes idempotently (`_closeFuture ??=`). `_run` swallows errors after notifying — errors surface through controller state, deliberate. Minor: class exposes `close()` while inheriting `dispose()` from ChangeNotifier; calling `dispose()` directly would skip controller shutdown (guarded only by convention; RuntimeShell calls `close()`). Verdict: retain.

### `app/lib/runtime/external_identity.dart`, `external_identity_contract.dart`, `external_identity_stub.dart` (reviewed 1-11, 1-17, 1-19)
- Conditional-import facade (`dart.library.js_interop` → web impl) with an explicit unsupported stub for non-web. Verdict: retain.

### `app/lib/runtime/external_identity_web.dart` (reviewed 1-50)
- Browser OIDC via `openid_client_browser.Authenticator`. `restoreIdentityToken` validates `credential.validateToken()` violations and re-checks token shape before returning; `beginAuthentication` redirects.
- **Finding**: `openid_client_browser.Authenticator` constructs **`Flow.implicit`** (confirmed against pub.dev API docs for openid_client 0.4.x) — the deprecated OAuth2 implicit flow, not authorization-code+PKCE (SEC-700). Tokens are not persisted by the library (state lost on refresh — per docs, only a `state` nonce goes to `window.localStorage`), so no insecure token-at-rest issue, but the id_token transits the URL fragment.
- **Verdict**: replace flow (keep the facade).

### `app/lib/runtime/widgets/runtime_shell.dart` (reviewed 1-291)
- Root widget: sign-in card (bootstrap code obscured, autocorrect/suggestions off, cleared after submit), waiting/terminal states with generic safe copy (never prints error objects), surface expiry re-render timer, `KeyedSubtree(ValueKey(scopeEpoch))` correctly destroys all per-surface widget state (drafts, focus) when the authenticated scope changes — good cross-tenant hygiene. `debugPrint` first-frame marker is the E2E hook and leaks nothing. Tests: `runtime_shell_test.dart` (396 lines). Verdict: retain.

### `app/lib/runtime/widgets/surface_view.dart` (reviewed 1-235)
- Dispatches envelope payloads to renderers; catches render exceptions to a safe fallback. Action submission is single-flighted with progress + generic error copy.
- **Notes**: `_onRemoteEvent` falls back to `surface.actions.single` when an RFW event names no binding — any event on a one-action surface fires that action (PROD-701, low; actions are server-authored and server-verified). `_actionLabel` ignores its parameter and always returns 'Continue' (CLEAN-703).
- Tests: `surface_view_test.dart` (1205 lines, includes INO flows). Verdict: retain, simplify `_actionLabel`.

### `app/lib/runtime/widgets/ino_conversation_view.dart` (reviewed 1-888)
- The INO chat: optimistic turn, delivery-uncertainty state machine keyed on `clientSubmissionId` (crypto-random, reused on retry so redelivery is idempotent), `_definitelyNotSubmitted` distinguishes definitely-failed from maybe-accepted, approval decisions carry `operationId`/`approvalId`/`clientDecisionId` and stay disabled until the authoritative feed advances — exactly the previewed/approved/journaled UX the rail requires. Connection actions delegated to the decoder-validated `InoConversationAction.target` and launched externally.
- **Notes**: legacy reconcile path matches by prompt *text* equality (PROD-702, legacy servers only); `_lastAcceptedReceipt` is written and read only for optimistic-state selection. Verdict: retain.

### `app/lib/runtime/widgets/ino_composer.dart` (reviewed 1-73)
- Bounded (4096) composer with enforced max length, semantics labels. Verdict: retain.

### `app/lib/runtime/buses/*` (5 files + typewriter, all reviewed in full: ino_editor_bus 1-21, ino_source_subscription 1-45, llm_settings_bus 1-51, prompt_input_bus 1-27, state_editor_bus 1-29, typewriter_controller 1-55)
- Global mutable singletons that ferry state across the RFW event boundary. Comments reference `BrainSceneScreen` / `brain_view_screen.dart`, which no longer exist; the only remaining consumers are widgets inside `rfw_host/digitalbrain_rfw_library.dart`, and `InoEditorBus.activeSubscription` is **never assigned** anywhere (always null → all editor-streaming paths dead). `TypewriterController` is sound (timer lifecycle + dispose guard) but its 1-char-per-18ms catch-up means a 32KB message takes ~10 minutes unless `cutToEnd` is called (REL-703 note). Findings ARCH-701, CLEAN-704. Verdict: delete `ino_editor_bus`/`ino_source_subscription`/`state_editor_bus`/`llm_settings_bus` with the legacy rail; keep `typewriter_controller`+`prompt_input_bus` only if the RFW palette that uses them survives its own audit.

### `app/lib/grpc/endpoint.dart` (reviewed 1-143)
- Kernel endpoint resolution from Aspire env / dart-define / `Uri.base`. **No production caller** — only `app/test/grpc/endpoint_test.dart` references it (legacy v1 rail). Web `?port=` query override would let a crafted link retarget the client's kernel port on the same host (SEC-703, dead path). `resolveKernelCallbackUrl` silently falls back to `http://localhost:8081` on error — swallowed failure, dead path. Verdict: delete (with its test) as part of legacy-rail removal.

### `app/lib/grpc/grpc_channel.dart` (reviewed 1-21)
- `createKernelChannel` (returns `dynamic`!) and `kernelInterceptors()` — **zero callers** in `app/`. Verdict: delete (CLEAN-700).

### `app/lib/grpc/digitalbrain.pb.dart`, `digitalbrain.pbgrpc.dart`, `ui.pb.dart`, `ui.pbenum.dart`, `ui.pbgrpc.dart` (excluded-generated)
- protoc/protoc-gen-dart output ("This is a generated file - do not edit", `// @dart = 3.3`), generated from `digitalbrain.proto` / `ui.proto`. Checked in rather than regenerated at build time → staleness risk if the kernel protos move; no generation script was found under `app/` (regeneration procedure undocumented — follow-up). `ui.pb*` serve the live v2 rail; `digitalbrain.pb*` serve only the orphaned v1 rail and shrink to zero users if CLEAN-700/ARCH-700 are executed.

### `app/lib/shell/digitalbrain_client_scope.dart` (reviewed 1-27)
- InheritedWidget for the v1 gateway client. The comment says "HomeScreen wraps its body in this once" — **no such mount exists**; `of()` always returns null. All RFW-card synapse calls, voice transcription, and catalog reloads that depend on it silently no-op (ARCH-700). Verdict: delete with the legacy rail.

### `app/lib/telemetry/telemetry.dart` (reviewed 1-131)
- Singleton wiring: otel SDK tracer provider + hand-rolled OTLP log/metric exporters. Endpoint from `OTEL_EXPORTER_OTLP_ENDPOINT` (skipped when protocol=grpc since exporters speak OTLP/HTTP-JSON) falling back to compile-time `OTLP_ENDPOINT` default `http://localhost:21017`. Headers parsed from `OTEL_EXPORTER_OTLP_HEADERS`. `shutdown()` exists but is never called from `main.dart` (timers cancelled only by circuit breaker). Desktop-only by the `main.dart` guard. Verdict: retain, simplify.

### `app/lib/telemetry/otlp_log_exporter.dart` (reviewed 1-148)
- Periodic-flush buffered OTLP/HTTP JSON log exporter with 4s timeout. Buffer cleared *before* POST → failed batches silently dropped (REL-702); breaker trip cancels the timer and clears buffer permanently. Attributes are caller-supplied strings; current call sites send only type names (no PII). Verdict: retain (until otel-dart ships logs), add tests.

### `app/lib/telemetry/otlp_metric_exporter.dart` (reviewed 1-214)
- Cumulative counters + min/max/sum/count "histograms" exported as OTLP histogram dataPoints **without `explicitBounds`/`bucketCounts`** — spec-tolerated but backend-dependent (FRAME-701). Attr-key encoding (`k=v` joined with `,`) can collide if values contain `,`/`=` — cosmetic. Breaker trip permanently clears all instruments. Verdict: retain, note limitations.

### `app/lib/telemetry/export_circuit_breaker.dart` (reviewed 1-19)
- 3-consecutive-failure permanent trip; no half-open/recovery (REL-701). Verdict: simplify (add reopen timer) or accept documented loss.

### `app/lib/telemetry/grpc_interceptor.dart` (reviewed 1-168)
- OTel client spans + metrics for **v1 gateway** calls, and — critically — injects `x-brain-id: primary` / `x-active-scope: primary` metadata on every non-"bootstrap" call, with bootstrap detection by hard-coded synapse type-name strings (grpc_interceptor.dart:16-33). This is client-asserted identity (SEC-701) and must never be trusted server-side; the v2 rail correctly does not use this interceptor. `traceparent` built by hand with hard-coded `01` sampled flag instead of a W3C propagator (FRAME-702); span status message carries `error.toString()` (SEC-702). Currently unreachable (no caller constructs it — `kernelInterceptors()` is uncalled). Verdict: delete with the legacy rail; if any part is kept, strip the identity headers.

### `app/lib/telemetry/bloc_observer.dart` (reviewed 1-55)
- Logs bloc event/transition **type names** only (no payloads — PII-safe by construction) plus `error.toString()` on failures. But the app defines **zero Blocs/Cubits**; the observer never fires (CLEAN-701). Verdict: delete with the flutter_bloc dependency, or keep only if blocs are planned imminently.

### `app/lib/telemetry/platform_env.dart` + `_io/_stub/_web` (reviewed 1-3, 1-3, 1-1, 1-14)
- Env facade. Web variant reads a single JS global `KERNEL_PORT`. Selector uses `dart.library.html`, while `external_identity.dart` uses `dart.library.js_interop` — under a wasm web build `dart.library.html` is false and the **stub** is selected, silently losing `KERNEL_PORT` (FRAME-700). Verdict: unify on `js_interop`.

### `app/lib/main.dart` (reviewed 1-46)
- Bootstraps binding, e2e semantics flag, fonts, media_kit, desktop telemetry, bloc observer, a stubbed perf gateway (comment documents why), and `runApp`. `GoogleFonts.config.allowRuntimeFetching = true` fetches fonts from the network at runtime — supply-chain/offline note only. Verdict: retain.

### `app/lib/app.dart` (reviewed 1-42)
- MaterialApp.router + forui theming; dark-only. Verdict: retain.

### `app/lib/router.dart` (reviewed 1-15)
- go_router 17.x with two routes (`/` redirect → `/chat` → `RuntimeShell`). Correct minimal usage; no auth redirect needed because `RuntimeShell` owns auth states internally. Verdict: retain.

### `app/lib/features/brain/voice_input.dart` (reviewed 1-267)
- Record → WAV temp file → chunked client-streaming `Transcribe` RPC on the **v1 gateway**. Only reachable from the RFW library, whose client scope is never mounted → transcription can never run (ARCH-700). Implementation notes if revived: reads entire recording into memory then re-chunks (fine for short clips), no size cap on recording length, transcript path deletes the temp file in `finally`, and the error callback interpolates raw `$e` into UI text (`'Transcribe failed: $e'`) — inconsistent with the v2 safe-message discipline. Verdict: move onto the v2 rail or delete.

### `app/lib/features/live/graph/brain_painter.dart` (reviewed 1-913), `comet.dart` (1-165), `cluster_layout.dart` (1-115), `domain_palette.dart` (1-110)
- Legacy "live brain graph" visualization. `BrainPainter` and `drawComets` have **no constructor call sites anywhere** — dead; `stepLayout`/`sphericalSeed` also uncalled. `cluster_layout.GraphNode/GraphEdge` and `domain_palette` helpers are still imported by `rfw_host` files. `brain_painter` contains eight tab-specific render branches, per-frame `DateTime.now()` calls and `shouldRepaint => true` (irrelevant while dead), and a hard-coded neuron id (`DigitalBrain.SDK.Diagram.DiagramExporterNeuron`) overlay. Verdict: delete `brain_painter.dart` + `comet.dart` + dead halves of `cluster_layout.dart`; keep `domain_palette` while the RFW palette needs it.

### `app/lib/widgets/canvas_3d.dart` (reviewed 1-359)
- 3D atom/bond viewer. **No call sites** (CLEAN-700). Internally sound (depth sort, gesture rotation). Verdict: delete or register in the RFW palette deliberately.

### `app/lib/widgets/neuron_vector_logo.dart` (reviewed 1-666)
- Brand/category icon painter; used by `ui_kit/ui_button.dart` and tests (live). String-contains category matching is loose (`'ai'` matches many ids) — cosmetic only. Verdict: retain.

### `app/lib/widgetbook.dart` (reviewed 1-109)
- Dev-only RFW palette catalog. Comment says "widgetbook is intentionally a dev_dependency" but `pubspec.yaml` lists `widgetbook: any` under **dependencies** with an unpinned `any` constraint (CLEAN-702). Verdict: retain file; fix dependency placement/pinning.

## Answers to subsystem questions

**Session/auth.** Desktop: Aspire injects `DIGITALBRAIN_V2_UI_BOOTSTRAP_SECRET` via process env; it is read once (`runtime_configuration.dart:51-53`), never compiled into web builds, never logged (all credential types have redacted `toString`), never persisted, and exchanged for opaque access/refresh tokens held in memory only. Interactive entry uses an obscured TextField cleared on submit. Web: OIDC via `openid_client_browser`; the id_token is validated locally then exchanged for the same in-memory session. **shared_preferences is declared in pubspec but has zero usages in `app/lib` — no tokens or secrets touch it** (CLEAN-702 hygiene note). The one genuine gap: `openid_client_browser.Authenticator` uses the deprecated **implicit flow**, not authorization-code+PKCE (SEC-700; verified against pub.dev API docs since Context7 was quota-exhausted).

**gRPC transport.** TLS enforced: `GrpcUiTransport.connect` rejects non-HTTPS endpoints and builds the channel with `transportSecure: true`; `runtime_configuration.parseUiEndpoint` also rejects non-HTTPS origins. Auth metadata (`x-v2-session`, `x-v2-audience`) is attached to every authenticated call, and a fresh token is fetched (with skew-based refresh and single-flight rotation) before *each* watch/ack/submit. Reconnect: bounded delay ladder (250ms/1s/2s/5s, optionally capped attempts), resume from `feed.lastSequence`, forced snapshot after gap resets, one forced token refresh on auth failure then fail-closed to sign-in. Backpressure: the controller consumes the stream with `await for`, which pauses the underlying single-subscription `ResponseStream` between events; combined with `maxBatchSize` (1..100 validated) this is adequate, though per-event acks are chatty (PERF-700). grpc ^5.1.0 API usage (`GrpcOrGrpcWebClientChannel.toSingleEndpoint`, `ResponseFuture.cancel`, `ResponseStream.cancel`, `isTimelineLoggingEnabled`) matches current pub.dev docs.

**Telemetry.** Desktop-only. Trace spans record rpc method/service names; logs record bloc/event *type names*; no tokens, prompts, or user content are exported — except `error.toString()` strings in span status and bloc error logs, which are safe for the runtime rail's typed errors but unbounded for arbitrary exceptions (SEC-702). opentelemetry ^0.18.11 usage is correct for traces; logs/metrics are hand-rolled because the Dart package has logs unimplemented and metrics in alpha (verified via pub.dev; FRAME-701).

**flutter_bloc.** Correctly configured observer, but there are no Blocs or Cubits in the app at all — the dependency and observer are dead weight (CLEAN-701). No stream-cancellation risk exists because nothing uses it.

**Trust asymmetries.** Client→server: the v2 rail is exemplary — server-driven data is bounds-checked, scope-checked against the signed identity, sensitive keys rejected, and openable URLs allow-listed to the two OAuth start paths on the trusted origin. Server→client: the legacy interceptor's client-asserted `x-brain-id`/`x-active-scope` headers are the one place the client tries to assert identity (SEC-701) — currently unreachable, and must die with the legacy rail.

## Findings

### ARCH-700: Orphaned legacy v1 gateway rail silently no-ops inside live RFW surfaces
- **Severity**: High
- **Confidence**: High
- **Evidence**: `app/lib/shell/digitalbrain_client_scope.dart:8-17` (widget defined, `of()` accessor); no construction site exists anywhere in `app/` (verified by repo-wide search); consumers at `app/lib/rfw_host/digitalbrain_rfw_library.dart:434,802,1918,2357` do `DigitalBrainClientScope.of(context)` and branch on null.
- **Current behavior**: every legacy RFW card path (synapse send, catalog reload, voice transcription via `features/brain/voice_input.dart`) receives a null client and silently does nothing (FACT).
- **Why it matters**: (INFERENCE) if the kernel ever emits an RFW surface using these cards, the user sees interactive UI whose buttons do nothing — a silent-failure class the OS model explicitly forbids; meanwhile ~2.5k lines of transport/UI code rot unexercised.
- **OS/product consequence**: breaks the "surface actions are real, previewed, journaled" contract; duplicated transport authority beside the v2 rail.
- **Recommendation**: (PROPOSAL) delete the v1 rail from the app (`digitalbrain.pb*.dart` users, `grpc_channel.dart`, `endpoint.dart`, `digitalbrain_client_scope.dart`, `grpc_interceptor.dart` legacy parts, `voice_input.dart`, editor buses) or explicitly re-home voice/editor features onto v2 typed actions.
- **Deletion/simplification opportunity**: yes — largest single deletion in the client (~3k lines incl. generated pb).
- **Dependencies**: CLEAN-700, CLEAN-704, SEC-701; rfw_host subsystem audit.
- **Tests/measurements required**: `flutter analyze` + full test run after deletion; grep proves no remaining `DigitalBrainGatewayClient` references.
- **Effort**: M
- **Migration/rollback concern**: none — code is unreachable today.

### ARCH-701: Global singleton buses with stale ownership story
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `app/lib/runtime/buses/llm_settings_bus.dart:6-9`, `prompt_input_bus.dart:8-10`, `state_editor_bus.dart:8-9`, `ino_editor_bus.dart:6-8` — process-global mutable singletons; comments in `ino_source_subscription.dart:3-6` and `prompt_input_bus.dart:6-7` reference `BrainSceneScreen`, which does not exist in the repo; `InoEditorBus.activeSubscription` is read at `digitalbrain_rfw_library.dart:630,788,1128,...` but never assigned anywhere.
- **Current behavior**: RFW widgets write/read global state; the editor-bus read paths always see null (FACT).
- **Why it matters**: (INFERENCE) global mutable state is not scoped to the authenticated session — a scope change (`RuntimeController._scopeEpoch`) resets widget state but NOT these singletons, so a draft prompt or LLM settings written under one identity survive into the next sign-in on the same process.
- **OS/product consequence**: weak cross-session isolation for UI residue (not credentials); contradicts the scope-epoch hygiene the shell otherwise enforces.
- **Recommendation**: (PROPOSAL) delete dead buses with ARCH-700; scope any survivors to the runtime session (e.g., provide via the surface-view subtree keyed by scopeEpoch) and clear them on sign-out.
- **Deletion/simplification opportunity**: yes — at least `ino_editor_bus`, `ino_source_subscription`, `state_editor_bus` are deletable now.
- **Dependencies**: ARCH-700.
- **Tests/measurements required**: widget test proving prompt draft does not survive sign-out/sign-in.
- **Effort**: S
- **Migration/rollback concern**: none.

### SEC-700: Web OIDC sign-in uses the deprecated implicit flow (no PKCE)
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `app/lib/runtime/external_identity_web.dart:19-23` — `oidc.Authenticator(client, scopes: ...)`; pub.dev API docs for `openid_client_browser.Authenticator` show the constructor builds `Flow.implicit(client, ...)` with state in `window.localStorage`.
- **Current behavior**: the browser is redirected with `response_type` of the implicit family; the id_token returns in the URL fragment and is then exchanged via `bootstrapSession` with `authorization: Bearer` (FACT).
- **Why it matters**: (INFERENCE) OAuth 2.0 Security BCP (RFC 9700) deprecates implicit flow: tokens in fragments are exposed to browser history, referrer leaks via injected scripts, and cannot be sender-constrained; PKCE authorization-code flow removes these classes. Mitigations present (strict issuer/client validation, token shape checks, server-side session exchange) reduce but do not eliminate exposure.
- **OS/product consequence**: weakens the "auth is least-privilege, revocable" boundary at its web entry point.
- **Recommendation**: (PROPOSAL) build the flow explicitly with `openid_client`'s `Flow.authorizationCodeWithPKCE` (supported by the package core) instead of the browser Authenticator's implicit default, or gate web sign-in behind a server-side code exchange.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: kernel session service must accept whatever token/code shape results.
- **Tests/measurements required**: e2e web sign-in; assert no token appears in `window.location` after callback handling.
- **Effort**: M
- **Migration/rollback concern**: IdP client registration must allow code flow + PKCE.

### SEC-701: Legacy interceptor injects client-asserted identity headers
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `app/lib/telemetry/grpc_interceptor.dart:16-33,96-99` — `enrichedMetadata['x-brain-id'] = 'primary'; enrichedMetadata['x-active-scope'] = 'primary';` unless the request typeName matches a hard-coded bootstrap list.
- **Current behavior**: any v1 gateway call would carry client-chosen brain/scope identity metadata; the interceptor is currently unreachable (`kernelInterceptors()` has no callers) (FACT).
- **Why it matters**: (INFERENCE) a server that honors these headers lets any client select its brain/scope — a tenant-isolation break waiting to be re-wired; the string-matched bootstrap allow-list is also brittle.
- **OS/product consequence**: violates "server derives identity from signed sessions only".
- **Recommendation**: (PROPOSAL) delete with ARCH-700; if the gateway rail survives, identity must come from server-issued session material, never client metadata.
- **Deletion/simplification opportunity**: yes.
- **Dependencies**: ARCH-700; kernel gateway audit (does the server read these headers?).
- **Tests/measurements required**: server-side test proving `x-brain-id` is ignored/rejected.
- **Effort**: S
- **Migration/rollback concern**: none while unreachable.

### SEC-702: Raw `error.toString()` exported to telemetry backends
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `app/lib/telemetry/grpc_interceptor.dart:74,139` (`span.setStatus(otel.StatusCode.error, error.toString())`); `app/lib/telemetry/bloc_observer.dart:43-46` (`'${bloc.runtimeType} error: $error'` + `error.message` attr); `app/lib/features/brain/voice_input.dart:111` (`'Transcribe failed: $e'` into UI).
- **Current behavior**: arbitrary exception text leaves the process toward the OTLP endpoint / UI (FACT). The v2 rail's typed errors are safe by construction; other exceptions are not.
- **Why it matters**: (INFERENCE) exception messages can embed request payloads, file paths, or user content; telemetry pipelines are broader-audience than the app.
- **Recommendation**: (PROPOSAL) export exception runtime types + safe codes only, mirroring `TransportException.safeMessage` discipline.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: SEC-701 (same file), ARCH-700 (voice_input).
- **Tests/measurements required**: unit test asserting exported log bodies for a synthetic exception contain no message text.
- **Effort**: S
- **Migration/rollback concern**: none.

### SEC-703: Web `?port=` query parameter retargets the kernel endpoint (dead path)
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `app/lib/grpc/endpoint.dart:8-14` — `base.queryParameters['port']` overrides the kernel gRPC port on web.
- **Current behavior**: a crafted link could point the (legacy) client at a different port on the same host; function has no production callers (FACT).
- **Why it matters**: (INFERENCE) same-host-only limits blast radius, but endpoint selection from URL input is an anti-pattern the v2 rail deliberately avoids.
- **Recommendation**: (PROPOSAL) delete with ARCH-700.
- **Deletion/simplification opportunity**: yes.
- **Dependencies**: ARCH-700, CLEAN-700.
- **Tests/measurements required**: none post-deletion.
- **Effort**: S
- **Migration/rollback concern**: none.

### REL-700: Reconnect loop can bypass backoff indefinitely
- **Severity**: Medium
- **Confidence**: Medium
- **Evidence**: `app/lib/runtime/runtime.dart:246-259` (gap reset sets `reconnectImmediately = true`), `:304-315` (successful refresh sets it), `:332-334` (`if (!reconnectImmediately) await _delay(...)`); default `ReconnectPolicy.maxAttempts == null` (runtime.dart:25-34).
- **Current behavior**: whenever an iteration ends with `reconnectImmediately`, the delay is skipped; `reconnectAttempt` increments but with unlimited attempts there is no bound (FACT).
- **Why it matters**: (INFERENCE) a server bug that repeatedly produces sequence gaps immediately after snapshot resume (or repeatedly accepts refresh then rejects the stream) yields a zero-delay reconnect spin — client-side CPU/network hot loop and server hammering.
- **OS/product consequence**: availability/self-DoS on the primary user-facing stream.
- **Recommendation**: (PROPOSAL) allow at most one immediate reconnect in a row; subsequent immediate causes fall back to the delay ladder.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: kernel feed semantics.
- **Tests/measurements required**: controller test: two consecutive gap resets → second reconnect delayed.
- **Effort**: S
- **Migration/rollback concern**: none.

### REL-701: Telemetry circuit breaker trips permanently with no recovery
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `app/lib/telemetry/export_circuit_breaker.dart:12-17` (`_disabled = true`, never reset); `otlp_log_exporter.dart:97-100` and `otlp_metric_exporter.dart:195-199` cancel timers and clear buffers on trip.
- **Current behavior**: 3 consecutive failed POSTs (e.g., collector not yet started at app launch) disable log/metric export for the entire app lifetime (FACT).
- **Why it matters**: (INFERENCE) transient startup ordering (Aspire brings the dashboard up after the app) silently kills observability for the session.
- **Recommendation**: (PROPOSAL) half-open retry after a cool-down, or reset on next successful trace export.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: none.
- **Tests/measurements required**: unit test: 3 failures then success path re-enables.
- **Effort**: S
- **Migration/rollback concern**: none.

### REL-702: Log batches dropped silently on export failure
- **Severity**: Note
- **Confidence**: High
- **Evidence**: `app/lib/telemetry/otlp_log_exporter.dart:64-66` copies and clears the buffer before the POST; the catch block does not requeue.
- **Current behavior**: any failed flush loses that batch (FACT).
- **Why it matters**: (INFERENCE) acceptable for client telemetry, but worth stating as a design decision rather than an accident.
- **Recommendation**: (PROPOSAL) comment the intent or requeue once.
- **Deletion/simplification opportunity**: no. **Dependencies**: REL-701. **Tests**: none required. **Effort**: S. **Migration**: none.

### REL-703: Typewriter catch-up time unbounded relative to text size
- **Severity**: Note
- **Confidence**: High
- **Evidence**: `app/lib/runtime/buses/typewriter_controller.dart:29-41` — 1 character per 18ms tick regardless of backlog.
- **Current behavior**: a 32KB chunk (protocol max message size) would take ~10 minutes to finish typing unless `cutToEnd()` is called (FACT).
- **Why it matters**: (INFERENCE) only matters if the RFW editor path is revived (its feeder is currently dead per ARCH-701).
- **Recommendation**: (PROPOSAL) advance proportionally to backlog (e.g., `max(1, backlog ~/ 20)` chars/tick). **Effort**: S. **Dependencies**: ARCH-700/701. **Deletion opportunity**: possibly delete with rail. **Tests**: unit timing test. **Migration**: none.

### PERF-700: One acknowledgement RPC per accepted feed event
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `app/lib/runtime/runtime.dart:261-269` — after every accepted envelope, `session.accessToken(...)` + `transport.acknowledgeSurfaceFeed(...)` are awaited inline in the stream loop.
- **Current behavior**: N events → N sequential unary acks, each preceded by a token-freshness check; the ack also blocks consumption of the next event (FACT).
- **Why it matters**: (INFERENCE) with `maxBatchSize: 50` bursts, ack latency dominates feed throughput and doubles RPC volume; `_run` already tracks `feed.lastSequence`, so debounced acks (e.g., trailing-edge per batch/500ms) preserve at-least-once semantics because the server treats acks as watermarks (`resumeSequence` model).
- **OS/product consequence**: slower surface convergence on weak links; unnecessary kernel load.
- **Recommendation**: (PROPOSAL) coalesce acks to the highest sequence on a short debounce and on stream close.
- **Deletion/simplification opportunity**: yes — removes the duplicate-ack path for `FeedDuplicate` at equal sequence.
- **Dependencies**: kernel feed watermark semantics (verify ack is high-watermark, not per-event).
- **Tests/measurements required**: controller test with 50-event burst asserting single ack at seq 50; latency measurement.
- **Effort**: S-M
- **Migration/rollback concern**: server must not require per-sequence acks.

### PROD-701: Single-action fallback fires on any unnamed RFW event
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `app/lib/runtime/widgets/surface_view.dart:193-198` — when no binding/actionType matches, `if (action == null && widget.surface.actions.length == 1) action = widget.surface.actions.single;`.
- **Current behavior**: any RFW event name on a one-action surface submits that action (FACT).
- **Why it matters**: (INFERENCE) a benign non-action event (hover/analytics-style event in a future RFW doc) would trigger the surface's only action; server-side preview/approval bounds the damage, but the client should not guess intent.
- **Recommendation**: (PROPOSAL) require an explicit binding or actionType; drop the `.single` fallback.
- **Deletion/simplification opportunity**: yes (one branch).
- **Dependencies**: kernel-emitted RFW docs must always name bindings (verify in kernel audit).
- **Tests/measurements required**: surface_view test: unnamed event on 1-action surface → no submission.
- **Effort**: S
- **Migration/rollback concern**: could break existing RFW docs relying on the fallback.

### PROD-702: Legacy submission reconciliation matches by prompt text
- **Severity**: Low
- **Confidence**: Medium
- **Evidence**: `app/lib/runtime/widgets/ino_conversation_view.dart:176-197` — when the server sends no `operationId`, confirmation is inferred from `lastUserTurn.text == pendingPrompt`.
- **Current behavior**: with legacy (operationId-less) servers, an identical earlier prompt already in the transcript would confirm a submission that never landed (FACT for the comparison; scenario inferred).
- **Why it matters**: (INFERENCE) misreports delivery in the uncertainty state machine; modern path (operationId) is unaffected.
- **Recommendation**: (PROPOSAL) delete the legacy reconcile path once kernels always emit operation metadata (decoder already models the "legacy" split).
- **Deletion/simplification opportunity**: yes.
- **Dependencies**: kernel surface emission version.
- **Tests/measurements required**: none if deleted; else duplicate-prompt test.
- **Effort**: S
- **Migration/rollback concern**: needs kernel-version gate.

### FRAME-700: Mixed conditional-import keys break wasm web builds' env access
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `app/lib/telemetry/platform_env.dart:1-3` uses `dart.library.html`; `app/lib/runtime/external_identity.dart:2-3` uses `dart.library.js_interop`.
- **Current behavior**: JS-compiled web selects `platform_env_web.dart`; a wasm build selects the stub (`getEnv` → null), silently losing the `KERNEL_PORT` JS-global lookup (FACT of selection semantics; wasm impact inferred from Dart conditional-import rules — `dart.library.html` is unavailable under wasm).
- **Why it matters**: (INFERENCE) inconsistent platform seams create divergent behavior between compile targets; `dart:html`-era keys are deprecated.
- **Recommendation**: (PROPOSAL) switch `platform_env.dart` to `dart.library.js_interop` (the web impl already uses `dart:js_interop`).
- **Deletion/simplification opportunity**: no. **Dependencies**: endpoint.dart is dead (CLEAN-700) — if deleted, `KERNEL_PORT` handling may go entirely. **Tests**: web smoke on wasm target. **Effort**: S. **Migration**: none.

### FRAME-701: Hand-rolled OTLP metric export emits histograms without buckets
- **Severity**: Note
- **Confidence**: High
- **Evidence**: `app/lib/telemetry/otlp_metric_exporter.dart:137-160` — histogram dataPoints carry count/sum/min/max but no `explicitBounds`/`bucketCounts`; log/metric exporters exist because opentelemetry-dart 0.18 has logs "unimplemented" and metrics in alpha (verified via pub.dev; Context7 quota-exhausted — documentation gap recorded).
- **Current behavior**: OTLP-tolerant collectors accept it; percentile queries are impossible (FACT).
- **Recommendation**: (PROPOSAL) either add fixed explicit bounds or export as summary-style gauges; revisit when otel-dart metrics stabilize.
- **Deletion/simplification opportunity**: no. **Dependencies**: none. **Tests**: exporter golden JSON test. **Effort**: S. **Migration**: dashboard queries.

### FRAME-702: Manual traceparent with hard-coded sampled flag; spans have no parents
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `app/lib/telemetry/grpc_interceptor.dart:165-167` — `'00-${ctx.traceId}-${ctx.spanId}-01'`; no use of the otel API `W3CTraceContextPropagator`; spans are created but never installed in a context for child operations.
- **Current behavior**: every RPC claims sampled=01 regardless of sampler decisions; spans are roots (FACT).
- **Recommendation**: (PROPOSAL) use the package's propagator API; moot if deleted with ARCH-700.
- **Deletion/simplification opportunity**: yes (with legacy rail). **Dependencies**: ARCH-700. **Tests**: header-format unit test. **Effort**: S. **Migration**: none.

### CLEAN-700: Dead legacy transport and visualization files
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `app/lib/grpc/grpc_channel.dart:6-21` (`createKernelChannel` returns `dynamic`, `kernelInterceptors` — zero callers), `app/lib/grpc/endpoint.dart` (only caller is `app/test/grpc/endpoint_test.dart`), `app/lib/features/live/graph/brain_painter.dart` + `comet.dart` (`BrainPainter`/`drawComets` never constructed outside the file pair), `cluster_layout.dart:47-114` (`sphericalSeed`, `stepLayout` uncalled), `app/lib/widgets/canvas_3d.dart` (`Canvas3DWidget` never used), stale comment `cluster_layout.dart:88` referencing removed `brain_view_screen.dart`.
- **Current behavior**: ~1.7k lines of unreachable hand-written code plus their test surface ship in the app (FACT).
- **Why it matters**: (INFERENCE) reading/maintenance tax; violates the repo's delete-first WoW; hides which rail is real.
- **Recommendation**: (PROPOSAL) delete all listed items in one PR alongside ARCH-700.
- **Deletion/simplification opportunity**: yes — the point.
- **Dependencies**: ARCH-700 (shared fate), rfw_host audit for `domain_palette`/`GraphNode` retention.
- **Tests/measurements required**: analyze + full test green post-delete.
- **Effort**: S-M
- **Migration/rollback concern**: git history preserves the visualizations if a live-graph feature returns.

### CLEAN-701: flutter_bloc + TelemetryBlocObserver + bloc_test with zero blocs
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `app/lib/main.dart:30` sets `Bloc.observer`; repo-wide search finds no `Bloc`/`Cubit` subclasses in `app/lib`; `app/pubspec.yaml:15` (`flutter_bloc`), dev `bloc_test`.
- **Current behavior**: observer never fires; two dependencies carried for nothing (FACT).
- **Recommendation**: (PROPOSAL) remove dependency + observer, or land the first real bloc.
- **Deletion/simplification opportunity**: yes. **Dependencies**: none. **Tests**: build green. **Effort**: S. **Migration**: none.

### CLEAN-702: Dependency hygiene — unused shared_preferences; widgetbook in prod deps with `any`
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `app/pubspec.yaml:42` (`shared_preferences: ^2.5.5`, zero usages under `app/lib` — verified by search; a comment at pubspec line ~38 says "persistence via prefs" but no code exists); `app/pubspec.yaml:54` `widgetbook: any` under `dependencies` while `app/lib/widgetbook.dart:9-10` claims it is "intentionally a dev_dependency".
- **Current behavior**: unused plugin compiled into every platform build; unpinned catalog framework in the production dependency closure (FACT).
- **Why it matters**: (INFERENCE) contradicts "latest deliberate versions" policy; shared_preferences presence invites future accidental secret persistence.
- **Recommendation**: (PROPOSAL) drop shared_preferences until needed; move widgetbook to dev_dependencies with a caret pin.
- **Deletion/simplification opportunity**: yes. **Dependencies**: none. **Tests**: build. **Effort**: S. **Migration**: none.

### CLEAN-703: `SurfaceView._actionLabel` ignores its parameter
- **Severity**: Note
- **Confidence**: High
- **Evidence**: `app/lib/runtime/widgets/surface_view.dart:232-234` — `static String _actionLabel(String actionType) { return 'Continue'; }`.
- **Current behavior**: all native-surface action buttons read "Continue" regardless of action type (FACT).
- **Why it matters**: (INFERENCE) a two-action native surface would show two identical "Continue" buttons — user cannot distinguish approve/reject-class actions.
- **Recommendation**: (PROPOSAL) derive a label from `actionType` or have the server supply labels (as INO surfaces already do).
- **Deletion/simplification opportunity**: no. **Dependencies**: kernel surface emission. **Tests**: widget test for multi-action native surface. **Effort**: S. **Migration**: none.

### CLEAN-704: Stale comments reference deleted screens
- **Severity**: Note
- **Confidence**: High
- **Evidence**: `app/lib/runtime/buses/ino_source_subscription.dart:3`, `prompt_input_bus.dart:6-7` ("BrainSceneScreen"), `cluster_layout.dart:88` ("brain_view_screen.dart"), `shell/digitalbrain_client_scope.dart:6-7` ("HomeScreen wraps its body in this once").
- **Current behavior**: comments describe an architecture that no longer exists (FACT).
- **Recommendation**: (PROPOSAL) fix or delete with the rail. **Effort**: S. Others: n/a.

### TEST-700: Telemetry subsystem has zero tests
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `app/test/` contains no files for `telemetry/**` (directory listing); the OTLP JSON wire formats in `otlp_log_exporter.dart`/`otlp_metric_exporter.dart` and the circuit breaker are hand-rolled and unverified.
- **Current behavior**: a malformed payload change would ship silently; breaker semantics (REL-701) unpinned (FACT).
- **Why it matters**: (INFERENCE) hand-written wire formats are exactly where regressions hide; the well-tested runtime rail shows the team knows how — this is an omission, not a capability gap.
- **Recommendation**: (PROPOSAL) golden-JSON tests for both exporters (mock http client), breaker unit tests, interceptor metadata tests (or delete the interceptor per ARCH-700).
- **Deletion/simplification opportunity**: partially — deleting the legacy interceptor shrinks the surface to test.
- **Dependencies**: REL-701, FRAME-701, SEC-702.
- **Tests/measurements required**: as above.
- **Effort**: S-M
- **Migration/rollback concern**: none.

### TEST-701: Dead/legacy feature code carries no tests (expected, but confirms deletability)
- **Severity**: Note
- **Confidence**: High
- **Evidence**: no tests exist for `features/live/graph/**`, `features/brain/voice_input.dart`, `widgets/canvas_3d.dart`, `runtime/buses/**`; the only `grpc/` test (`endpoint_test.dart`) exercises a function with no production callers.
- **Why it matters**: (INFERENCE) nothing will regress when CLEAN-700/ARCH-700 delete these — deletion is cheap and safe.
- **Recommendation**: (PROPOSAL) delete code + `endpoint_test.dart` together. **Effort**: S. Others: n/a.

## Verification gaps

- **Context7 unavailable** (monthly quota exceeded) throughout this audit. Framework claims were instead verified against official pub.dev API docs via web fetch: `openid_client_browser.Authenticator` → `Flow.implicit` (constructor implementation page), `GrpcOrGrpcWebClientChannel.toSingleEndpoint` semantics (grpc-dart docs; credential details not shown on the page — TLS-on-`transportSecure:true` is asserted from the class contract, confidence Medium), opentelemetry-dart 0.18 signal support matrix (traces beta / metrics alpha / logs unimplemented). grpc-dart HTTP/2 flow-control-on-pause behavior for the `await for` backpressure claim could not be doc-verified; marked as inference.
- Regeneration procedure for the checked-in `*.pb*.dart` files is undocumented in `app/` — staleness risk unquantified (follow-up for build/tooling audit).
