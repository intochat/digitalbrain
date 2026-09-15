# Telegram Aspire onboarding

`kernel.WithTelegramBot<Projects.DigitalBrain_Modules_Telegram_Gateway>()` enables the bot by default. Aspire prompts for the secret `telegram-bot-token` parameter and generates/persists `telegram-webhook-secret`. Neither secret is passed to the gateway or Flutter.

In local run mode, the gateway starts independently of the kernel. A direct `cloudflared` executable forwards only to that gateway. The kernel waits up to 90 seconds for an HTTPS quick-tunnel origin in a fresh, uniquely named log; failure has no localhost fallback. The kernel registrar verifies its own authenticated public health response before configuring Telegram.

Configuration on the **AppHost**:

- `Telegram:Enabled=false`: omit onboarding resources and token prompts.
- `Telegram:PublicUrl=https://your-public-host`: use a stable public origin and omit cloudflared. Route that origin to `telegram-gateway`, never the unrestricted kernel.
- `Telegram:CloudflaredCommand`: optional executable path, otherwise `cloudflared` on PATH.
- `DigitalBrain:FlutterCommand`: optional Flutter executable path, otherwise `flutter` on PATH.

The local Mini App bundle is built through an Aspire completion resource when missing. Existing builds are reused. After editing Flutter, rebuild `src/Modules/UI/Flutter/telegram` using `flutter build web --release --base-href /telegram/app/`, then refresh the Mini App.

Quick-tunnel URLs change. Restart the **AppHost** after a tunnel process exits or is restarted so that a new log/origin and registration belong to the same run. This integration does not silently loop/restart a tunnel while the kernel keeps a stale URL. Production publishing requires `Telegram:PublicUrl` or an explicit disable; quick tunnels are development-only.

The gateway forwards only `POST /telegram/webhook`, authenticated `GET /telegram/health`, the three exact Mini App API routes, and `GET`/`HEAD` assets below `/telegram/app/`. It strips queries, cookies, host/forwarding headers, method overrides, and response authentication cookies. Provider requests carry only the webhook-secret header; Mini App requests carry only their `tma` authorization header. The kernel still verifies both authentication schemes. Redirects cannot forward credentials to another origin.
