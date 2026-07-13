# Subsystem audit: platform-and-skills

- **Subsystem**: platform-and-skills — native platform scaffolding for the Flutter client (`app/android`, `app/ios`, `app/linux`, `app/macos`, `app/web`, `app/windows`) plus the vendored agent skills in `.agents/skills/`.
- **Commit**: `72400e3ebbec27e17af4ae6b5b2c4158c2797fa4` (branch `docs/refinement-audit`)
- **Date**: 2026-07-13
- **Scope**: the 121 files in the platform file list and the 37 files in the skills file list (158 files total).

## Subsystem overview

The Flutter app (`app/`) is the DigitalBrain client shell. Its platform directories are almost entirely `flutter create` scaffolding, lightly touched by the tooling when plugins were added (generated plugin registrants) and by two hand-edits to `app/web/index.html` (canonical-domain redirect, `kernel_port.txt` bootstrap shim, SEO/OG metadata). The client is a gRPC/OTLP network client of the kernel on every platform, records microphone audio for voice input (`record` plugin, `app/lib/features/brain/voice_input.dart`), accepts file drops/pickers, and plays video (`media_kit`, `youtube_player_iframe`).

Actual shipping targets today are **Windows desktop** (dev, launched via `src/DigitalBrain.Aspire/FlutterAspireExtensions.cs`, whose secret-bearing client is documented "desktop targets only") and **web** (production `digitalbrain.tech`, built by `.github/workflows/deploy.yml`). Android, iOS, macOS and Linux scaffolding is checked in but has never been reconciled with the app's actual capabilities — this is where the significant findings live: the OS-level permission/entitlement declarations do not grant what the app needs (network on Android/macOS, microphone on Android/iOS/macOS), so those targets are broken-by-default rather than over-permissioned. Nothing in the platform layer weakens the OS trust model (no cleartext-traffic opt-in, no extra exported components, sandbox not disabled, JIT only in macOS debug).

`.agents/skills/` contains six **vendored Microsoft Aspire agent skills** (`author: Microsoft`, MIT, version 0.0.1), installed by `aspire agent init` / `aspire init` (commits `3d367fe`, `9bdeefc`). They are instruction documents for coding agents (lifecycle, wiring, deployment, monitoring guidance) — no secrets, no executable automation, destructive operations (`aspire destroy --yes`) explicitly gated on human intent.

---

## Per-file review — app/android

### app/android/app/src/main/AndroidManifest.xml (reviewed 1–45)
Hand-editable manifest; content is the stock template. Single activity `.MainActivity`, `exported="true"` — required for the LAUNCHER intent filter and safe (no other intent filters, `taskAffinity=""` hardens against task hijacking). `queries` block is the standard PROCESS_TEXT declaration. **No `<uses-permission>` at all**: no `INTERNET`, no `RECORD_AUDIO`. The app is a gRPC client with voice input; Android enforces `INTERNET` for all sockets and `RECORD_AUDIO` for the `record` plugin, so any Android build is non-functional (findings PROD-1001, PROD-1002). No `usesCleartextTraffic` opt-in and no `networkSecurityConfig` (good default; but note dev kernels are plain-HTTP, so a debug-only network security config will be needed when Android becomes a target). App label is the raw project name `digitalbrain_flutter` (CLEAN-1000). Verdict: **retain + fix permissions when Android becomes a target**.

### app/android/app/src/profile/AndroidManifest.xml (reviewed 1–7)
Stock profile-build overlay granting `INTERNET` for the Flutter tool. Notably the sibling **debug** overlay (`src/debug/AndroidManifest.xml`, part of every `flutter create` output) is absent from the repo — so even `flutter run` debug builds on Android have no INTERNET grant (PROD-1001). Verdict: retain.

### app/android/app/build.gradle.kts (reviewed 1–45)
Stock template: `namespace`/`applicationId` `io.digitalbrain.app`, Java 17, min/target/compile SDK delegated to the Flutter plugin (`flutter.minSdkVersion` = Flutter default 24 for current stable; `record` requires ≥23, satisfied). Release build type **signs with the debug keystore** (stock TODO left in place, lines 33–39) — fine for local `flutter run --release`, unacceptable for distribution (SEC-1000). No dependencies added, no ProGuard config. Verdict: retain; add real signing config before any Android release.

### app/android/build.gradle.kts (reviewed 1–25)
Stock root build script (google/mavenCentral repos, relocated build dir, clean task). No custom repositories or plugins. Verdict: retain.

### app/android/gradle.properties (reviewed 1–2)
Two lines: JVM args (`-Xmx8G` etc. — generous but harmless; stock current template values) and `android.useAndroidX=true`. Verdict: retain.

### app/android/settings.gradle.kts (reviewed 1–27)
Stock plugin management reading `flutter.sdk` from `local.properties` (correctly gitignored). AGP 8.11.1 / Kotlin 2.2.20 — current template pins. Verdict: retain.

