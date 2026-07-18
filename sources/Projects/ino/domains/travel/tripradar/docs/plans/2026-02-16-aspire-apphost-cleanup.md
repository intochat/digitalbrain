# Aspire AppHost Cleanup Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Refactor AppHost.cs from 71 lines of mixed imperative/declarative code to ~31 lines of purely declarative resource wiring.

**Architecture:** Extract Assistant domain into `Hosting/Assistant/` module. Add `.WithReference(infrastructure)` pattern on TripRadar to eliminate the `infrastructure.Kafka` leak. Delete custom `Resolve()` helper and use `builder.Configuration[]` natively. Move `otel-config.yaml` into `OpenTelemetry/` folder.

**Tech Stack:** .NET 11, Aspire 13.1.1, C# 13 extension members

**Design doc:** `docs/plans/2026-02-16-aspire-apphost-cleanup-design.md`

---

### Task 1: Move `otel-config.yaml` into `OpenTelemetry/` folder

**Files:**
- Move: `src/Aspire/otel-config.yaml` → `src/Aspire/OpenTelemetry/otel-config.yaml`
- Modify: `src/Aspire/Hosting/SharedInfrastructure/SharedInfrastructureExtensions.cs`

**Step 1: Create folder and move file**

```bash
mkdir -p src/Aspire/OpenTelemetry
git mv src/Aspire/otel-config.yaml src/Aspire/OpenTelemetry/otel-config.yaml
```

**Step 2: Update path reference**

In `src/Aspire/Hosting/SharedInfrastructure/SharedInfrastructureExtensions.cs`, change:
```csharp
// Line 22 — old:
.WithConfig("./otel-config.yaml")

// New:
.WithConfig("./OpenTelemetry/otel-config.yaml")
```

**Step 3: Build to verify**

```bash
dotnet build src/Aspire/Aspire.csproj
```

Expected: Build succeeded.

**Step 4: Commit**

```bash
git add src/Aspire/OpenTelemetry/otel-config.yaml src/Aspire/Hosting/SharedInfrastructure/SharedInfrastructureExtensions.cs
git commit -m "chore: move otel-config.yaml into OpenTelemetry folder"
```

---

### Task 2: Delete `Resolve()` from `TelegramExtensions`

**Files:**
- Modify: `src/Aspire/Hosting/Telegram/TelegramExtensions.cs`

**Step 1: Replace all `Resolve()` calls with `builder.Configuration[]`**

There are 4 usages in the `WithTelegramBot` method and the `Resolve` definition itself.

Replace lines 49, 36-46 where `Resolve(appBuilder, ...)` is called:
```csharp
// In WithTelegramBot — replace:
//   Resolve(appBuilder, MiniAppUrlEnvVar)
// With:
//   appBuilder.Configuration[MiniAppUrlEnvVar] ?? ""

// Same pattern for MiniAppPathEnvVar, MiniAppButtonTextEnvVar, NgrokApiUrlEnvVar
```

Full replacement in `WithTelegramBot`:
```csharp
var miniAppUrl = appBuilder.AddParameter(
    "telegram-mini-app-url",
    () => appBuilder.Configuration[MiniAppUrlEnvVar] ?? "",
    publishValueAsDefault: true);
var miniAppPath = appBuilder.AddParameter(
    "telegram-mini-app-path",
    () => appBuilder.Configuration[MiniAppPathEnvVar] ?? "/miniapp/",
    publishValueAsDefault: true);
var miniAppButtonText = appBuilder.AddParameter(
    "telegram-mini-app-button-text",
    () => appBuilder.Configuration[MiniAppButtonTextEnvVar] ?? "Open App",
    publishValueAsDefault: true);

builder.WithTelegram(botToken, webhookUrl)
    .WithEnvironment(NgrokApiUrlEnvVar, appBuilder.Configuration[NgrokApiUrlEnvVar] ?? "")
    // ...rest unchanged
```

**Step 2: Delete the `Resolve()` method**

Delete lines 108-118 (the private static `Resolve` method at the bottom of the file).

**Step 3: Build to verify**

```bash
dotnet build src/Aspire/Aspire.csproj
```

Expected: Build succeeded.

**Step 4: Commit**

```bash
git add src/Aspire/Hosting/Telegram/TelegramExtensions.cs
git commit -m "refactor: replace Resolve() with native Configuration API in TelegramExtensions"
```

---

### Task 3: Delete `Resolve()` from `TripRadarExtensions` and simplify parameter helpers

