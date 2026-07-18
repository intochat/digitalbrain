# Cloudflared Aspire Integration — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Automate cloudflared tunnel startup via Aspire so the public tunnel URL flows automatically to the assistant silo's `TELEGRAM_WEBHOOK_URL` — zero manual steps, zero silo-side changes.

**Architecture:** Aspire launches `cloudflared` as a managed executable resource with `--logfile`. A custom health check polls the log for the `trycloudflare.com` URL. The silo `WaitFor`s cloudflared, then an env callback reads the resolved URL from the log and sets `TELEGRAM_WEBHOOK_URL` + `Telegram__WebhookUrl`. The generic cloudflared extension has no Telegram knowledge; the Telegram-specific mapping lives in `AssistantExtensions`.

**Tech Stack:** .NET Aspire (net11.0), `AddExecutable`, custom `IHealthCheck`, Aspire health check registration

**Design doc:** `docs/plans/2026-03-08-cloudflared-aspire-integration-design.md`

---

## Task 1: Create generic CloudflaredExtensions

**Files:**
- Create: `src/Aspire/Hosting/Cloudflared/CloudflaredExtensions.cs`

**Context:**
- The codebase uses C# 14 extension members (`extension(Type) { ... }`) — see `AssistantExtensions.cs`, `TelegramExtensions.cs` for the pattern.
- `AddExecutable(name, command, workingDirectory, args...)` is the Aspire API for launching external executables — see `UnityHostingExtensions.cs:45`.
- Health checks use `builder.Services.AddHealthChecks().AddCheck(name, instance)` + `.WithHealthCheck(name)` on the resource — see `WithHttpHealthCheck` usage in `TripRadarExtensions.cs:200`.
- Use Context7 to verify `AddExecutable`, `WithHealthCheck`, and `IHealthCheck` APIs before writing code.

**Step 1: Create CloudflaredExtensions.cs**

```csharp
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Aspire.Hosting.Cloudflared;

internal static partial class CloudflaredExtensions
{
    private const string TunnelUrlPattern = @"https://[a-z0-9-]+\.trycloudflare\.com";

    extension(IDistributedApplicationBuilder builder)
    {
        public IResourceBuilder<ExecutableResource> AddCloudflaredTunnel(
            string name,
            int localPort)
        {
            var logFilePath = Path.Combine(Path.GetTempPath(), "aspire-cloudflared", $"{name}.log");
            var logDir = Path.GetDirectoryName(logFilePath)!;

            if (!Directory.Exists(logDir))
                Directory.CreateDirectory(logDir);

            if (File.Exists(logFilePath))
                File.Delete(logFilePath);

            var healthCheckName = $"{name}-tunnel-ready";

            builder.Services.AddHealthChecks()
                .AddCheck(healthCheckName, new CloudflaredTunnelHealthCheck(logFilePath));

            return builder
                .AddExecutable(name, "cloudflared", ".", "tunnel", "--url", $"http://localhost:{localPort}", "--logfile", logFilePath)
                .WithHealthCheck(healthCheckName);
        }
    }

    internal static string? ExtractTunnelUrl(string logFilePath)
    {
        if (!File.Exists(logFilePath))
            return null;

        var content = File.ReadAllText(logFilePath);
        var match = TunnelUrlRegex().Match(content);
        return match.Success ? match.Value : null;
    }

    [GeneratedRegex(TunnelUrlPattern)]
    private static partial Regex TunnelUrlRegex();
}

internal sealed class CloudflaredTunnelHealthCheck(string logFilePath) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var url = CloudflaredExtensions.ExtractTunnelUrl(logFilePath);

        var result = url is not null
            ? HealthCheckResult.Healthy($"Tunnel URL: {url}")
            : HealthCheckResult.Unhealthy("Tunnel URL not yet available in log file");

        return Task.FromResult(result);
    }
}
```

**Step 2: Build to verify compilation**

Run: `dotnet build src/Aspire/Aspire.csproj`
Expected: Build succeeds

**Step 3: Commit**

```bash
git add src/Aspire/Hosting/Cloudflared/CloudflaredExtensions.cs
git commit -m "feat: add generic cloudflared tunnel Aspire integration"
```

---

## Task 2: Wire cloudflared into AssistantExtensions

**Files:**
- Modify: `src/Aspire/Hosting/Assistant/AssistantExtensions.cs`

**Context:**
- `AssistantResource` has `Builder` (the `IDistributedApplicationBuilder`) and `Silo` (the `IResourceBuilder<ProjectResource>`).
- `WithCloudflaredTunnel` calls the generic `AddCloudflaredTunnel`, then maps the resolved URL to Telegram env vars via a `WithEnvironment` callback.
- The callback reads the URL from the log file. Because `WaitFor(cloudflared)` ensures the silo doesn't start until the health check passes, the URL is guaranteed to be in the log by the time the callback runs.
- The env vars `TELEGRAM_WEBHOOK_URL` and `Telegram__WebhookUrl` are set by `TelegramExtensions.WithTelegram` from a parameter resource. Our callback runs AFTER that (since `WithCloudflaredTunnel()` is called after `WithTelegramBot()`), so it overrides the parameter-based value.

