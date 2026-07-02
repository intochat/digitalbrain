# Telegram Channel: Real Marketplace Setup, Flutter Mini App, Chat-Driven Authoring

- **Status:** Draft — pending user review
- **Date:** 2026-07-02
- **Owner:** Vladyslav Horbachov
- **Repo:** `digitalbraintech/framework` (`brain/`); touches `digitalbraintech/app` (`app/`)
- **Branch base:** `master` (independent of `spec/marketplace-cleanup` — see §6). New branch: `spec/telegram-marketplace-miniapp`.

## 0. TL;DR

The target experience: go to the marketplace, tap **Setup** on the Telegram channel, paste a bot token (+ LLM provider/key), and the bot is live — no AppHost restart. From Telegram, open a **Flutter Mini App** that gives full access to the brain (same UI as digitalbrain.tech), and talk to the bot to **author new neurons and synapses**, not just chat with an LLM.

An audit of the current code (2026-07-02) found the backend plumbing for the config-driven bot is real and mostly correct, but the end-to-end loop **does not work today**:

1. **The marketplace "install" of the Telegram pack has no matching "publish" in production.** `MarketplaceNeuron.HandleAsync(ListPublished)` (`DigitalBrain.Kernel/MarketplaceNeuron.cs:192-198`) unconditionally calls `InstallFromMarketplace("DigitalBrain.Telegram.Responder", "0.1.0", "local-dev")`, but nothing publishes that pack in production code — only test steps do (`DigitalBrain.Tests/Steps/TelegramReactiveLoopSteps.cs:67-71`). `FindPublishedPack` fails, install silently no-ops.
2. **No UI ever navigates to the config form.** `GeneratedNeuron.EmitConfigFormIfRequiredAsync` (`DigitalBrain.Kernel/GeneratedNeuron.cs:88-99`) correctly builds and emits a `ConfigFormSurface` (`Kind == "pack-config-form"`, `DigitalBrain.Core/Configuration.cs`) after install, and its submit button already encodes a generic `ConfigurationProvided` synapse the client can `Send` (proven end-to-end only by `DigitalBrain.Tests/Steps/ConfigFormSteps.cs`). But nothing in `app/lib/features/.../forui_app_shell.dart` or `experience_host_screen.dart` recognizes a `pack-config-form` surface and shows it. The one screen that might have hosted this, `marketplace_screen.dart`, is dead code ("MIGRATED... kept for reference only").
3. **Today the only way the bot actually works is `Telegram__BotToken` injected as an Aspire parameter/env var at host level** — bypassing the pack/config system entirely. That's a developer path, not a marketplace path.
4. **There is no Flutter Telegram Mini App.** No chat-reachable authoring exists either — the Telegram flow is pure passthrough (`message → AskLlm → LlmResponderNeuron → reply`), with zero access to MCP tools (`ask_ino`, `publish_to_marketplace`).

This spec closes all three gaps as three ordered, independently shippable slices:

- **Slice 1 — Fix the setup loop.** Real publish + a dedicated "Channels" entry point that installs the pack and surfaces its config form.
- **Slice 2 — Flutter Telegram Mini App.** Serve the existing Flutter web app through Telegram, authenticated via Telegram's `initData`, themed natively.
- **Slice 3 — Chat-driven authoring.** Let the bound owner create neurons/synapses by talking to the bot, via a new kernel-resident bridge to existing MCP authoring tools.

## 1. Slice 1 — Fix the marketplace bot-setup loop

**Why it's not a generic "Open a bundle" fix:** the 2026-07-02 marketplace-cleanup spec (`docs/specs/2026-07-02-marketplace-cleanup-design.md`) fixes a *different* bug — the `ExperienceUsed{action:"open"}` → hop-start bridge for content bundles (ForUI gallery, GmailInsights, TrixxterMonitoring) — and explicitly excludes Telegram as a non-goal. The Telegram pack was already deliberately excluded from `LocalUiPacks` because its lifecycle (configure once, then run persistently as a channel) is fundamentally different from "open and view content." It needs its own presentation, not a slot in the content-bundle grid.

**Backend:**
- Publish `DigitalBrain.Telegram.Responder` for real at kernel startup, the same way `LocalUiPacks` are seeded, but keep it out of the generic content list — it belongs to a new, small **"Channels"** grouping (v1: just Telegram) that the marketplace neuron exposes separately from `ListPublished`'s bundle grid.
- No new RPC needed for saving config — `ConfigurationProvided` (`DigitalBrain.Core/Configuration.cs:6-9`) is already a generic synapse the gateway's existing `Send` path accepts and `PackConfigStore` already persists correctly (proven by `PackConfigStoreTests.cs`, `PackConfigPullTests.cs`). The gap is purely "nobody shows the form," not "the form doesn't work."
- After config is saved, `TelegramReplyDispatcher.PullConfigAndApplyAsync` (`DigitalBrain.Telegram.Transport/TelegramReplyDispatcher.cs:50-77`) already pulls it and rebuilds the bot client — this is real and tested. Surface a `PackConfigured` status back to the client (the dispatcher already emits/handles this type) so the UI can show "Connected as @yourbot" instead of a silent success.