**Files:**
- Modify: `src/Aspire/Hosting/TripRadar/TripRadarExtensions.cs`

**Step 1: Replace local helper functions**

In the `AddTripRadar` method, replace the 4 local functions (lines 33-43):

```csharp
// Old:
IResourceBuilder<ParameterResource> AddDefaultParameter(string parameterName, string defaultValue) => ...
IResourceBuilder<ParameterResource> AddResolvedDefaultParameter(string parameterName, string environmentVariableName, string fallback = "") => ...
IResourceBuilder<ParameterResource> AddResolvedSecretParameter(string parameterName, string environmentVariableName, string fallback = "") => ...
IResourceBuilder<ParameterResource> AddSecretParameter(string parameterName) => ...

// New:
IResourceBuilder<ParameterResource> AddDefaultParam(string name, string defaultValue) =>
    builder.AddParameter(name, defaultValue, publishValueAsDefault: true);

IResourceBuilder<ParameterResource> AddSecretParam(string name) =>
    builder.AddParameter(name, secret: true);

IResourceBuilder<ParameterResource> AddConfigBackedParam(string name, string configKey, string fallback = "") =>
    builder.AddParameter(name, () => builder.Configuration[configKey] ?? fallback, publishValueAsDefault: true);

IResourceBuilder<ParameterResource> AddConfigBackedSecret(string name, string configKey, string fallback = "") =>
    builder.AddParameter(name, () => builder.Configuration[configKey] ?? fallback, secret: true);
```

**Step 2: Update all call sites to use new helper names**

Rename all calls within `AddTripRadar`:
- `AddSecretParameter(` → `AddSecretParam(`
- `AddDefaultParameter(` → `AddDefaultParam(`
- `AddResolvedDefaultParameter(` → `AddConfigBackedParam(`
- `AddResolvedSecretParameter(` → `AddConfigBackedSecret(`

**Step 3: Update `ResolveTelegramAuthBaseUrl` to use `builder.Configuration[]`**

```csharp
private static string ResolveTelegramAuthBaseUrl(IDistributedApplicationBuilder builder)
{
    var explicitAuthBaseUrl = builder.Configuration[TripRadarConstants.EnvironmentVariables.TelegramAuthBaseUrl];
    if (!string.IsNullOrWhiteSpace(explicitAuthBaseUrl))
        return explicitAuthBaseUrl;

    var miniAppUrl = builder.Configuration[TripRadarConstants.EnvironmentVariables.TelegramMiniAppUrl];
    var baseUrlFromMiniApp = TryGetUriAuthority(miniAppUrl ?? "");
    if (!string.IsNullOrWhiteSpace(baseUrlFromMiniApp))
        return baseUrlFromMiniApp;

    var webhookUrl = builder.Configuration[TripRadarConstants.EnvironmentVariables.TelegramWebhookUrl];
    var baseUrlFromWebhook = TryGetUriAuthority(webhookUrl ?? "");
    if (!string.IsNullOrWhiteSpace(baseUrlFromWebhook))
        return baseUrlFromWebhook;

    return TripRadarConstants.WebUi.DefaultAuthBaseUrl;
}
```

**Step 4: Delete the `Resolve()` method**

Delete the `internal static string Resolve(...)` method (lines 396-406) and `TryGetUriAuthority` stays.

**Step 5: Build to verify**

```bash
dotnet build src/Aspire/Aspire.csproj
```