### app/android/gradle/wrapper/gradle-wrapper.properties (reviewed 1–5)
Gradle 8.14-all from services.gradle.org over HTTPS. `gradle-wrapper.jar` is gitignored (see .gitignore) so no binary-tampering surface in-repo. No `distributionSha256Sum` pinning (Note-level; standard for Flutter templates). Verdict: retain.

### app/android/app/src/main/kotlin/io/digitalbrain/app/MainActivity.kt (reviewed 1–5)
Empty `FlutterActivity` subclass — stock. Verdict: retain.

### app/android/app/src/main/res/** (styles.xml, values-night/styles.xml, launch_background.xml ×2 — each reviewed in full; mipmap PNGs excluded-asset)
Stock launch/normal themes and splash layer-lists, unmodified. Launcher PNGs are the default Flutter icon (binary assets, not line-audited). Verdict: retain (replace icons before store distribution — cosmetic).

### app/android/.gitignore (reviewed 1–14)
Stock; correctly ignores `local.properties`, `key.properties`, `*.keystore`, `*.jks` — keeps signing material out of the repo. Verdict: retain.

---

## Per-file review — app/ios

### app/ios/Runner/Info.plist (reviewed 1–71)
Stock scene-based template (Flutter 3.4x `UIApplicationSceneManifest` + `SceneDelegate` variant), display name "DigitalBrain Flutter". **No `NSMicrophoneUsageDescription`** — the `record` plugin requires it (verified against pub.dev/packages/record setup docs); iOS terminates an app that requests mic access without the usage string, so voice input crashes any iOS build (PROD-1002). No `NSAppTransportSecurity` exceptions (good; note dart:io sockets used by grpc-dart are not subject to ATS, so dev plain-HTTP kernels are not blocked by this on iOS). Verdict: retain + add mic usage string when iOS becomes a target.

### app/ios/Runner/AppDelegate.swift (reviewed 1–17), SceneDelegate.swift (1–7), Runner-Bridging-Header.h (1–1)
Stock current-template implicit-engine registration (`FlutterImplicitEngineDelegate`), empty `FlutterSceneDelegate`. No custom native code. Verdict: retain.

### app/ios/RunnerTests/RunnerTests.swift (reviewed 1–13)
Placeholder empty XCTest (TEST-1000). Verdict: retain (or delete until native code exists).

### app/ios/Flutter/AppFrameworkInfo.plist (1–25), Debug.xcconfig (1–1), Release.xcconfig (1–1)
Stock tool-managed files; xcconfigs only include `Generated.xcconfig` (gitignored). Note: no `Podfile` exists under `app/ios/` — with CocoaPods-based plugin dependencies a Podfile is normally generated on first iOS build and committed; whether this repo relies on Flutter's Swift Package Manager flow instead is unverified (REL-1001, Context7 quota prevented doc verification). Verdict: retain.

### app/ios/Runner.xcodeproj/project.pbxproj + workspaces/schemes/settings (excluded-generated)
Generated by `flutter create`/Xcode. Grep-scanned for anomalies: only the two standard `xcode_backend.sh` script phases; `PRODUCT_BUNDLE_IDENTIFIER = io.digitalbrain.app`; automatic signing; no `DEVELOPMENT_TEAM` committed (correct). Handled correctly (checked in, as required).

### app/ios/Runner/Base.lproj/LaunchScreen.storyboard (1–38), Main.storyboard (1–27)
Stock storyboards, unmodified. Verdict: retain.

### app/ios/Runner/Assets.xcassets/** (Contents.json + PNGs — excluded-generated/asset)
Default Flutter icon set and launch images (the LaunchImage README that documented them was deleted in `134db9a`, orphaning the placeholder images slightly — cosmetic). Handled correctly.

### app/ios/.gitignore (reviewed 1–35)
Stock; ignores Pods/, ephemeral, Generated.xcconfig. Verdict: retain.

---

## Per-file review — app/linux

### app/linux/runner/main.cc (1–7), my_application.cc (1–149), my_application.h (1–22)
Stock GTK runner, byte-for-byte template behavior (header-bar heuristic, `G_APPLICATION_NON_UNIQUE`, dart entrypoint args passthrough). Window title hardcoded `digitalbrain_flutter` (lines 48, 52 — CLEAN-1000). `APPLICATION_ID` is `io.digitalbrain.digitalbrain_flutter` (CMake), inconsistent with `io.digitalbrain.app` used on Android/macOS (CLEAN-1000). No sandboxing on Linux (native default — nothing to declare). Verdict: retain.

### app/linux/CMakeLists.txt (1–129), runner/CMakeLists.txt (1–27), flutter/CMakeLists.txt (1–89)
Stock Flutter Linux build rules; `flutter/CMakeLists.txt` is tool-managed ("should not be edited"). No custom compile options beyond template `-Wall -Werror`. Verdict: retain.