**Frontend:**
- Add a "Channels" card/section (new, minimal — one card for v1) reachable from wherever the live marketplace/installed screen lives today. Tapping **Setup** fires the pack's normal install path (same mechanism as any other pack), then the shell must recognize the resulting `UiSurface` with `Kind == "pack-config-form"` arriving over the home-feed stream and render it using the *already-correct* generic `ui:*` tree renderer (proven by `app/test/features/experience/config_form_tree_test.dart` — it just needs a caller, not new rendering logic).
- Delete or repurpose `marketplace_screen.dart` rather than resurrecting it — it predates the current generic surface model.
- Show connection status (bot username, "Connected"/"Not configured") on the Channels card by watching for `PackConfigured`.

**Why independent of marketplace-cleanup, not blocked on it:** both bugs are "a `UiSurface` was correctly emitted but the shell never navigates to it," but they're different surface kinds (`pack-config-form` vs. the Open→hop-start bridge) living in different code paths. Implementing this slice does not require marketplace-cleanup to land first. Flagging the shared pattern for whoever implements both, so the shell's surface-routing logic converges rather than growing two parallel special cases.

## 2. Slice 2 — Flutter Telegram Mini App

**Goal:** the existing Flutter app (unchanged — no bespoke mini-app UI) opens inside Telegram via the bot's menu button, authenticated automatically via Telegram identity, themed to match Telegram's light/dark mode live.

**Serving — reuse `ino`'s proven single-origin pattern:** `DigitalBrain.Telegram.Transport` (the existing webhook Kestrel host, already tunneled) additionally serves the Flutter web build's static files and proxies gRPC-Web to the gateway. One origin, one HTTPS URL, no CORS — this is the exact pattern `Projects/ino/clients/Telegram/Ino.Telegram.Host` already validated (`docs/superpowers/specs/2026-04-11-unified-flutter-client-design.md` in that archive). Reuses infrastructure already stood up (the transport host + tunnel) instead of adding a new hosting resource.

**Auth — port TripRadar's proven server-side HMAC verification as-is.** This is the single most battle-tested piece harvested from prior art (byte-identical between the archived `Projects/ino/domains/travel/tripradar` copy and the live `E:\TripRadar` repo, so it's been stable across a full rewrite of everything else around it):
- New endpoint on the transport host, e.g. `POST /telegram/session`, validates the raw `initData` string server-side only (never trusts client-parsed fields): exclude `hash`, sort remaining keys, join with `\n`, compute `HMAC-SHA256(HMAC-SHA256("WebAppData", botToken), dataCheckString)`, compare with `CryptographicOperations.FixedTimeEquals`.
- Reject `auth_date` older than ~5 minutes or in the future (replay protection); reject duplicate query keys.
- On success, mint a session bound to the Telegram user id, tying into whatever the gateway uses today for chat-binding trust (extends the existing `TelegramChatNeuron` per-chat binding concept rather than inventing a new identity system).
- **HMAC verification proves identity, not authorization** — it confirms "this really is Telegram user X," not "X is allowed to use this brain." Since this is a personal, single-owner system (per the target use case: "setting up my whole brain"), the session endpoint must additionally check the verified user id against the same owner/bound-chat concept Slice 3 uses as its trust boundary, and reject/no-op the Mini App session for anyone else. Without this check, anyone who discovers the bot's `t.me` link could open the Mini App and see the whole brain, even though they could never trigger authoring via chat (Slice 3's gate). Both slices should share one owner-identity check, not define it twice.

**Client bridge (Flutter):** a small `dart:js_interop`/`package:web` wrapper around `window.Telegram.WebApp` — `getInitData()` (sent once to `/telegram/session` on boot), `colorScheme` + a `themeChanged` listener feeding Flutter's theme live (not read-once), and `CloudStorage` for anything that needs to persist, with `sessionStorage`/`localStorage` as a fallback since CloudStorage isn't guaranteed available.

