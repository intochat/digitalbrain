# Telegram Bot: Make Marketplace Setup Actually Work (Minimal)

- **Status:** Approved scope — cut down from a 3-slice draft after user feedback ("too complicated, some thrashing — keep it minimal, but Telegram must work from marketplace")
- **Date:** 2026-07-02
- **Owner:** Vladyslav Horbachov
- **Repo:** `digitalbraintech/framework` (`brain/`); touches `digitalbraintech/app` (`app/`)
- **Branch:** `spec/telegram-marketplace-miniapp` (based off `master`)

## 0. TL;DR

One job: go to the marketplace, install the Telegram bot, paste a token, it works — no AppHost restart, no env vars. Everything else (Flutter Mini App, chat-driven authoring of neurons/synapses) is cut from this spec. Those ideas aren't wrong, they're just not now — see §4.

## 1. What's actually broken (unchanged from the fuller audit)

The backend config plumbing (`PackConfigStore`, the `GetPackConfig` RPC, the generic `ConfigurationProvided` synapse, `TelegramReplyDispatcher.PullConfigAndApplyAsync`) is real and already covered by tests. Two concrete gaps stop it from working end to end:

1. **No production publish.** `MarketplaceNeuron.HandleAsync(ListPublished)` (`DigitalBrain.Kernel/MarketplaceNeuron.cs:192-198`) calls `InstallFromMarketplace("DigitalBrain.Telegram.Responder", ...)`, but nothing publishes that pack outside test code (`DigitalBrain.Tests/Steps/TelegramReactiveLoopSteps.cs:67-71`). Install silently no-ops.
2. **No UI shows the form.** `GeneratedNeuron.EmitConfigFormIfRequiredAsync` (`DigitalBrain.Kernel/GeneratedNeuron.cs:88-99`) correctly emits a `ConfigFormSurface` (`Kind == "pack-config-form"`) after install — its submit button already round-trips a working `ConfigurationProvided` synapse. Nothing in `app/lib/.../forui_app_shell.dart` navigates to or renders a surface of that kind.

Today the only way the bot works at all is `Telegram__BotToken` injected as an Aspire env var — bypassing the pack/config system entirely. That's not the marketplace flow.

## 2. Fix — one slice, minimal surface area

**Backend:** publish `DigitalBrain.Telegram.Responder` for real at kernel startup, same mechanism already used to publish other seeded packs. That's the whole backend change — everything downstream of a successful install already works and is tested.

**Frontend:** teach the shell to recognize a `pack-config-form` surface arriving on the home-feed stream and render it via the existing generic `ui:*` tree renderer (already proven correct by `config_form_tree_test.dart` — it has no caller today, that's the entire gap). No new screen, no new "Channels" concept: the Telegram pack shows up as a normal tile in the existing marketplace list, tap installs it like any other pack, and the form that arrives after install is simply displayed instead of dropped.

**Sequencing note:** `spec/marketplace-cleanup` (not yet implemented) is about to fix the exact same category of bug for a different surface kind (`ExperienceUsed{action:open}` → hop-start content). Whoever implements this should check whether that fix already generalizes to `pack-config-form` and reuse it, rather than writing a second parallel surface-routing mechanism. One mechanism, not two — this is the specific thing to avoid re-thrashing on.

**Feedback on success:** reuse the existing `PackConfigured` synapse the dispatcher already emits/handles — a plain toast/snackbar is enough ("Connected as @yourbot"). No bespoke status card.

## 3. Testing

- A test publishing the pack at startup and asserting `InstallFromMarketplace` actually succeeds (fails today — write first).
- A Flutter widget test: a `pack-config-form` surface arriving over the home-feed stream results in the form being rendered, not dropped.
- One integration test covering install → submit `ConfigurationProvided` → `PullConfigAndApplyAsync` rebuilds the bot client → `PackConfigured` observed by the client.

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