Expected: Build succeeded. (AppHost.cs still compiles because we haven't removed its `Resolve()` call yet — that happens in Task 7.)

Note: The build will fail at this point because AppHost.cs line 11 calls `TripRadarExtensions.Resolve()` which we just deleted. To keep each task independently compilable, we need to handle this. **Temporarily replace AppHost.cs line 11:**

```csharp
// Old:
var useCloudflareTunnel = !string.IsNullOrWhiteSpace(TripRadarExtensions.Resolve(builder, TripRadarConstants.EnvironmentVariables.CloudflareTunnelToken));

// Temporary (until Task 7 rewrites AppHost):
var useCloudflareTunnel = !string.IsNullOrWhiteSpace(builder.Configuration[TripRadarConstants.EnvironmentVariables.CloudflareTunnelToken]);
```

Also update AppHost.cs line 38 which passes `TripRadarConstants.EnvironmentVariables.CloudflareTunnelToken` as second positional arg to `builder.AddParameter()`— this was using `Resolve` as default value factory. Replace:
```csharp
// Old (line 36-38):
var cloudflareTunnelToken = builder.AddParameter(
    "cloudflare-tunnel-token",
    () => TripRadarExtensions.Resolve(builder, TripRadarConstants.EnvironmentVariables.CloudflareTunnelToken),
    secret: true);

// Temporary:
var cloudflareTunnelToken = builder.AddParameter(
    "cloudflare-tunnel-token",
    () => builder.Configuration[TripRadarConstants.EnvironmentVariables.CloudflareTunnelToken] ?? "",
    secret: true);
```

**Step 6: Build to verify**

```bash
dotnet build src/Aspire/Aspire.csproj
```

Expected: Build succeeded.

**Step 7: Commit**

```bash
git add src/Aspire/Hosting/TripRadar/TripRadarExtensions.cs src/Aspire/AppHost.cs
git commit -m "refactor: replace Resolve() with native Configuration API in TripRadarExtensions"
```

---

### Task 4: Add `.WithReference(infrastructure)` on TripRadarResource

**Files:**
- Modify: `src/Aspire/Hosting/SharedInfrastructure/SharedInfrastructureExtensions.cs`
- Modify: `src/Aspire/Hosting/TripRadar/TripRadarExtensions.cs`
- Modify: `src/Aspire/Hosting/TripRadar/TripRadarResource.cs`
- Modify: `src/Aspire/AppHost.cs`

**Step 1: Add `.WithReference()` extension on `SharedInfrastructureExtensions.cs`**

Add a new extension block after the existing `extension(IDistributedApplicationBuilder builder)`:

```csharp
extension(TripRadarResource tripRadar)
{
    public TripRadarResource WithReference(SharedInfrastructureResource infrastructure)
    {
        tripRadar.Kafka = infrastructure.Kafka;
        return tripRadar;
    }
}
```

This requires adding a using at the top of the file:
```csharp
using Aspire.Hosting.TripRadar;
```

**Step 2: Make `TripRadarResource.Kafka` nullable**

In `src/Aspire/Hosting/TripRadar/TripRadarResource.cs`, change:
```csharp
// Old:
public IResourceBuilder<KafkaServerResource> Kafka { get; internal set; } = null!;

// New:
public IResourceBuilder<KafkaServerResource>? Kafka { get; internal set; }
```

**Step 3: Remove `kafka` parameter from `AddTripRadar()`**

In `src/Aspire/Hosting/TripRadar/TripRadarExtensions.cs`:

```csharp
// Old signature:
public TripRadarResource AddTripRadar(
    IResourceBuilder<KafkaServerResource> kafka,
    string name = TripRadarNames.Default,
    Action<TripRadarOptions>? configure = null)

// New signature:
public TripRadarResource AddTripRadar(
    Action<TripRadarOptions>? configure = null,
    string name = TripRadarNames.Default)
```

Also remove line 167: `tripRadarResource.Kafka = kafka;`

**Step 4: Add null guard in `ConfigureSharedServiceReferences`**

The Kafka reference is now optional until `.WithReference(infrastructure)` is called:

```csharp
IResourceBuilder<T> ConfigureSharedServiceReferences<T>(IResourceBuilder<T> serviceBuilder)
    where T : IResourceWithEnvironment
{
    serviceBuilder
        .WithReference(db)
        .WithReference(redis)
        .WithReference(flagd);

    if (tripRadarResource.Kafka is not null)
        serviceBuilder.WithReference(tripRadarResource.Kafka, connectionName: TripRadarConstants.ConnectionNames.TripRadarKafka);

    return serviceBuilder;
}
```

Also update the `WithReference(TripRadarResource)` extension (the generic one for silos):
```csharp
public IResourceBuilder<T> WithReference(TripRadarResource tripRadar)
{
    builder.WithReference(tripRadar.Api)
        .WithEnvironment(TripRadarConstants.ConfigurationKeys.TripRadarApiApiKey, tripRadar.ApiKey)
        .WithEnvironment(TripRadarConstants.ConfigurationKeys.TripRadarApiBearerToken, tripRadar.SiloGraphQlBearerToken);

    if (tripRadar.Kafka is not null)
        builder.WithReference(tripRadar.Kafka, connectionName: TripRadarConstants.ConnectionNames.TripRadarKafka);

    return builder;
}
```

And update `WaitFor(TripRadarResource)`:
```csharp
public IResourceBuilder<T> WaitFor(TripRadarResource tripRadar)
{
    builder.WaitFor(tripRadar.Api);
    if (tripRadar.Kafka is not null)
        builder.WaitFor(tripRadar.Kafka);
    return builder;
}
```

**Step 5: Update AppHost.cs call site**

```csharp
// Old:
var tripRadar = builder.AddTripRadar(infrastructure.Kafka,
        configure: opts => opts.MinimalInfrastructure().UseRealApis())
    .WithWebUI();

// New:
var tripRadar = builder.AddTripRadar(opts => opts.MinimalInfrastructure().UseRealApis())
    .WithReference(infrastructure)
    .WithWebUI();
```

Note: The `configure:` named argument is no longer needed since `configure` is now the first parameter.

**Step 6: Build to verify**

```bash
dotnet build src/Aspire/Aspire.csproj
```

Expected: Build succeeded.

**Step 7: Commit**

```bash
git add src/Aspire/Hosting/SharedInfrastructure/SharedInfrastructureExtensions.cs src/Aspire/Hosting/TripRadar/TripRadarExtensions.cs src/Aspire/Hosting/TripRadar/TripRadarResource.cs src/Aspire/AppHost.cs
git commit -m "refactor: add WithReference(infrastructure) pattern, remove Kafka param from AddTripRadar"
```

---

### Task 5: Create `Hosting/Assistant/` module

**Files:**
- Create: `src/Aspire/Hosting/Assistant/AssistantNames.cs`
- Create: `src/Aspire/Hosting/Assistant/AssistantConstants.cs`
- Create: `src/Aspire/Hosting/Assistant/AssistantResource.cs`
- Create: `src/Aspire/Hosting/Assistant/AssistantExtensions.cs`

**Step 1: Create `AssistantNames.cs`**

```csharp
namespace Aspire.Hosting.Assistant;

internal static class AssistantNames
{
    public const string Default = "assistant";
    public const string Host = "assistant-host";
    public const string MainMiniApp = "assistant-miniapp-main-ui";
    public const string TestMiniApp = "assistant-miniapp-test-ui";
    public const string CloudflareTunnel = "cloudflared-tunnel";
}
```

**Step 2: Create `AssistantConstants.cs`**

```csharp
namespace Aspire.Hosting.Assistant;

internal static class AssistantConstants
{
    internal static class EnvironmentVariables
    {
        public const string CloudflareTunnelToken = "CLOUDFLARE_TUNNEL_TOKEN";
        public const string MiniAppUpstream = "TELEGRAM_MINI_APP_UPSTREAM";
    }

    internal static class ParameterNames
    {
        public const string CloudflareTunnelToken = "cloudflare-tunnel-token";
    }

    internal static class Ports
    {
        public const int TestMiniApp = 5160;
    }
}
```

**Step 3: Create `AssistantResource.cs`**

```csharp
namespace Aspire.Hosting.Assistant;

public class AssistantResource(IDistributedApplicationBuilder builder, string name) : Resource(name)
{
    internal IDistributedApplicationBuilder Builder { get; } = builder;
    public IResourceBuilder<ProjectResource> Host { get; internal set; } = null!;
    public IResourceBuilder<ProjectResource> MainMiniApp { get; internal set; } = null!;
    public IResourceBuilder<ProjectResource> TestMiniApp { get; internal set; } = null!;
    public IResourceBuilder<ExecutableResource>? CloudflareTunnel { get; internal set; }
}
```

**Step 4: Create `AssistantExtensions.cs`**

```csharp
using Aspire.Hosting.AI;
using Aspire.Hosting.Telegram;

namespace Aspire.Hosting.Assistant;

internal static class AssistantExtensions
{
    extension(IDistributedApplicationBuilder builder)
    {
        public AssistantResource AddAssistant(string name = AssistantNames.Default)
        {
            var resource = new AssistantResource(builder, name);
            var assistantBuilder = builder.AddResource(resource)
                .WithInitialState(new()
                {
                    ResourceType = "Assistant",
                    State = KnownResourceStates.Running,
                    Properties = [new("Type", "AI Assistant")]
                });

            resource.MainMiniApp = builder
                .AddProject<Projects.Assistant_MiniApp_Api>(AssistantNames.MainMiniApp)
                .WithParentRelationship(assistantBuilder);

            resource.TestMiniApp = builder
                .AddProject<Projects.Assistant_TelegramMiniApp>(AssistantNames.TestMiniApp)
                .WithParentRelationship(assistantBuilder)
                .WithHttpEndpoint(port: AssistantConstants.Ports.TestMiniApp, name: "http");

            resource.Host = builder
                .AddProject<Projects.Assistant_Silo>(AssistantNames.Host)
                .WithParentRelationship(assistantBuilder)
                .WithEnvironment(AssistantConstants.EnvironmentVariables.MiniAppUpstream,
                    resource.MainMiniApp.GetEndpoint("http"))
                .WaitFor(resource.MainMiniApp);

            return resource;
        }
    }

    extension(AssistantResource assistant)
    {
        public AssistantResource WithReference(AIResource ai)
        {
            assistant.Host.WithReference(ai);
            return assistant;
        }

        public AssistantResource WithTelegramBot(Action<TelegramBotOptions>? configure = null)
        {
            assistant.Host.WithTelegramBot(assistant.Builder, configure);
            return assistant;
        }

        public AssistantResource WithCloudflareTunnel()
        {
            var token = assistant.Builder.Configuration[
                AssistantConstants.EnvironmentVariables.CloudflareTunnelToken];
            if (string.IsNullOrWhiteSpace(token)) return assistant;

            var tokenParam = assistant.Builder.AddParameter(
                AssistantConstants.ParameterNames.CloudflareTunnelToken,
                () => assistant.Builder.Configuration[
                    AssistantConstants.EnvironmentVariables.CloudflareTunnelToken] ?? "",
                secret: true);

            assistant.CloudflareTunnel = assistant.Builder
                .AddExecutable(
                    AssistantNames.CloudflareTunnel,
                    "pwsh",
                    assistant.Builder.AppHostDirectory,
                    [
                        "-NoProfile", "-Command",
                        $"cloudflared tunnel --no-autoupdate run --token $env:{AssistantConstants.EnvironmentVariables.CloudflareTunnelToken}"
                    ])
                .WithEnvironment(
                    AssistantConstants.EnvironmentVariables.CloudflareTunnelToken, tokenParam);

            assistant.Host = assistant.Host.WaitFor(assistant.CloudflareTunnel);
            return assistant;
        }
    }

    extension<T>(IResourceBuilder<T> builder) where T : IResourceWithWaitSupport
    {
        public IResourceBuilder<T> WaitFor(AssistantResource assistant) =>
            builder.WaitFor(assistant.Host);
    }
}
```

**Step 5: Build to verify**

```bash
dotnet build src/Aspire/Aspire.csproj
```

Expected: Build succeeded. (AppHost.cs still has old code that also compiles — both paths work until Task 7.)

**Step 6: Commit**

```bash
git add src/Aspire/Hosting/Assistant/
git commit -m "feat: add Assistant hosting module with resource, extensions, and Cloudflare tunnel"
```

---

### Task 6: Move `CloudflareTunnelToken` from TripRadarConstants to AssistantConstants

**Files:**
- Modify: `src/Aspire/Hosting/TripRadar/Constants/TripRadarConstants.Configuration.cs`

**Step 1: Delete the constant**

In `TripRadarConstants.Configuration.cs`, delete line 81:
```csharp
public const string CloudflareTunnelToken = "CLOUDFLARE_TUNNEL_TOKEN";
```

**Step 2: Verify no remaining references**

Search for `TripRadarConstants.EnvironmentVariables.CloudflareTunnelToken` — should only appear in AppHost.cs (temporary code from Task 3 that gets replaced in Task 7).

```bash
# Verify — only AppHost.cs should reference it, and that's the temporary code
grep -r "CloudflareTunnelToken" src/Aspire/ --include="*.cs" | grep -v "AssistantConstants"
```

**Step 3: Update temporary AppHost.cs references**

Replace the temporary references in AppHost.cs from Task 3:
```csharp
// Old (temporary from Task 3):
var useCloudflareTunnel = !string.IsNullOrWhiteSpace(builder.Configuration[TripRadarConstants.EnvironmentVariables.CloudflareTunnelToken]);

// New:
var useCloudflareTunnel = !string.IsNullOrWhiteSpace(builder.Configuration[Aspire.Hosting.Assistant.AssistantConstants.EnvironmentVariables.CloudflareTunnelToken]);
```

And the same for the `AddParameter` call. (This is temporary — Task 7 removes all of it.)

**Step 4: Build to verify**

```bash
dotnet build src/Aspire/Aspire.csproj
```

Expected: Build succeeded.

**Step 5: Commit**

```bash
git add src/Aspire/Hosting/TripRadar/Constants/TripRadarConstants.Configuration.cs src/Aspire/AppHost.cs
git commit -m "refactor: move CloudflareTunnelToken constant to AssistantConstants"
```

---

### Task 7: Rewrite `AppHost.cs`

**Files:**
- Modify: `src/Aspire/AppHost.cs`

**Step 1: Replace entire file contents**

```csharp
using Aspire.Hosting.AI;
using Aspire.Hosting.Assistant;
using Aspire.Hosting.SharedInfrastructure;
using Aspire.Hosting.TripRadar;
using Rosex.AI.AI.Models.Anthropic;
using Rosex.AI.AI.Models.OpenAI;
using Rosex.AI.State.Providers;

var builder = DistributedApplication.CreateBuilder(args);

var infrastructure = builder.AddInfrastructure();

var ai = builder.AddAI(ai => ai
    .UseDevelopmentClustering()
    .WithLLM<Claude45Haiku>()
    .WithLLM<Gpt4o>()
    .WithState<FileSystemStateProvider>(stateRootDirectory: "D:\\RoseX\\AI")
    .WithMcpServer());

var tripRadar = builder.AddTripRadar(opts => opts.MinimalInfrastructure().UseRealApis())
    .WithReference(infrastructure)
    .WithWebUI();

var assistant = builder.AddAssistant()
    .WithReference(ai)
    .WithTelegramBot(bot => bot.UseLocalVoice2Text())
    .WithCloudflareTunnel();

builder.AddProject<Projects.TripRadar_Silo>("tripradar-host")
    .WithReference(ai)
    .WithReference(tripRadar)
    .WaitFor(tripRadar)
    .WaitFor(assistant);

builder.Build().Run();
```

**Step 2: Verify no stale usings remain**

The old file had `using Aspire.Hosting.Telegram;` and `using Aspire.Hosting.TripRadar.Constants;` — both are no longer needed.

**Step 3: Build to verify**

```bash
dotnet build src/Aspire/Aspire.csproj
```

Expected: Build succeeded.

**Step 4: Commit**

```bash
git add src/Aspire/AppHost.cs
git commit -m "refactor: rewrite AppHost.cs to purely declarative 31-line resource wiring"
```

---

### Task 8: Build, Run, and Verify with Aspire MCP

**Step 1: Full build**

```bash
dotnet build src/Aspire/Aspire.csproj
```

Expected: Build succeeded, 0 warnings related to our changes.

**Step 2: Run Aspire**

```bash
dotnet run --project src/Aspire/Aspire.csproj
```

**Step 3: Verify resources via Aspire MCP**

Use `mcp__aspire__list_resources` to verify:
- `assistant` group resource exists with state "Running"
- `assistant-host`, `assistant-miniapp-main-ui`, `assistant-miniapp-test-ui` are nested under it
- `infra` group still has `infra-kafka`, `infra-otel`, `infra-jaeger`, `infra-grafana`, `infra-prometheus`
- `tripradar` group still has all its children (postgres, redis, api, jobs, etc.)
- `ai` group still has qdrant, ai-mcp, mcp-inspector
- Kafka is properly referenced by tripradar-api and tripradar-jobs
- If `CLOUDFLARE_TUNNEL_TOKEN` is not set, no `cloudflared-tunnel` resource appears

**Step 4: Check console logs for errors**

Use `mcp__aspire__list_console_logs` on key resources:
- `assistant-host` — should start without errors
- `tripradar-api` — should connect to Kafka
- `ai-mcp` — should connect to Orleans

**Step 5: Verify no regressions in structured logs**

Use `mcp__aspire__list_structured_logs` to check for error-level entries across all resources.

**Step 6: Final commit if any fixes were needed**

```bash
git add -A
git commit -m "fix: address issues found during Aspire verification"
```

---

## Task Dependency Order

```
Task 1 (otel move)          — independent
Task 2 (Telegram Resolve)   — independent
Task 3 (TripRadar Resolve)  — independent (temporarily patches AppHost)
Task 4 (WithReference)      — independent of 1-3
Task 5 (Assistant module)   — independent of 1-4
Task 6 (move constant)      — after Task 3 + Task 5
Task 7 (rewrite AppHost)    — after Task 4 + Task 5 + Task 6
Task 8 (verify)             — after Task 7
```

Tasks 1-5 can be parallelized. Tasks 6-8 must be sequential.
