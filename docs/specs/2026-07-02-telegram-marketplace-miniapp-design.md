# Telegram Bot: Make Marketplace Setup Actually Work (Minimal)

- **Status:** Approved scope — cut down from a 3-slice draft after user feedback ("too complicated, some thrashing — keep it minimal, but Telegram must work from marketplace")
- **Date:** 2026-07-02
- **Owner:** Vladyslav Horbachov
- **Repo:** `digitalbraintech/framework` (`brain/`); touches `digitalbraintech/app` (`app/`)
- **Branch:** `spec/telegram-marketplace-miniapp` (based off `master`)

## 0. TL;DR

One job: go to the marketplace, install the Telegram bot, paste a token, it works — no AppHost restart, no env vars. Everything else (Flutter Mini App, chat-driven authoring of neurons/synapses) is cut from this spec. Those ideas aren't wrong, they're just not now — see §4.

## 1. What's actually broken (corrected after direct code verification — see §1.1)

The backend is already fully wired and working, verified by direct reading (not delegated research) of every hop in the chain:

- `DigitalBrain.Telegram.Responder` (v1.0.0) **is** in `MarketplaceSeeds.LocalUiPacks` (`DigitalBrain.Core/MarketplaceSeeds.cs:177-184`) — it is not excluded.
- `MarketplaceNeuron.EnsureCache()` (`DigitalBrain.Kernel/MarketplaceNeuron.cs:195-215`) seeds every `LocalUiPacks` entry into `_publishedCache` via `MaterializeManifest`, which embodies the pack and pulls its real `GetBundleManifest()` (`BundleTier.Channel`, `[Telegram]`). Telegram.Responder is published, findable, and correctly tiered.
- `IsPreinstalledLocalPack` (`DigitalBrain.Core/UiSurfaces.cs:1456-1460`) only auto-marks `DigitalBrain.UI*`/`DigitalBrain.Experience*` packs as pre-installed — `DigitalBrain.Telegram.Responder` matches neither prefix, so it correctly shows as a normal, tappable "Install" tile.
- The generic Install dispatch (`GatewayService.Send`, `DigitalBrain.Kernel/Gateway/GatewayService.cs:50-65`) reads `packName`/`version` from whatever the client sends — no hardcoded pack name or version anywhere in this path.
- On install, `GeneratedNeuron.DispatchSynapse` (`DigitalBrain.Kernel/GeneratedNeuron.cs:41-44`) embodies the pack and calls `EmitConfigFormIfRequiredAsync()` (lines 88-99), which builds a `ConfigFormSurface` (`Kind == "pack-config-form"`) and **broadcasts it to `HomeFeedBus`** — the exact stream `WatchHomeFeed` (and therefore the Flutter client) is subscribed to. Its submit button already round-trips a working `ConfigurationProvided` synapse, handled correctly by `GatewayService.Send` (lines 69+) and persisted by `PackConfigStore`.

**The one real, verified bug:** `app/lib/shell/forui_app_shell.dart`'s `_onCard` (lines 136-201) stores any arriving surface in `_surfacesByKind[kind]` (line 187) but only ever auto-switches the visible body (`_selectedTarget`) for one hardcoded case — the UI Kit Gallery (lines 191-199, `isGallery` check). A `pack-config-form` surface lands in the map and **just sits there**, never displayed, because nothing sets `_selectedTarget = 'pack-config-form'`. This is the entire gap.

Today the only way the bot actually responds is `Telegram__BotToken` injected as an Aspire env var — not because install is broken, but because a user has no way to ever *see* the form that install correctly triggers.

### 1.1 Correction note

An earlier draft of this spec (based on delegated sub-agent research) claimed the Telegram pack was never published in production and required a new backend publish call. Direct verification while writing the implementation plan disproved this — `LocalUiPacks` already includes it. Lesson: sub-agent research is a starting point, not a citation to build a plan on without spot-checking the exact lines it claims — see the corresponding memory note for the broader takeaway.

## 2. Fix — one change, one file

**Frontend only.** Add a `pack-config-form` auto-switch to `_onCard`, structurally identical to the existing gallery auto-switch it sits next to:

```dart
final isConfigForm = kind == 'pack-config-form';
if (isConfigForm) {
  _selectedTarget = kind;
}
```

Once `_selectedTarget == 'pack-config-form'`, the existing generic body-rendering path in `build()` (`forui_app_shell.dart:337-344`) already renders it correctly via `_renderEnvelope` + the generic `ui:*` tree renderer — no new branch needed there, because `'pack-config-form'` doesn't match any of the existing `effectiveTarget.contains(...)` special cases (gallery/market/install), so it falls through to the plain default render. Verified this rendering path is already correct and tested by `app/test/features/experience/config_form_tree_test.dart` — it just has no caller today.

**No backend change.** Everything described in §1 already works.

**Sequencing note:** `spec/marketplace-cleanup` (not yet implemented) is about to fix a related-but-distinct bug — the `ExperienceUsed{action:open}` → hop-start bridge for content bundles, also in `forui_app_shell.dart`'s surface-routing area. Worth a glance for whoever implements this to keep the two auto-switch mechanisms consistent in shape, but they are separate surface kinds and neither blocks the other.

**Feedback on success:** reuse the existing `PackConfigured` synapse the dispatcher already emits/handles — a plain toast/snackbar is enough ("Connected as @yourbot"). No bespoke status card.

## 3. Testing

- A Flutter widget test on `_ForuiAppShellState`: a `pack-config-form` `RfwCardEnvelope` arriving via `_onCard` results in `_selectedTarget == 'pack-config-form'` and the form tree being rendered — fails today (no auto-switch exists), passes once the fix lands.
- Existing coverage already proves the rest of the chain and does not need new tests: `PackConfigStoreTests.cs`/`PackConfigPullTests.cs` (persistence), `ConfigFormSteps.cs` (form emission + submit round-trip), `config_form_tree_test.dart` (tree rendering correctness).

```
dotnet build Brain.slnx
dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "Telegram|Config"
aspire doctor
cd app && flutter analyze && flutter test
aspire run   # manual: marketplace → install Telegram bundle → paste token → confirm bot responds in Telegram
```

## 4. Explicitly deferred (not designed further here)

- **Flutter Telegram Mini App** (serving the app inside Telegram, `initData` auth). Real prior art exists to harvest when this comes up again: TripRadar's server-side HMAC-SHA256 `initData` verification is proven and portable as-is; `Projects/ino`'s single-origin serve-through-the-bot-host pattern avoids CORS. Worth its own spec once the setup loop above is proven manually.
- **Chat-driven creation of neurons/synapses** (bot as an authoring surface via MCP tools). Real security question to resolve when this comes up (who's allowed to trigger authoring from chat) — not resolved here on purpose.
- A distinct "Channels" UI concept — cut in favor of reusing the marketplace list as-is.

## 5. Non-goals

- No changes to the 3-bundle content marketplace narrowing (`spec/marketplace-cleanup`'s job).
- No new LLM provider integrations.
- No sandboxing/trust-model changes.