### app/linux/flutter/generated_plugin_registrant.{cc,h}, generated_plugins.cmake (excluded-generated)
Generated by the Flutter tool from pubspec (desktop_drop, irondash_engine_context, media_kit, record_linux, super_native_extensions, url_launcher_linux, jni FFI). Checked in as Flutter requires; regenerated on `flutter pub get` — consistent with pubspec, no staleness observed. Handled correctly.

### app/linux/.gitignore (1–1)
Ignores `flutter/ephemeral`. Correct.

---

## Per-file review — app/macos

### app/macos/Runner/DebugProfile.entitlements (reviewed 1–13)
Stock: `app-sandbox` + `cs.allow-jit` + `network.server`. **Missing `com.apple.security.network.client`** — verified against docs.flutter.dev/platform-integration/macos/building: the template does not include it and without it "network requests fail with SocketException: Operation not permitted". The app is purely a network client of the kernel, so even debug macOS runs cannot connect (PROD-1000). Also missing `com.apple.security.device.audio-input` for the `record` plugin (PROD-1002). Verdict: retain + add `network.client` and `device.audio-input`.

### app/macos/Runner/Release.entitlements (reviewed 1–9)
Stock: `app-sandbox` only. Missing `network.client` (fatal for this app), `device.audio-input` (voice), and `files.user-selected.read-only` is not present either — `file_picker`/drag-drop file reading under the sandbox typically requires the user-selected-files entitlement (medium confidence; folded into PROD-1000/PROD-1002 recommendations). Positive security note: sandbox is **on** and JIT is **not** allowed in release — the desktop client cannot be a vector for Foundry code execution; nothing here disables the sandbox. Verdict: retain + fix entitlements.

### app/macos/Runner/Info.plist (reviewed 1–33)
Stock; all values via build settings. **No `NSMicrophoneUsageDescription`** (required by `record` on macOS — PROD-1002). Verdict: retain + add usage string.

### app/macos/Runner/AppDelegate.swift (1–14), MainFlutterWindow.swift (1–16)
Stock (terminate-after-last-window, secure restorable state = true, plugin registration). Verdict: retain.

### app/macos/Runner/Configs/AppInfo.xcconfig (1–15), Debug/Release/Warnings.xcconfig (each read in full)
Stock; `PRODUCT_NAME = digitalbrain_flutter`, `PRODUCT_BUNDLE_IDENTIFIER = io.digitalbrain.app`, 2026 copyright. Verdict: retain (product name cosmetic — CLEAN-1000).

### app/macos/Flutter/Flutter-Debug.xcconfig (1–1), Flutter-Release.xcconfig (1–1)
One-line includes of tool-generated ephemeral config. Retain.

### app/macos/Flutter/GeneratedPluginRegistrant.swift (excluded-generated)
Generated; registers 13 plugins (adds file_picker, shared_preferences, webview_flutter_wkwebview, wakelock_plus, device_info_plus, package_info_plus vs the Linux/Windows sets — transitive from youtube_player_iframe/media_kit). Consistent with pubspec; updated in `8df5f8e` when drag-drop was added. Handled correctly.

### app/macos/Runner.xcodeproj/project.pbxproj + workspace files + Runner.xcscheme (excluded-generated)
Generated by flutter create/Xcode; grep-scanned: standard `macos_assemble.sh` phases, entitlements correctly bound per configuration (`CODE_SIGN_ENTITLEMENTS = Runner/DebugProfile.entitlements` for Debug/Profile, `Runner/Release.entitlements` for Release), ad-hoc signing (`CODE_SIGN_IDENTITY = "-"`). Handled correctly.

### app/macos/Runner/Base.lproj/MainMenu.xib, Assets.xcassets/** (excluded-generated/asset)
Stock menu nib and default icons. Handled correctly.

### app/macos/RunnerTests/RunnerTests.swift (reviewed 1–13)
Placeholder empty XCTest (TEST-1000).

### app/macos/.gitignore (1–7)
Stock. Correct.

---

## Per-file review — app/web

### app/web/index.html (reviewed 1–89) — HAND-EDITED
The one substantially hand-edited platform file. Three custom additions on top of the template:
1. **Canonical-domain redirect** (lines 4–9): client-side JS replace from `www.digitalbrain.tech` to `digitalbrain.tech`. Works, but is not a real 301 (comment claims "301-equivalent"); users on www pay a full page load first and crawlers may not treat it as canonical — mitigated by the `rel=canonical` link (line 29). Acceptable; a server-side redirect at the host would be strictly better.
2. **SEO/OG metadata** (lines 26–49): static, no external loads. Good.
3. **`kernel_port.txt` bootstrap gate** (lines 51–76): sets `window.KERNEL_PORT = null`, then fetches `kernel_port.txt?t=<now>` and only loads `flutter_bootstrap.js` after the fetch settles. **Nothing in the repository produces `kernel_port.txt`** (repo-wide grep: only index.html references it; `.github/workflows/deploy.yml` just runs `flutter build web`), so in production every page load performs a guaranteed-404 round-trip *before* the Flutter engine even begins loading, plus a console warning (REL-1000). Consumed by `app/lib/telemetry/platform_env_web.dart` → `app/lib/grpc/endpoint.dart`, which also accepts a `?port=` query parameter — the query-param path appears to be the live mechanism.
No external scripts/fonts/CDNs are referenced — everything is same-origin (favicon, manifest, flutter_bootstrap.js). **No CSP** meta tag (SEC-1001): given the app renders server-driven UI (rfw) and embeds YouTube iframes, a CSP would be cheap defense-in-depth for the production origin. Verdict: **simplify** — delete the dead kernel_port gate (or make bootstrap non-blocking), keep the rest.

