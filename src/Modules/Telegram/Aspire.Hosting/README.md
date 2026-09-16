# Telegram module hosting

Like UI's `WithWindowHost`, configure Telegram through its module builder:

```csharp
builder.AddDigitalBrain("brain")
    .AddModule<TelegramModule>(telegram => telegram.WithBot());
```

The projection adds secret parameters, a named `telegram` HTTP endpoint on the consuming kernel, and local Flutter build/tunnel resources. No Gateway project or extra .NET process remains. `IModule.Configure(IEndpointRouteBuilder)` owns the webhook, Mini App and status endpoints. The `IBot` neuron owns durable incoming receipts.

The kernel has two listeners. Its usual `http` listener retains owner APIs. The `telegram` listener permits only endpoints explicitly owned by Telegram; other APIs, health and OAuth callbacks return 404 there. Cloudflare forwards only to that listener. `DigitalBrain__Telegram__PublicPort` is injected from its target port; no Host or forwarded header determines isolation.

Configuration on the AppHost:

- `Telegram:Enabled=false`: omit onboarding resources and token prompts.
- `Telegram:PublicUrl=https://your-public-host`: use a stable HTTPS origin and omit cloudflared. Route that origin to the named `telegram` listener.
- `Telegram:CloudflaredCommand`: optional executable path.
- `DigitalBrain:FlutterCommand`: optional Flutter executable path.
- `WithBot(options => ...)` also accepts PublicUrl, MiniAppDirectory and CloudflaredCommand.

Aspire prompts for `telegram-bot-token`, and generates/persists `telegram-webhook-secret`. A missing bundle is built before kernel startup; existing bundles are reused. After Flutter changes, rebuild with `flutter build web --release --base-href /telegram/app/`. The Telegram runtime project owns bundle copying into build/publish output.

Each AppHost run uses a fresh tunnel log and bounded HTTPS-origin discovery. Restart the AppHost after a tunnel process restart to re-register its new URL. Production publishing requires a stable public HTTPS origin or explicit disable. Tokens stay in the server; provider errors are sanitized and token-bearing HTTP traces suppressed.