**Bot registration:** extend `TelegramWebhookSetup` to also call `setChatMenuButton` with a persistent `MenuButtonWebApp` pointing at the Mini App URL, and register an inline "Open App" button on `/start` — both, not just one (per ino's finding that relying on only the menu button misses users who never open it).

## 3. Slice 3 — Chat-driven creation of neurons and synapses

**Goal:** from Telegram (chat or the mini-app's chat surface), the owner can ask the bot to create something — e.g. "watch for X and do Y" — and the system actually authors, compiles, and embodies a new pack via the existing MCP tools, closing the full author→pack→marketplace→install→embodied→live loop from a phone.

**Architecture — a second kernel-resident responder, parallel to the existing one:**
- Today every Telegram message becomes an `AskLlm` synapse handled by `LlmResponderNeuron`. This slice adds an intent step: when the LLM (already invoked per message) determines the user is asking to create/modify a neuron — v1 keeps this simple with an explicit trigger (e.g. a `/create` command, or the LLM call using function-calling against a small fixed toolset: `ask_ino`, `publish_to_marketplace`, `list_marketplace`) — the responder emits a new `AuthoringIntent` synapse instead of a plain `AskLlm`.
- A new kernel-resident `AuthoringBridgeNeuron : IHandle<AuthoringIntent>` is the *only* thing permitted to invoke MCP authoring tools on behalf of a chat. This preserves the pure-pack model: `IPackBehavior` stays pure/sync/no-services (per the locked architecture decision from the original Telegram experience spec — see project memory `telegram-llm-experience`); only a kernel-resident neuron touches external tools, exactly like `LlmResponderNeuron` is the only thing that touches `IChatClient`.
- After a pack is authored/published/installed via this path, the chat gets a reply describing what was created, with a deep link into the Slice 2 mini-app to view/configure the new neuron — closing the loop back into the visual surface.

**Security — gated to the owner chat only.** MCP authoring tools compile and embody arbitrary code into the running kernel; this is the most consequential decision in this spec and needs explicit confirmation (flagged in §7). Default: only the chat that completed Slice 1's setup (the binding-owner chat, extending `TelegramChatNeuron`'s existing `/start <bundleId>` binding concept as the trust boundary) may trigger `AuthoringIntent`. Any other chat talking to the bot stays on the existing passthrough `AskLlm` path — no authoring capability.

## 4. Testing (TDD-first, matching this repo's convention)

- **Slice 1:** a `BundleHarness`-style test that publishes the Telegram pack at startup and asserts `InstallFromMarketplace` actually succeeds (currently would fail — write it first). A Flutter widget test proving a `pack-config-form` surface arriving over the home-feed stream results in the form being shown (not silently dropped). An integration test: install → submit `ConfigurationProvided` → `PullConfigAndApplyAsync` rebuilds the bot client → `PackConfigured` status observable by the client.
- **Slice 2:** server-side unit tests for `initData` HMAC validation ported directly from TripRadar's test suite (valid signature, tampered payload, stale `auth_date`, duplicate keys — all cases TripRadar already covers). A Playwright/E2E test (per `DigitalBrain.Tests/E2E`) driving the Flutter app through the transport host's single origin.
- **Slice 3:** a deterministic fake-MCP-tool test proving `AuthoringIntent` → `AuthoringBridgeNeuron` → `publish_to_marketplace` produces an installed, embodied pack (mirrors the existing `TrixxterMonitoring`-style `BundleHarness` pattern). A security test proving a non-owner chat's authoring attempt is rejected/ignored.

## 5. Verification ritual

```
dotnet build Brain.slnx
dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "Telegram|Marketplace|Config|Authoring"
aspire doctor
cd app && flutter analyze && flutter test
aspire run   # manual: marketplace → Channels → Setup → paste token → bot responds in Telegram;
             # open Mini App from bot menu button; ask bot to create a neuron, confirm it's installed
```

## 6. Sequencing note

This spec is written to be implementable independently of `spec/marketplace-cleanup` (base off `master`, not that branch) since the two touch different surface kinds. If `spec/marketplace-cleanup` lands first, Slice 1's implementer should check whether its shell surface-routing refactor already generalizes cleanly to `pack-config-form` and reuse it rather than adding a second special case.

## 7. Open questions / decisions flagged for user confirmation

These were defaulted under reasonable judgment because the brainstorming session's scoping question timed out (user away). Flag on return:

1. **Scope of this spec:** defaulted to all three slices in one spec (matching how the original `telegram-llm-experience` spec shipped 7 ordered slices in one plan), rather than splitting into 3 separate specs. Confirm this is still right, or split Slice 2/3 out.
2. **Authoring safety gating (Slice 3):** defaulted to owner-chat-only via the existing per-chat binding concept. Confirm this is the right trust boundary — alternatives include a confirmation step before any publish/install action regardless of chat, or an explicit allow-list separate from binding.
3. **Mini App scope (Slice 2):** defaulted to reusing the full existing Flutter app unchanged (just a new entry point + theming layer), not a bespoke trimmed-down mini-app UI. Confirm, or specify which subset of the app matters most for the Telegram surface.
4. **"Channels" as a new UI concept (Slice 1):** defaulted to a new, separate, minimal section distinct from the content-bundle marketplace grid, since Telegram's configure-once/run-persistently lifecycle doesn't fit the "open and view" model the 3 kept bundles use. Confirm this matches intent, or if Telegram should visually live inside the marketplace grid after all (with special-cased tap behavior).

## 8. Non-goals

- No changes to the 3-bundle content marketplace narrowing itself (that's `spec/marketplace-cleanup`'s job).
- No new LLM provider integrations beyond what `LlmResponderNeuron` already supports.
- No sandboxing/open-publishing changes — authoring in Slice 3 uses the exact same MCP tools and trust model (signed packs, ECDSA) that already exist; it only adds a new *caller* (Telegram chat) to tools that already exist.
- No redesign of the marketplace/installed screen beyond adding the Channels section.