### app/web/manifest.json (reviewed 1–36)
Hand-tuned PWA manifest (name, dark `#0A0A0F` theme, description matching product copy, four icons incl. maskable). Correct and minimal; `start_url: "."` and `display: standalone` are fine for a PWA shell. Verdict: retain.

### app/web/favicon.png, icons/Icon-*.png (excluded-asset)
Binary icons (default Flutter blue icon per byte size — not line-audited). Cosmetic: replace before treating the PWA as branded. Handled correctly.

---

## Per-file review — app/windows

### app/windows/runner/main.cpp (1–44), flutter_window.{cpp,h} (1–72 / 1–34), win32_window.{cpp,h} (1–289 / 1–103), utils.{cpp,h} (1–66 / 1–20)
Stock Flutter Windows runner, read in full; matches the current template exactly (COM init, DPI-aware Win32Window, dark-mode titlebar via `DwmSetWindowAttribute`, UTF-16→UTF-8 args). Window title `digitalbrain_flutter` (main.cpp:30 — CLEAN-1000). No custom message handling, no elevation, manifest requests only PerMonitorV2 DPI awareness + Win10/11 supportedOS (runner.exe.manifest 1–15) — no privileged capabilities. This is the primary dev target and it works because Windows imposes no manifest-level network/mic permission gates. Verdict: retain.

### app/windows/CMakeLists.txt (1–109), runner/CMakeLists.txt (1–41), flutter/CMakeLists.txt (not on list but tool-managed), Runner.rc (1–122), resource.h (1–17)
Stock build rules and version resource (fed from FLUTTER_VERSION defines; company `io.digitalbrain`, 2026 copyright). Verdict: retain.

### app/windows/flutter/generated_plugin_registrant.{cc,h}, generated_plugins.cmake (excluded-generated)
Generated from pubspec (desktop_drop, irondash_engine_context, media_kit, record_windows, super_native_extensions, url_launcher_windows, jni). Updated in `8df5f8e`. Handled correctly.

### app/windows/runner/resources/app_icon.ico (excluded-asset)
Default icon binary.

### app/windows/.gitignore (1–17)
Stock. Correct.

---

## Per-file review — .agents/skills (vendored, Microsoft, MIT)

All 37 files were read in full. They are prose/instruction markdown (plus two reference GitHub-Actions YAMLs); none executes anything by itself. Assessment per skill:

### .agents/skills/aspire/SKILL.md (1–159) + references/aspire-13-3-breaking-changes.md (1–110)
Top-level router skill: detection table, workflow, routing to sub-skills, prerequisites, and a 13.3 breaking-change scrub list. Accurate to Aspire 13.3/13.4-era CLI; consistent with the repo's pinned Aspire 13.4.6. Ironically the file contains "Project-Local Skill Override" language written for the *plugin* copy — since this *is* the project-local copy, that section is self-referential noise (ARCH-1000). Verdict: retain (vendored; refresh via `aspire agent init` rather than hand-edit).

### .agents/skills/aspire-init/ (SKILL.md 1–147; references/init-workflow.md 1–124; references/templates.md 1–93)
First-run skeleton-drop guidance (`aspire new` vs `aspire init`). Not useful for this repo (an AppHost already exists — the skill itself says "do not run init" in that case), so it is inert weight kept only because the vendored set ships as a unit. Verdict: retain-as-vendored; candidate for deletion if the skill set is ever trimmed (Step-2 deletion opportunity, low value).

### .agents/skills/aspireify/ (SKILL.md 1–330; references: apphost-wiring.md 1–394, csharp-authoring.md 1–180, docker-compose.md 1–215, full-solution-apphosts.md 1–333, javascript-apps.md 1–151, opentelemetry.md 1–113, scan-and-propose.md 1–122, service-defaults.md 1–115, typescript-authoring.md 1–247, validation.md 1–98)
AppHost-wiring skill. Content quality is high and includes genuinely good security guidance (never pass secrets via `WithArgs`, never model auto-generated integration passwords, never print secret values). Mostly inapplicable day-to-day since the DigitalBrain AppHost is fully wired; the skill self-describes as "one-time" and self-deactivating. Verdict: retain-as-vendored.