**Step 1: Add using + WithCloudflaredTunnel method**

Add `using Aspire.Hosting.Cloudflared;` to the top of the file.

Add a new `extension(AssistantResource assistant)` block (or add to the existing one) with:

```csharp
public AssistantResource WithCloudflaredTunnel(int localPort = 5151)
{
    var cloudflared = assistant.Builder.AddCloudflaredTunnel("cloudflared", localPort);

    var logFilePath = Path.Combine(Path.GetTempPath(), "aspire-cloudflared", "cloudflared.log");

    assistant.Silo
        .WaitFor(cloudflared)
        .WithEnvironment(context =>
        {
            var tunnelUrl = CloudflaredExtensions.ExtractTunnelUrl(logFilePath);
            if (tunnelUrl is not null)
            {
                context.EnvironmentVariables["CLOUDFLARED_TUNNEL_URL"] = tunnelUrl;
                context.EnvironmentVariables["TELEGRAM_WEBHOOK_URL"] = tunnelUrl;
                context.EnvironmentVariables["Telegram__WebhookUrl"] = tunnelUrl;
            }
        });

    return assistant;
}
```

**Important:** The log file path must use the same `name` ("cloudflared") as passed to `AddCloudflaredTunnel` so the paths match. This is a coupling point — if refactored later, extract the path into a shared constant or return it from `AddCloudflaredTunnel`.

**Step 2: Build to verify compilation**

Run: `dotnet build src/Aspire/Aspire.csproj`
Expected: Build succeeds

**Step 3: Commit**

```bash
git add src/Aspire/Hosting/Assistant/AssistantExtensions.cs
git commit -m "feat: wire cloudflared tunnel URL into assistant silo env vars"
```

---

## Task 3: Add WithCloudflaredTunnel to AppHost

**Files:**
- Modify: `src/Aspire/AppHost.cs`

**Context:**
- Current code at line 27-29:
  ```csharp
  var assistant = builder.AddAssistant()
      .WithReference(ai)
      .WithTelegramBot(bot => bot.UseLocalVoice2Text());
  ```
- Add `.WithCloudflaredTunnel()` AFTER `.WithTelegramBot()` so the env callback overrides the parameter-based webhook URL.

**Step 1: Add the call**

Change lines 27-29 to:

```csharp
var assistant = builder.AddAssistant()
    .WithReference(ai)
    .WithTelegramBot(bot => bot.UseLocalVoice2Text())
    .WithCloudflaredTunnel();
```

**Step 2: Build to verify**

Run: `dotnet build src/Aspire/Aspire.csproj`
Expected: Build succeeds

**Step 3: Commit**

```bash
git add src/Aspire/AppHost.cs
git commit -m "feat: enable cloudflared tunnel in AppHost"
```

---

## Task 4: Remove Shirubasoft.Aspire.Cloudflared from Directory.Packages.props

**Files:**
- Modify: `Directory.Packages.props` (line 204)

**Step 1: Remove the package version line**

Delete this line:
```xml
    <PackageVersion Include="Shirubasoft.Aspire.Cloudflared" Version="1.0.4" />
```

**Step 2: Verify no references to the package exist**

Run: `grep -r "Shirubasoft.Aspire.Cloudflared" --include="*.csproj" --include="*.props" src/`
Expected: No matches (it was declared but never referenced in any `.csproj`)

**Step 3: Build full solution to confirm nothing breaks**

Run: `dotnet build src/Aspire/Aspire.csproj`
Expected: Build succeeds

**Step 4: Commit**

```bash
git add Directory.Packages.props
git commit -m "chore: remove unused Shirubasoft.Aspire.Cloudflared package"
```

---

## Task 5: Integration test — run Aspire and verify tunnel

**Context:**
- `cloudflared` must be installed and in PATH.
- This is a manual verification step since the tunnel requires network access.

**Step 1: Run the Aspire AppHost**

Run: `dotnet run --project src/Aspire/Aspire.csproj`

**Step 2: Verify in Aspire dashboard**

- Open Aspire dashboard (default: `https://localhost:17223`)
- Confirm `cloudflared` appears as a resource with status "Running"
- Check cloudflared console logs for the tunnel URL line:
  ```
  Your quick Tunnel has been created! Visit it at:
  https://<random>.trycloudflare.com
  ```

**Step 3: Verify assistant silo received the URL**

- In the Aspire dashboard, check `assistant-host` resource environment variables
- Confirm `CLOUDFLARED_TUNNEL_URL`, `TELEGRAM_WEBHOOK_URL`, and `Telegram__WebhookUrl` all contain the `trycloudflare.com` URL

**Step 4: Verify Telegram webhook registration**

- Check `assistant-host` logs for: `Webhook registered successfully: https://<random>.trycloudflare.com/api/telegram/webhook`

**Step 5: Commit all if not already committed**

```bash
git commit --allow-empty -m "test: verified cloudflared tunnel integration works end-to-end"
```
