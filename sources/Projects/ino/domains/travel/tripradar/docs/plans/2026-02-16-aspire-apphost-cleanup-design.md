# Aspire AppHost Cleanup Design

**Date:** 2026-02-16
**Goal:** Refactor AppHost.cs from 71 lines of mixed imperative/declarative code to ~31 lines of purely declarative resource wiring. Eliminate leaky abstractions, extract the Assistant domain into its own hosting module, and clean up file organization.

## Target AppHost.cs

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

## Module Dependency Graph

```
AppHost.cs
  ├── SharedInfrastructure  (no deps on other modules)
  ├── AI                    (no deps on other modules)
  ├── Assistant             (depends on AI for .WithReference(ai), Telegram for .WithTelegramBot())
  ├── TripRadar             (depends on SharedInfrastructure for .WithReference(infrastructure))
  └── Telegram              (no deps on other modules — generic extensions)
```

No module depends on TripRadar constants except TripRadar itself. Assistant owns its own constants.

## Change 1: SharedInfrastructure `.WithReference()` Pattern

**Problem:** `AddInfrastructure()` returns raw `SharedInfrastructureResource`, forcing AppHost to reach into `infrastructure.Kafka` and pass it to `AddTripRadar()`. Every other domain uses `.WithReference()` for cross-domain wiring — infrastructure is the odd one out.

**Solution:** Add a `.WithReference(SharedInfrastructureResource)` extension on `TripRadarResource`.

### SharedInfrastructureExtensions.cs — add extension:

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

### TripRadarExtensions.cs — changes:

- Remove `IResourceBuilder<KafkaServerResource> kafka` parameter from `AddTripRadar()`
- Remove `tripRadarResource.Kafka = kafka;` assignment
- Kafka is now set lazily when `.WithReference(infrastructure)` is called

### TripRadarResource.cs — change:

- `Kafka` property: change `= null!` to `= null` (nullable, set via `.WithReference()`)

## Change 2: New `Hosting/Assistant/` Module

**Problem:** AppHost.cs has ~40 lines of raw assistant logic — two mini-app projects, Cloudflare tunnel conditional, assistant-host wiring. This is the only domain without its own `Hosting/{Name}/` folder.

### New files:

**`Hosting/Assistant/AssistantNames.cs`**

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

**`Hosting/Assistant/AssistantConstants.cs`**

```csharp
namespace Aspire.Hosting.Assistant;

internal static class AssistantConstants
{
    internal static class EnvironmentVariables
    {
        public const string CloudflareTunnelToken = "CLOUDFLARE_TUNNEL_TOKEN";
    }
}
```

**`Hosting/Assistant/AssistantResource.cs`**

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

**`Hosting/Assistant/AssistantExtensions.cs`**

Core shape:

```csharp
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
                .WithHttpEndpoint(port: 5160, name: "http");

            resource.Host = builder
                .AddProject<Projects.Assistant_Silo>(AssistantNames.Host)
                .WithParentRelationship(assistantBuilder)
                .WithEnvironment("TELEGRAM_MINI_APP_UPSTREAM",
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
                "cloudflare-tunnel-token",
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

Key decisions:
- `.WithReference(ai)` on AssistantResource delegates to `Host.WithReference(ai)`. AI wiring stays on the silo, not the mini-apps.
- `.WithCloudflareTunnel()` is self-contained — checks config, creates executable only if token exists. No conditional logic in AppHost.
- `WaitFor(AssistantResource)` — other silos wait for the Host project.
- Mini-apps get `.WithParentRelationship()` — nested under Assistant group in the Aspire dashboard.

## Change 3: File Organization

### 3A: Move `otel-config.yaml` into its own folder

```
# Before:
src/Aspire/otel-config.yaml

# After:
src/Aspire/OpenTelemetry/otel-config.yaml
```

Update path in `SharedInfrastructureExtensions.cs`:
```csharp
// Before:
.WithConfig("./otel-config.yaml")