### .agents/skills/aspire-orchestration/ (SKILL.md 1–205; references: agent-workflows.md 1–120, app-commands.md 1–124, detection.md 1–161, resource-management.md 1–39, safety-guardrails.md 1–273)
Lifecycle/safety skill (aspire start/stop/wait, file-lock recovery). This is the most operationally valuable skill for this repo, and it **overlaps and partially conflicts with `CLAUDE.md`'s way-of-working** (CLAUDE.md prescribes `aspire run`, `dotnet test` root loops and Aspire MCP tools; the skill prescribes `aspire start`/`aspire wait`/CLI and says "avoid `aspire run` in agent workflows"). Two co-existing authorities for the same loop is drift risk (ARCH-1000). No unsafe automation; the guardrails are the opposite. Verdict: retain; reconcile with CLAUDE.md.

### .agents/skills/aspire-deployment/ (SKILL.md 1–223; references: aws.md 1–177, azure.md 1–317, cicd.md 1–343, docker-compose.md 1–156, github-actions-azure-csharp.yml 1–54, github-actions-azure-typescript.yml 1–54, javascript.md 1–127, kubernetes.md 1–237, preflight.md 1–190)
Deployment guidance. The two YAML references are templates, not live workflows (the repo's real deploy is `.github/workflows/deploy.yml`); they pin third-party actions by commit SHA (good practice) and reference secrets only via `${{ secrets.* }}` — no secret material. Guidance repeatedly enforces "do not print secret values", `--yes` only after explicit teardown intent, GitHub Environments for production gates. Verdict: retain-as-vendored.

### .agents/skills/aspire-monitoring/ (SKILL.md 1–198; references: diagnostics-bridge.md 1–210, monitoring.md 1–162, playwright-handoff.md 1–22)
Observability routing. Heavy internal duplication (the standalone-dashboard and `--dashboard-url` sections appear near-verbatim in SKILL.md, monitoring.md and diagnostics-bridge.md) — vendored, so not worth hand-deduplicating (CLEAN-1001). Verdict: retain-as-vendored.

**Cross-cutting skills verdict**: they belong in the repo (Aspire's own precedence model expects project-local copies and the docs in the skills themselves say project-local wins), contain **no secrets** (grep for key/token/password/BEGIN patterns returned only instructional text) and **no unsafe automation**. Risks are staleness (pinned to "13.4 guidance from the current Aspire development branch", `version: 0.0.1`, no refresh process recorded) and duplicated authority vs CLAUDE.md.

---

## Findings

### PROD-1000: macOS entitlements lack `com.apple.security.network.client` — desktop client cannot reach the kernel
- **Severity**: High
- **Confidence**: High
- **Evidence**: `app/macos/Runner/DebugProfile.entitlements:4-11` (sandbox + jit + network.server only) and `app/macos/Runner/Release.entitlements:4-7` (sandbox only) — FACT. Flutter docs (docs.flutter.dev/platform-integration/macos/building) confirm the template omits `network.client` and that without it outbound requests fail with `SocketException: Operation not permitted` — FACT (doc-verified 2026-07-13).
- **Current behavior**: Both entitlement files are the untouched template. The app's entire function is outbound gRPC/HTTP/OTLP to the kernel (`app/lib/grpc/endpoint.dart`, pubspec `grpc`, `http`, `opentelemetry`).
- **Why it matters**: (INFERENCE) any macOS build — debug or release — fails on its first network call; macOS is inside the AppHost's stated "desktop targets only" support surface, so the platform is silently broken.
- **OS/product consequence**: The client shell — the user's window into every Neuron/INO journey — is dead on macOS; no auth handshake, no synapse traffic.
- **Recommendation**: (PROPOSAL) add `com.apple.security.network.client` to both entitlement files; add `com.apple.security.files.user-selected.read-only` for file_picker/drag-drop reads under the sandbox (verify against file_picker docs).
- **Deletion/simplification opportunity**: no.
- **Dependencies**: PROD-1002 (same files also need audio-input).
- **Tests/measurements required**: `flutter run -d macos` reaching the kernel; a release build performing one gRPC call and one file attach.
- **Effort**: S
- **Migration/rollback concern**: none (additive entitlement).

### PROD-1001: Android has no `INTERNET` permission in any checked-in manifest
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `app/android/app/src/main/AndroidManifest.xml:1-45` contains no `<uses-permission>`; only `src/main` and `src/profile` manifests exist (glob-verified) — the standard `src/debug/AndroidManifest.xml` overlay that grants INTERNET for dev is absent — FACT.
- **Current behavior**: Release builds have no INTERNET grant at all; debug builds don't either (missing debug overlay). Android enforces INTERNET for every socket.
- **Why it matters**: (INFERENCE) all network I/O — gRPC channel, OTLP export, OIDC — throws `SecurityException`/socket failures on any Android device; the target is non-functional. Severity Medium not High only because Android is not a current shipping target (AppHost drives desktop + web).
- **OS/product consequence**: Blocks the mobile client journey entirely when it is attempted.
- **Recommendation**: (PROPOSAL) add `<uses-permission android:name="android.permission.INTERNET"/>` to the main manifest (the app needs it in all build types), restore the debug overlay, and plan a debug-only `networkSecurityConfig` for plain-HTTP dev kernels (cleartext is blocked by default on API 28+).
- **Deletion/simplification opportunity**: no.
- **Dependencies**: PROD-1002 (RECORD_AUDIO belongs in the same edit).
- **Tests/measurements required**: `flutter run -d android` completing a kernel round-trip.
- **Effort**: S
- **Migration/rollback concern**: none.

### PROD-1002: Microphone capability undeclared on Android, iOS and macOS despite shipping voice input
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: pubspec `record: ^6.1.2`; `app/lib/features/brain/voice_input.dart` uses it; registrants confirm `record_windows` / `record_linux` / `RecordMacOsPlugin` are compiled in. Missing: `RECORD_AUDIO` in `app/android/app/src/main/AndroidManifest.xml`, `NSMicrophoneUsageDescription` in `app/ios/Runner/Info.plist:1-71` and `app/macos/Runner/Info.plist:1-33`, `com.apple.security.device.audio-input` in both `app/macos/Runner/*.entitlements` — FACT. Requirements verified against pub.dev/packages/record setup docs (2026-07-13).
- **Current behavior**: Voice input can only work on Windows/Linux/web (no manifest gate there). On iOS the OS *kills* an app that requests mic access without the usage string; Android/macOS requests fail.
- **Why it matters**: (INFERENCE) a shipped feature is broken on three of six scaffolded platforms; the iOS failure mode is a hard crash, the worst UX class.
- **OS/product consequence**: Voice → INO input journey unavailable/crashing outside the current dev platform.
- **Recommendation**: (PROPOSAL) declare `RECORD_AUDIO` (Android), `NSMicrophoneUsageDescription` (iOS + macOS), `com.apple.security.device.audio-input` (macOS both entitlement files) with an honest usage string; alternatively gate voice_input by platform until done.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: PROD-1000, PROD-1001.
- **Tests/measurements required**: record start/stop smoke test per platform.
- **Effort**: S
- **Migration/rollback concern**: iOS App Store review requires the usage string to be meaningful.

### REL-1000: Production web bootstrap is gated behind a fetch of `kernel_port.txt` that nothing produces
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `app/web/index.html:51-76` — `fetch('kernel_port.txt?t='+Date.now())` and only then `bootstrapFlutter()`; repo-wide grep finds no producer of `kernel_port.txt`; `.github/workflows/deploy.yml:77` builds this index.html into the production site — FACT.
- **Current behavior**: Every production page load performs a cache-busted request that 404s, logs a console warning, then falls back to loading `flutter_bootstrap.js`. `window.KERNEL_PORT` is always null in production; `app/lib/grpc/endpoint.dart:8` resolves the port from the `?port=` query param instead.
- **Why it matters**: (INFERENCE) dead dev machinery on the hot path: one wasted RTT serialized before engine load on every visit (worst on high-latency mobile), console noise on the marketing/product domain, and a misleading mechanism future readers will assume works.
- **OS/product consequence**: Slower first paint of the OS shell for every web user; confusing bootstrap contract.
- **Recommendation**: (PROPOSAL) delete the fetch gate and load `flutter_bootstrap.js` unconditionally; if dynamic port discovery is ever needed for dev web, run the fetch in parallel (don't gate) or have the AppHost inject the port into the served page.
- **Deletion/simplification opportunity**: yes — ~20 lines of index.html.
- **Dependencies**: `app/lib/grpc/endpoint.dart`, `app/lib/telemetry/platform_env_web.dart` (frontend subsystem).
- **Tests/measurements required**: web build loads with no 404 in console; endpoint resolution via `?port=` still works in dev.
- **Effort**: S
- **Migration/rollback concern**: confirm no local dev script (outside the repo) writes `kernel_port.txt` into the web root before deleting.

### SEC-1000: Android release build signs with the debug keystore
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `app/android/app/build.gradle.kts:33-39` — `release { signingConfig = signingConfigs.getByName("debug") }` with the stock TODO — FACT.
- **Current behavior**: Any `flutter build apk --release` is debug-signed.
- **Why it matters**: (INFERENCE) debug-signed artifacts are not distributable and, if ever shared, are trivially re-signable/impersonatable; also masks the missing real signing setup.
- **OS/product consequence**: Blocks trustworthy distribution of the Android shell; no immediate exposure while Android is unshipped.
- **Recommendation**: (PROPOSAL) leave as-is until Android is a target, then add `key.properties`-driven signing (the .gitignore already anticipates it).
- **Deletion/simplification opportunity**: no.
- **Dependencies**: PROD-1001.
- **Tests/measurements required**: signed-release verification (`apksigner verify`).
- **Effort**: S
- **Migration/rollback concern**: none.

### SEC-1001: No Content-Security-Policy on the production web page
- **Severity**: Note
- **Confidence**: Medium
- **Evidence**: `app/web/index.html:1-89` has no CSP meta tag and the static host (GitHub-Pages-style deploy per `.github/workflows/deploy.yml`) sets no CSP header — FACT (header behavior inferred from host type — INFERENCE).
- **Current behavior**: The page itself loads only same-origin resources; the running app, however, renders server-driven UI (rfw), embeds YouTube iframes, and google_fonts may fetch fonts at runtime.
- **Why it matters**: (INFERENCE) a CSP is cheap defense-in-depth for an app whose UI is partially server-driven; without it any future injected/inline script runs unconstrained on the product origin.
- **OS/product consequence**: Web trust boundary relies solely on app-level discipline.
- **Recommendation**: (PROPOSAL) when hardening the web target, add a CSP (via host headers preferably) permitting self + the specific frame/font/connect origins the app actually uses.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: frontend subsystem (rfw, youtube_player_iframe, google_fonts usage).
- **Tests/measurements required**: app functions fully under the candidate policy (report-only first).
- **Effort**: M
- **Migration/rollback concern**: overly strict CSP can break Flutter web (wasm/eval requirements) — needs report-only rollout.

### ARCH-1000: Vendored Aspire skills duplicate/conflict with CLAUDE.md's way-of-working and have no refresh process
- **Severity**: Note
- **Confidence**: High
- **Evidence**: `.agents/skills/aspire-orchestration/SKILL.md:52-64` mandates `aspire start`/`aspire wait`, "avoid `aspire run` in agent workflows" (`references/app-commands.md:17`), while `CLAUDE.md` prescribes `aspire run`, MCP-tool-driven restarts and its own test loop; all six skills are `author: Microsoft`, `version: "0.0.1"` snapshots (commits `3d367fe`/`9bdeefc`) with self-referential "project-local override" text — FACT.
- **Current behavior**: Two independent instruction authorities for the same operational loop coexist; the skills are pinned snapshots with no recorded update mechanism.
- **Why it matters**: (INFERENCE) agents may follow whichever they load first; guidance drift as Aspire evolves past 13.4; `aspire-init` skill is inert for this repo (AppHost exists).
- **OS/product consequence**: Inconsistent agent behavior in the self-evolution/dev loop the repo treats as a product surface.
- **Recommendation**: (PROPOSAL) pick one authority: reference the skills from CLAUDE.md (and delete overlapping prose there), record "refresh via `aspire agent init` on CLI upgrade" as the update path, and consider deleting the inert `aspire-init` skill directory.
- **Deletion/simplification opportunity**: yes — `.agents/skills/aspire-init/` (3 files) and overlapping CLAUDE.md prose.
- **Dependencies**: CLAUDE.md ownership (repo-meta subsystem).
- **Tests/measurements required**: none (process change).
- **Effort**: S
- **Migration/rollback concern**: none; skills are regenerable.

### CLEAN-1000: Inconsistent application identity across platforms; default project-name branding in user-visible strings
- **Severity**: Low
- **Confidence**: High
- **Evidence**: Android label `digitalbrain_flutter` (`app/android/app/src/main/AndroidManifest.xml:3`); Linux window title `digitalbrain_flutter` (`app/linux/runner/my_application.cc:48,52`) and `APPLICATION_ID io.digitalbrain.digitalbrain_flutter` (`app/linux/CMakeLists.txt:10`) vs `io.digitalbrain.app` on Android/macOS/iOS; Windows title `digitalbrain_flutter` (`app/windows/runner/main.cpp:30`); iOS display name "DigitalBrain Flutter" (`app/ios/Runner/Info.plist:9-10`); default Flutter icons everywhere — FACT.
- **Current behavior**: Users on the primary Windows target see a window titled `digitalbrain_flutter` with the stock Flutter icon.
- **Why it matters**: (INFERENCE) cosmetic, but the shell is the product's face; inconsistent bundle IDs also complicate future store/deep-link setup.
- **OS/product consequence**: Perceived polish of the OS shell.
- **Recommendation**: (PROPOSAL) one pass to set title/label/icon and align the Linux application id to `io.digitalbrain.app`.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: none.
- **Tests/measurements required**: visual check per platform.
- **Effort**: S
- **Migration/rollback concern**: changing Android applicationId later (after any install base) is breaking — do it before shipping.

### CLEAN-1001: Heavy duplication inside vendored monitoring skill content
- **Severity**: Note
- **Confidence**: High
- **Evidence**: The standalone-dashboard/`--dashboard-url` sections appear near-verbatim in `.agents/skills/aspire-monitoring/SKILL.md:116-147`, `references/monitoring.md:119-145`, and `references/diagnostics-bridge.md:90-123` — FACT.
- **Current behavior**: Redundant text inflates agent context loads.
- **Why it matters**: (INFERENCE) token/context waste when agents load multiple files; but the files are vendored and regenerated, so hand-editing is churn.
- **OS/product consequence**: Marginal agent-loop cost.
- **Recommendation**: (PROPOSAL) do nothing locally; upstream if it matters.
- **Deletion/simplification opportunity**: only upstream.
- **Dependencies**: ARCH-1000.
- **Tests/measurements required**: none.
- **Effort**: S
- **Migration/rollback concern**: none.

### TEST-1000: iOS and macOS Runner test targets are empty placeholders
- **Severity**: Note
- **Confidence**: High
- **Evidence**: `app/ios/RunnerTests/RunnerTests.swift:7-10` and `app/macos/RunnerTests/RunnerTests.swift:7-10` — a single empty `testExample()` each — FACT.
- **Current behavior**: Native test targets exist but assert nothing (stock template).
- **Why it matters**: (INFERENCE) zero native code exists to test; harmless, but they can create the illusion of native coverage.
- **OS/product consequence**: none today.
- **Recommendation**: (PROPOSAL) leave until native code appears.
- **Deletion/simplification opportunity**: optional deletion.
- **Dependencies**: none.
- **Tests/measurements required**: n/a.
- **Effort**: S
- **Migration/rollback concern**: none.

### REL-1001: No Podfile checked in for iOS/macOS despite native-plugin dependencies (dependency-manager path unverified)
- **Severity**: Note
- **Confidence**: Low
- **Evidence**: No `app/ios/Podfile` or `app/macos/Podfile` exists; pubspec pulls plugins with native iOS/macOS code (record, file_picker, media_kit, webview_flutter_wkwebview) — FACT.
- **Current behavior**: Apple-platform builds must generate CocoaPods files on first build, or rely on Flutter's Swift Package Manager flow.
- **Why it matters**: (INFERENCE) if the CocoaPods path is required by any plugin, the first Apple build mutates the tree with files that should be committed; whether Flutter 3.41's SPM flow covers all these plugins could not be doc-verified (Context7 monthly quota exhausted during this audit — documentation gap recorded per audit standard).
- **OS/product consequence**: Possible first-build friction on Apple platforms.
- **Recommendation**: (PROPOSAL) when iOS/macOS become targets, run a build and commit whichever dependency-manager artifacts Flutter generates.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: PROD-1000/1002.
- **Tests/measurements required**: clean-clone `flutter build macos`/`ios`.
- **Effort**: S
- **Migration/rollback concern**: none.

---

## Answers to subsystem-specific questions

**Android — permissions, cleartext, exported components, SDK levels.** The manifest declares *zero* permissions: no INTERNET (and the debug overlay that normally grants it is missing) and no RECORD_AUDIO — the problem is under-declaration, not over-breadth (PROD-1001/1002). Cleartext traffic is *not* enabled and there is no network-security-config; exported components are exactly one: the launcher activity (`exported="true"` with only MAIN/LAUNCHER, `taskAffinity=""` — safe). min/target/compileSdk are delegated to the Flutter tool's defaults (`flutter.minSdkVersion` etc., `app/android/app/build.gradle.kts:22-31`) — nothing pinned locally; `record` needs minSdk ≥23, which current Flutter defaults satisfy. Release signing uses the debug keystore (SEC-1000).

**iOS/macOS — usage strings, ATS, entitlements, sandbox/JIT vs Foundry.** iOS Info.plist has no usage-description strings at all; the required `NSMicrophoneUsageDescription` is missing (crash on mic request — PROD-1002). No ATS exceptions are declared (and none are needed for the gRPC path, which uses dart:io sockets outside ATS). macOS: sandbox is **enabled** in both configurations, hardened-runtime JIT (`cs.allow-jit`) is allowed **only in debug/profile**, and the release entitlements are minimal — the desktop shell does **not** disable the sandbox or allow arbitrary JIT/exec, so it is not a Foundry code-execution vector; the defect is the opposite direction: missing `network.client` (app can't connect at all — PROD-1000) plus missing mic entitlement/usage string.

**Web — external loads, CSP, manifest.** `index.html` loads no external scripts, fonts, or CDN resources — everything is same-origin. The two hand-added behaviors are a client-side www→apex redirect and the dead `kernel_port.txt` bootstrap gate (REL-1000). There is no CSP (SEC-1001, Note). `manifest.json` is a clean, minimal PWA manifest with maskable icons.

**Are permissions minimal and justified?** They are *below* minimal: every platform gate the app's features need (network on Android/macOS, mic on Android/iOS/macOS, sandbox file access on macOS) is missing, while nothing unnecessary is requested anywhere. Windows and web work only because those platforms impose no equivalent declarations.

**.agents/skills — what are they, do they belong, secrets/unsafe automation?** Six vendored Microsoft Aspire agent-skill bundles (MIT, `version 0.0.1`) installed by `aspire agent init`/`aspire init`. They belong checked in — Aspire's own precedence model expects project-local copies. No secrets (grepped; only instructional placeholders like `${{ secrets.AZURE_CLIENT_ID }}` in template YAML) and no unsafe automation — destructive commands are explicitly gated ("`--yes` only after destructive intent is explicit") and the reference workflows pin actions by SHA. Real issues are governance-level: duplicated/conflicting authority with `CLAUDE.md` and no recorded refresh process (ARCH-1000), plus an inert `aspire-init` skill this repo can never legitimately use.
