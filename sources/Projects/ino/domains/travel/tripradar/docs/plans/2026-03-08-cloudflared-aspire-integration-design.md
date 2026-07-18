# Cloudflared Aspire Integration

## Problem

Starting the app requires manually running `cloudflared tunnel --url http://localhost:5151`, copying the generated public URL, and populating it in Aspire's `telegram-webhook-url` parameter. This should be fully automated.

## Solution

Add cloudflared as a managed Aspire executable resource. Parse the tunnel URL from cloudflared's log file at the Aspire level and pass the resolved public URL to the assistant silo via environment variables. Zero silo-side changes.

## Architecture

### Data Flow

```
Aspire starts cloudflared executable (with --logfile {tempPath})
        |
Health check polls log file for https://*.trycloudflare.com URL
        |
Resource marked healthy -> URL extracted from log
        |
Silo starts (WaitFor cloudflared) -> env vars set with resolved URL
        |
WebhookSetupService reads TELEGRAM_WEBHOOK_URL (existing code, unchanged)
```

### Components

**1. `src/Aspire/Hosting/Cloudflared/CloudflaredExtensions.cs` (new)**

Generic, reusable cloudflared tunnel resource. No Telegram knowledge.

- `AddCloudflaredTunnel(string name, int localPort)` extension on `IDistributedApplicationBuilder`
- Starts `cloudflared tunnel --url http://localhost:{port} --logfile {tempPath}`
- Deletes stale log file on startup
- Registers a custom health check (`CloudflaredTunnelHealthCheck`) that polls the log file
- Resource is "healthy" only when the trycloudflare.com URL is found in the log
- Exposes URL extraction utility for consumers
- Sets `CLOUDFLARED_TUNNEL_URL` env var (generic naming)

**2. `src/Aspire/Hosting/Assistant/AssistantExtensions.cs` (modify)**

Add `WithCloudflaredTunnel(int localPort = 5151)` extension on `AssistantResource`:

- Calls generic `AddCloudflaredTunnel` to create the resource
- `assistant.Silo.WaitFor(cloudflared)` ensures ordering
- Maps the resolved URL to Telegram-specific env vars:
  - `TELEGRAM_WEBHOOK_URL`
  - `Telegram__WebhookUrl`

**3. `src/Aspire/AppHost.cs` (modify)**

```csharp
var assistant = builder.AddAssistant()
    .WithReference(ai)
    .WithTelegramBot(bot => bot.UseLocalVoice2Text())
    .WithCloudflaredTunnel();  // new
```

**4. `Directory.Packages.props` (modify)**

Remove `Shirubasoft.Aspire.Cloudflared` v1.0.4 package reference.

## What's NOT touched

- `WebhookSetupService` — zero changes, reads `TELEGRAM_WEBHOOK_URL` as before
- `TelegramConfiguration.cs` — no new options classes
- `TelegramExtensions.cs` — untouched
- `TelegramHostOptionsBuilder.cs` — untouched
- `Program.cs` (Assistant.Silo) — untouched
- All ngrok code — completely untouched

## Edge Cases

- **cloudflared not installed**: executable fails to start, health check stays unhealthy, silo never starts (WaitFor blocks). Error visible in Aspire dashboard.
- **Tunnel takes too long**: health check keeps returning unhealthy until URL appears in logs.
- **User wants to disable**: remove `.WithCloudflaredTunnel()` from AppHost.cs — falls back to existing behavior (manual URL or ngrok).
- **Log file from previous run**: deleted on startup before cloudflared starts.

## Prerequisites

- `cloudflared` must be installed and available in PATH.
