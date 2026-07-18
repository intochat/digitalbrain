# Assistant Telegram Aspire Parameters

## Purpose
Quick reference for Telegram-related Aspire parameters used by `tripradar-bot`.

## Where to set values
1. Start AppHost: `dotnet run --project src/Aspire/Aspire.csproj`.
2. Open the Aspire dashboard.
3. Open the `Parameters` section.
4. Set the values listed below.

## Parameters
1. `telegram-bot-token`
Secret Telegram bot token.
Used as:
- `TELEGRAM_BOT_TOKEN`
- `Bot__BotToken`

2. `telegram-session-sync-secret`
Secret used to authenticate session sync requests between MiniApp and bot.
Used as:
- `Bot__SessionSyncSecret`

## Notes
1. `telegram-bot-token` is intentionally single and shared across Aspire hosting extensions.
2. Webhook URL is automatically discovered from cloudflared quick tunnel logs — no manual parameter needed.
