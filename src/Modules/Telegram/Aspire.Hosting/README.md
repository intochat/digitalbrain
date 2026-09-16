# Telegram module hosting

Like UI's `WithWindowHost`, configure Telegram through its module builder:

```csharp
builder.AddDigitalBrain("brain")
    .AddModule<TelegramModule>(telegram => telegram.WithBot());
```

The projection adds secret parameters, a named `telegram` HTTP endpoint on the consuming kernel, and local Flutter build/tunnel resources. No Gateway project or extra .NET process remains. `IModule.Configure(IEndpointRouteBuilder)` owns the webhook, Mini App and status endpoints. The `IBot` neuron owns durable incoming receipts.

The kernel has two listeners. Its usual `http` listener retains owner APIs. The `telegram` listener permits explicitly public Telegram and Identity endpoints plus owner-session-protected UI workspace APIs. Other APIs, health and OAuth callbacks return 404 there. Module ownership alone does not grant access. Cloudflare forwards only to that listener. `DigitalBrain__Telegram__PublicPort` is injected from its target port; no Host or forwarded header determines isolation.

Configuration on the AppHost:

- `Telegram:Enabled=false`: omit onboarding resources and token prompts.
- `Telegram:PublicUrl=https://your-public-host`: use a stable HTTPS origin and omit cloudflared. Route that origin to the named `telegram` listener.
- `Telegram:TunnelMode=Named`: run a remotely managed tunnel using secret parameter `cloudflare-tunnel-token`. Requires PublicUrl and uses fixed `Telegram:PublicPort` (default 5181). Configure the Cloudflare hostname's service to `http://localhost:5181`. The connector receives `TUNNEL_TOKEN` in its environment.
- `Telegram:TunnelMode=External`: use an existing public origin without launching a connector. `Quick` discovers a development origin. Omitted mode preserves the earlier PublicUrl-based selection; explicit Named never falls back.
- `Telegram:CloudflaredCommand`: optional executable path.
- `DigitalBrain:FlutterCommand`: optional Flutter executable path.
- `WithBot(options => ...)` also accepts PublicUrl, MiniAppDirectory, CloudflaredCommand, TunnelMode and PublicPort.

Aspire prompts for `telegram-bot-token`, and generates/persists `telegram-webhook-secret`. It builds the shared desktop shell's Telegram entrypoint before kernel startup, using Flutter's incremental build cache. Manual build from `src/Modules/UI/Flutter/shell`: `flutter build web --release --target lib/main_telegram.dart --output build/telegram --base-href /telegram/app/`. The Telegram runtime project copies this bundle into build/publish output. The old standalone `Flutter/telegram` reminder application is no longer served.

Quick mode uses a fresh tunnel log and bounded HTTPS-origin discovery. Restart the AppHost after a quick-tunnel process restart to re-register its new URL. Named mode keeps its hostname and port across restarts. Production publishing requires a stable public HTTPS origin or explicit disable. Tokens stay in the server; provider errors are sanitized and token-bearing HTTP traces suppressed. See [identity setup](../../../../docs/v2/IDENTITY.md) for owner bootstrap, Telegram account linking, session authentication and durable behavior grants.