// After:
.WithConfig("./OpenTelemetry/otel-config.yaml")
```

Final layout:
```
src/Aspire/
├── Grafana/
│   ├── config/
│   └── dashboards/
├── OpenTelemetry/
│   └── otel-config.yaml
├── Prometheus/
│   └── prometheus.yml
├── flags/
├── Hosting/
│   ├── AI/
│   ├── Assistant/          ← NEW
│   ├── SharedInfrastructure/
│   ├── Telegram/
│   └── TripRadar/
└── AppHost.cs
```

### 3B: Move `CloudflareTunnelToken` constant

- Delete from `TripRadarConstants.EnvironmentVariables` (line 81 of `TripRadarConstants.Configuration.cs`)
- Lives in `AssistantConstants.EnvironmentVariables` (new file)

## Change 4: Delete `Resolve()`, Use Aspire-Native APIs

**Problem:** Both `TripRadarExtensions` and `TelegramExtensions` have identical custom `Resolve()` methods that duplicate what `builder.Configuration[key]` already provides.

**Solution:** Delete `Resolve()` from both files. Replace all usages with `builder.Configuration[key]`.

### TripRadarExtensions.cs:

- Delete `internal static string Resolve(...)` method
- Delete `AddResolvedDefaultParameter` / `AddResolvedSecretParameter` local functions
- Replace with cleaner local helpers:

```csharp
IResourceBuilder<ParameterResource> AddDefaultParam(string name, string defaultValue) =>
    builder.AddParameter(name, defaultValue, publishValueAsDefault: true);

IResourceBuilder<ParameterResource> AddSecretParam(string name) =>
    builder.AddParameter(name, secret: true);

IResourceBuilder<ParameterResource> AddConfigBackedParam(string name, string configKey, string fallback = "") =>
    builder.AddParameter(name, () => builder.Configuration[configKey] ?? fallback, publishValueAsDefault: true);

IResourceBuilder<ParameterResource> AddConfigBackedSecret(string name, string configKey, string fallback = "") =>
    builder.AddParameter(name, () => builder.Configuration[configKey] ?? fallback, secret: true);
```

- `ResolveTelegramAuthBaseUrl()` stays as a private method but uses `builder.Configuration[key]` instead of `Resolve()`

### TelegramExtensions.cs:

- Delete `private static string Resolve(...)` method
- Replace usages with `appBuilder.Configuration[key] ?? fallback`

## Change 5: Rosex.AI Extensions — No Changes Needed

All environment variable contracts between Aspire hosting and Rosex.AI runtime remain unchanged:

| Env Var | Set by (Aspire) | Read by (Rosex.AI) | Changes? |
|---------|-----------------|---------------------|----------|
| `AIConfig.Clustering.Mode` | `AIExtensions.WithReference(ai)` | `HostBuilderExtensions.AddAI()` | No |
| `AIConfig.Clustering.GatewayEndpointEnvVar` | `AIExtensions.WithReference(AIResourceClient)` | `ResolveGatewayEndpoint()` | No |
| `AIConfig.State.FileSystem.RootDirectoryEnvVar` | `StateProviderHostConfig.ApplyTo()` | `AddGrainInfrastructure()` | No |
| All LLM/State env vars | Config classes `ApplyTo()` | LLM/State registration | No |

The refactoring changes how resources are wired in the AppHost. It does not change what environment variables reach running processes.

## Full File Impact

| # | Change | Files |
|---|--------|-------|
| 1 | SharedInfrastructure `.WithReference()` | `SharedInfrastructureExtensions.cs`, `TripRadarExtensions.cs`, `TripRadarResource.cs` |
| 2 | New Assistant module | NEW: `AssistantNames.cs`, `AssistantConstants.cs`, `AssistantResource.cs`, `AssistantExtensions.cs` |
| 3 | Move otel-config.yaml | Move file, update `SharedInfrastructureExtensions.cs` |
| 4 | Delete `Resolve()` | `TripRadarExtensions.cs`, `TelegramExtensions.cs` |
| 5 | Move CloudflareTunnelToken | `TripRadarConstants.Configuration.cs` (delete), `AssistantConstants.cs` (add) |
| 6 | Rewrite AppHost.cs | `AppHost.cs` |
| 7 | No Rosex.AI changes | Verified — env var contracts unchanged |
