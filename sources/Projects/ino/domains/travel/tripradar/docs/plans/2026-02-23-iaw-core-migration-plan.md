# IAW Core Migration & Travel Bot Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Replace Rosex.AI with IAW.Core, update all Aspire hosting to use IAW types, build travel-domain agents without "Agent" postfix, and implement Telegram Bot 9.4 topics-based wizard UX.

**Architecture:** Copy IAW.Core from `demo/src/IAW/IAW.Core/` into `src/AI/IAW.Core/`, delete Rosex.AI entirely, adapt the Aspire hosting layer to reference IAW types instead of Rosex types, build fresh travel-domain agents extending IAW's `Agent` base with Orleans event broadcasting for reactive behavior (TripApproved -> Weather monitoring, PriceTracker).

**Tech Stack:** .NET 11, Orleans 10.0.1 (Journaling, Streaming, DurableJobs), IAW.Core (Agent base, IAgent, AgentTest), Telegram.BotAPI 9.4.0, Microsoft.Extensions.AI 10.3.0, Aspire 13.1.1, xUnit v3

**Design doc:** `docs/plans/2026-02-23-iaw-core-migration-design.md`

---

## Phase 1: Core Framework Swap (IAW.Core replaces Rosex.AI)

### Task 1: Copy IAW.Core into the project

**Files:**
- Create: `src/AI/IAW.Core/` (entire directory tree copied from `E:\TripRadar\demo\src\IAW\IAW.Core\`)
- Modify: `src/AI/IAW.Core/IAW.Core.csproj` (adapt for local Directory.Packages.props)

**Step 1: Copy IAW.Core directory**

```bash
cp -r "E:/TripRadar/demo/src/IAW/IAW.Core" "E:/TripRadar/Propotypes/TelegramApp/src/AI/IAW.Core"
```

**Step 2: Adapt IAW.Core.csproj for the TelegramApp project**

The demo uses its own `Directory.Packages.props`. The TelegramApp also uses central package management, but some packages may be missing. Add any missing package versions to `Directory.Packages.props`:

Packages from IAW.Core that may need adding:
- `Microsoft.Orleans.DurableJobs` Version `10.0.1-alpha.1`
- `Octokit` Version `13.0.1`
- `xunit.v3.assert` (already present as `3.2.2`)

Also ensure IAW.Core.csproj doesn't have `ManagePackageVersionsCentrally=false` since the TelegramApp uses central versioning.

**Step 3: Verify IAW.Core.csproj has correct TargetFramework**

Both demo and TelegramApp target `net11.0`. Verify the csproj has `<TargetFramework>net11.0</TargetFramework>`. If not present (demo uses Directory.Build.props), add it.

**Step 4: Build IAW.Core in isolation**

```bash
dotnet build src/AI/IAW.Core/IAW.Core.csproj
```

Expected: Successful build with no errors.

**Step 5: Commit**

```bash
git add src/AI/IAW.Core/
git commit -m "feat: copy IAW.Core framework from demo project"
```

---

### Task 2: Update Aspire hosting to use IAW.Core types instead of Rosex.AI types

**Files:**
- Modify: `src/Aspire/Hosting/AI/AIExtensions.cs` — change `using Rosex.AI.*` to `using IAW.Core.*`
- Modify: `src/Aspire/Hosting/AI/AIResource.cs` — same namespace changes
- Modify: `src/Aspire/Hosting/AI/Configurations/LLMProviderHostConfig.cs` — same
- Modify: `src/Aspire/Hosting/AI/Configurations/StateProviderHostConfig.cs` — same
- Modify: `src/Aspire/AppHost.cs` — change `using Rosex.AI.*` to `using IAW.Core.*`
- Modify: `src/Aspire/Aspire.csproj` — change ProjectReference from Rosex.AI to IAW.Core

**Step 1: Identify all Rosex.AI type usages in Aspire hosting**

Key namespace mappings:
| Rosex.AI Namespace | IAW.Core Namespace |
|---|---|
| `Rosex.AI.AI.Models` | `IAW.Core.AI` |
| `Rosex.AI.AI.Models.Anthropic` | `IAW.Core.AI.Models` |
| `Rosex.AI.AI.Models.OpenAI` | `IAW.Core.AI.Models` |
| `Rosex.AI.Configuration` | `IAW.Core.AI` (for LlmConfig) |
| `Rosex.AI.State` | No direct equivalent — needs adaptation |
| `Rosex.AI.State.Providers` | No direct equivalent — needs adaptation |

**Important:** IAW.Core does NOT have `AIConfig`, `IStateProvider`, `StateProviderType`, `FileSystemStateProvider`, or `BlobStateProvider`. These are Rosex.AI-specific abstractions for the state provider system. We need to either:
- (a) Port `AIConfig.cs` and the state provider interfaces into IAW.Core, OR
- (b) Keep a minimal `Aspire.Hosting.AI.Config` namespace in the Aspire project itself

**Recommended: Option (b)** — Move `AIConfig.cs` and `IStateProvider`/`StateProviderType` into the Aspire hosting project since they're hosting-level concerns, not agent framework concerns. IAW.Core uses Orleans Journaling directly.

**Step 2: Move AIConfig and state provider types into Aspire hosting**

Create `src/Aspire/Hosting/AI/Config/AIConfig.cs` — copy from `src/AI/Rosex.AI/Configuration/AIConfig.cs` but in the `Aspire.Hosting.AI` namespace.

Create `src/Aspire/Hosting/AI/Config/StateProviderType.cs` — enum with FileSystem, Memory, AzureBlob values.

Create `src/Aspire/Hosting/AI/Config/IStateProvider.cs` — minimal interface with `StorageName` and `ProviderType` properties.

Create `src/Aspire/Hosting/AI/Config/FileSystemStateProvider.cs` and `VolatileStateProvider.cs` — minimal config records.

**Step 3: Update AIExtensions.cs**

Replace all `using Rosex.AI.*` with:
```csharp
using IAW.Core.AI;
using IAW.Core.AI.Models;
using Aspire.Hosting.AI.Config;
```

**Step 4: Update AppHost.cs**

```csharp
// Before:
using Rosex.AI.AI.Models.Anthropic;
using Rosex.AI.AI.Models.OpenAI;
using Rosex.AI.State.Providers;

// After:
using IAW.Core.AI.Models;
using Aspire.Hosting.AI.Config;
```

**Step 5: Update Aspire.csproj**

```xml
<!-- Before: -->
<ProjectReference Include="..\AI\Rosex.AI\Rosex.AI.csproj" IsAspireProjectResource="false" />
<ProjectReference Include="..\AI\Rosex.AI.MCP\Rosex.AI.Mcp.csproj" />

<!-- After: -->
<ProjectReference Include="..\AI\IAW.Core\IAW.Core.csproj" IsAspireProjectResource="false" />
```

Note: Rosex.AI.MCP reference removed for now — we'll update it in Task 4.

**Step 6: Build Aspire project**

```bash
dotnet build src/Aspire/Aspire.csproj
```

Expected: Build may fail due to downstream projects still referencing Rosex.AI. That's OK — we fix those next.

**Step 7: Commit**

```bash
git add src/Aspire/
git commit -m "feat: update Aspire hosting to use IAW.Core types"
```

---

### Task 3: Update Assistant project references from Rosex.AI to IAW.Core

**Files:**
- Modify: `src/Assistant/Assistant/Assistant.csproj` — change Rosex.AI reference to IAW.Core
- Modify: `src/Assistant/Assistant.Silo/Assistant.Silo.csproj` — same
- Modify: All `.cs` files in `src/Assistant/` that use `Rosex.AI` namespaces

**Step 1: Update Assistant.csproj**

```xml
<!-- Before: -->
<ProjectReference Include="..\..\AI\Rosex.AI\Rosex.AI.csproj" />

<!-- After: -->
<ProjectReference Include="..\..\AI\IAW.Core\IAW.Core.csproj" />
```

**Step 2: Update namespace imports in Assistant source files**

Key files to update:
- `src/Assistant/Assistant/IUserAgent.cs` — `IInteractiveGrain` → `IAgent` (from `IAW.Core`)
- `src/Assistant/Assistant/Telegram/ITelegram.cs` — same pattern
- `src/Assistant/Assistant/Telegram/ITelegramUser.cs` — same
- `src/Assistant/Assistant.Silo/Grains/UserAgent.cs` — `AIAgent` → `Agent`
- `src/Assistant/Assistant.Silo/Grains/Telegram/TelegramGrain.cs` — `InteractiveGrain` → update
- `src/Assistant/Assistant.Silo/Grains/Telegram/TelegramUserGrain.cs` — same
- All voice grains

Namespace mappings for imports:
| Rosex.AI | IAW.Core |
|---|---|
| `using Rosex.AI.Grains;` | `using IAW.Core;` |
| `using Rosex.AI.AI.Agents;` | `using IAW.Core;` |
| `using Rosex.AI.Abstractions.States;` | `using IAW.Core.Models;` |
| `using Rosex.AI.Attributes;` | `using IAW.Core.Attributes;` |
| `using Rosex.AI.AI.Attributes;` | `using IAW.Core.AI;` |

Base class mappings:
| Rosex.AI | IAW.Core |
|---|---|
| `: AIAgent` | `: Agent` |
| `: InteractiveGrain` | `: DurableGrain` (or `: Agent` if it needs IAgent) |
| `: IInteractiveGrain` | `: IAgent` |
| `: IChatAgent` | `: IAgent` |
| `[Tool("name")]` | `[Description("description")]` on method |

**Step 3: Handle constructor signature differences**

Rosex.AI `AIAgent` constructor:
```csharp
protected AIAgent(
    [Memory("grain-state")] IDurableDictionary<string, StateDescriptor> state,
    [Memory("pending")] IDurableQueue<ChatItem> pendingMessages,
    [Memory("conversation-history")] IDurableDictionary<ChatItem> conversationHistory,
    IChatClient chatClient,
    ILogger logger)
```

IAW.Core `Agent` constructor:
```csharp
protected Agent(
    [Memory("agent-state")] IDurableDictionary<string, StateDescriptor> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    [Memory("tracking-items")] IDurableDictionary<string, TrackingItem> trackingItems,
    IChatClient chatClient)
```

Key differences: different memory key names, no pending queue, no logger param (use `ILogger` from DI), events/tracking replace chat history.

**Step 4: Build to check compilation**

```bash
dotnet build src/Assistant/Assistant/Assistant.csproj
dotnet build src/Assistant/Assistant.Silo/Assistant.Silo.csproj
```

Fix any remaining compilation errors.

**Step 5: Commit**

```bash
git add src/Assistant/
git commit -m "feat: migrate Assistant projects from Rosex.AI to IAW.Core"
```

---

### Task 4: Update MCP server from Rosex.AI.MCP to reference IAW.Core

**Files:**
- Modify: `src/AI/Rosex.AI.MCP/Rosex.AI.Mcp.csproj` — update project references
- Modify: All `.cs` files in `src/AI/Rosex.AI.MCP/` — update namespaces
- Optionally rename directory: `src/AI/Rosex.AI.MCP/` → `src/AI/IAW.MCP/`

**Step 1: Update Rosex.AI.Mcp.csproj**

```xml
<!-- Before: -->
<ProjectReference Include="..\Rosex.AI\Rosex.AI.csproj" />

<!-- After: -->
<ProjectReference Include="..\IAW.Core\IAW.Core.csproj" />
```

**Step 2: Update source files to use IAW.Core namespaces**

The MCP server exposes agent tools. It references `IInteractiveGrain`, `IChatAgent`, `IAgentRegistry`, etc. Update all to IAW equivalents (`IAgent`, `IAgentRegistryGrain`).

**Step 3: Build**

```bash
dotnet build src/AI/Rosex.AI.MCP/Rosex.AI.Mcp.csproj
```

**Step 4: Commit**

```bash
git add src/AI/Rosex.AI.MCP/
git commit -m "feat: update MCP server to use IAW.Core"
```

---

### Task 5: Update TripRadar and Playground project references

**Files:**
- Modify: `src/TripRadar/TripRadar/TripRadar.csproj` — if it references Rosex.AI
- Modify: `src/TripRadar/TripRadar.Silo/TripRadar.Silo.csproj` — same
- Modify: All Playground projects under `src/AI/Playground/` — update references
- Modify: `src/AI/Rosex.AI.ConsoleClient/` — update reference

**Step 1: Update each csproj**

Search for all `.csproj` files referencing `Rosex.AI.csproj` and update to `IAW.Core.csproj`.

**Step 2: Update namespace imports in all affected source files**

Same namespace mappings as Task 3.

**Step 3: Build entire solution**

```bash
dotnet build TripRadar.slnx
```

Fix all remaining compilation errors.

**Step 4: Commit**

```bash
git add .
git commit -m "feat: migrate all projects from Rosex.AI to IAW.Core"
```

---

### Task 6: Delete Rosex.AI directory

**Files:**
- Delete: `src/AI/Rosex.AI/` (entire directory)

**Step 1: Verify no remaining references**

```bash
grep -r "Rosex.AI" --include="*.csproj" .
grep -r "Rosex.AI" --include="*.cs" src/
```

Expected: No references to Rosex.AI remain (except possibly in MCP project name which can be renamed later).

**Step 2: Delete Rosex.AI**

```bash
rm -rf src/AI/Rosex.AI/
```

**Step 3: Update TripRadar.slnx**

Remove the Rosex.AI project entry, add IAW.Core:

```xml
<!-- Remove: -->
<Project Path="src/AI/Rosex.AI/Rosex.AI.csproj" />

<!-- Add: -->
<Project Path="src/AI/IAW.Core/IAW.Core.csproj" />
```

**Step 4: Build entire solution**

```bash
dotnet build TripRadar.slnx
```

Expected: Clean build with no errors.

**Step 5: Run existing tests**

```bash
dotnet test
```

Expected: All existing tests pass (some may need fixes due to API differences).

**Step 6: Commit**

```bash
git add .
git commit -m "feat: remove Rosex.AI, complete migration to IAW.Core"
```

---

## Phase 2: Agent Interfaces & Event Types

### Task 7: Create event type records

**Files:**
- Create: `src/Assistant/Assistant/Events/TripApproved.cs`
- Create: `src/Assistant/Assistant/Events/WeatherAlert.cs`
- Create: `src/Assistant/Assistant/Events/PriceAlert.cs`
- Create: `src/Assistant/Assistant/Events/SearchCompleted.cs`

**Step 1: Create event records**

All events must have `[GenerateSerializer]` for Orleans:

```csharp
// src/Assistant/Assistant/Events/TripApproved.cs
using Orleans;

namespace Assistant.Events;

[GenerateSerializer, Immutable]
public sealed record TripApproved(
    [property: Id(0)] string UserId,
    [property: Id(1)] long ChatId,
    [property: Id(2)] string Destination,
    [property: Id(3)] DateOnly DepartureDate,
    [property: Id(4)] DateOnly ReturnDate);
```

```csharp
// src/Assistant/Assistant/Events/WeatherAlert.cs
using Orleans;

namespace Assistant.Events;

[GenerateSerializer, Immutable]
public sealed record WeatherAlert(
    [property: Id(0)] string UserId,
    [property: Id(1)] long ChatId,
    [property: Id(2)] string City,
    [property: Id(3)] double OldTemp,
    [property: Id(4)] double NewTemp,
    [property: Id(5)] string Summary);
```

```csharp
// src/Assistant/Assistant/Events/PriceAlert.cs
using Orleans;

namespace Assistant.Events;

[GenerateSerializer, Immutable]
public sealed record PriceAlert(
    [property: Id(0)] string UserId,
    [property: Id(1)] long ChatId,
    [property: Id(2)] string Route,
    [property: Id(3)] decimal OldPrice,
    [property: Id(4)] decimal NewPrice,
    [property: Id(5)] string Currency);
```

```csharp
// src/Assistant/Assistant/Events/SearchCompleted.cs
using Orleans;

namespace Assistant.Events;

[GenerateSerializer, Immutable]
public sealed record SearchCompleted(
    [property: Id(0)] string UserId,
    [property: Id(1)] string SearchType,
    [property: Id(2)] string QueryJson);
```

**Step 2: Build**

```bash
dotnet build src/Assistant/Assistant/Assistant.csproj
```

**Step 3: Commit**

```bash
git add src/Assistant/Assistant/Events/
git commit -m "feat: add Orleans event types for agent broadcasting"
```

---

### Task 8: Create Telegram agent interfaces (no "Agent" postfix)

**Files:**
- Modify: `src/Assistant/Assistant/Telegram/ITelegram.cs` — rewrite to extend `IAgent`, add topics support
- Modify: `src/Assistant/Assistant/Telegram/ITelegramUser.cs` — rewrite to extend `IAgent`
- Modify: `src/Assistant/Assistant/Telegram/Models/TelegramModels.cs` — add new models

**Step 1: Rewrite ITelegram interface**

The existing `ITelegram` extends `IInteractiveGrain`. Rewrite to extend `IAgent` with topics support (threadId parameter):

```csharp
using IAW.Core;

namespace Assistant.Telegram;

public interface ITelegram : IAgent
{
    Task<SendMessageResult> SendMessage(long chatId, string text, int? threadId = null, CancellationToken ct = default);
    Task<SendMessageResult> SendMarkdown(long chatId, string markdown, int? threadId = null, CancellationToken ct = default);
    Task SendTyping(long chatId, int? threadId = null, CancellationToken ct = default);
    Task<SendMessageResult> SendInlineKeyboard(long chatId, string text, InlineButton[][] buttons, int? threadId = null, CancellationToken ct = default);
    Task<SendMessageResult> SendMiniAppLaunch(long chatId, string text, string webAppUrl, CancellationToken ct = default);
    Task<SendMessageResult> UpdateMessage(long chatId, int messageId, string text, InlineButton[][]? buttons = null, CancellationToken ct = default);
    Task<int> CreateForumTopic(long chatId, string name, int? iconColor = null, CancellationToken ct = default);
    Task PinMessage(long chatId, int messageId, int? threadId = null, CancellationToken ct = default);
    Task SetWebhook(string url, CancellationToken ct = default);
    Task<DownloadFileResult> DownloadFile(string fileId, CancellationToken ct = default);
    Task AnswerCallbackQuery(string callbackQueryId, string? text = null, CancellationToken ct = default);
}
```

**Step 2: Rewrite ITelegramUser interface**

```csharp
using IAW.Core;

namespace Assistant.Telegram;

public interface ITelegramUser : IAgent
{
    [OneWay]
    Task ProcessUpdateOneWay(TelegramUpdate update);
    Task<UpdateProcessResult> ProcessUpdate(TelegramUpdate update, CancellationToken ct = default);
    Task<UserStats> GetStats(CancellationToken ct = default);
}
```

**Step 3: Add new Telegram models for topics and wizard**

Add to `TelegramModels.cs`:

```csharp
[GenerateSerializer, Immutable]
public sealed record TopicRegistry(
    [property: Id(0)] int? TravelAiThreadId,
    [property: Id(1)] int? GeneralAiThreadId);

[GenerateSerializer, Immutable]
public sealed record UserSettings(
    [property: Id(0)] bool AgentsEnabled,
    [property: Id(1)] string? Locale,
    [property: Id(2)] string? Timezone);

[GenerateSerializer]
public sealed record WizardState(
    [property: Id(0)] string SearchType,     // "flight", "hotel", "place"
    [property: Id(1)] string Step,           // "from", "to", "dates", "passengers", "confirm"
    [property: Id(2)] string? From,
    [property: Id(3)] string? To,
    [property: Id(4)] DateOnly? DepartureDate,
    [property: Id(5)] DateOnly? ReturnDate,
    [property: Id(6)] int? Passengers,
    [property: Id(7)] string? Category,
    [property: Id(8)] string? OptionsJson);
```

**Step 4: Build**

```bash
dotnet build src/Assistant/Assistant/Assistant.csproj
```

**Step 5: Commit**

```bash
git add src/Assistant/Assistant/Telegram/
git commit -m "feat: rewrite Telegram interfaces with IAgent base and topics support"
```

---

### Task 9: Create new agent interfaces (Travel, Weather, Search, Notification)

**Files:**
- Create: `src/Assistant/Assistant/Agents/ITravelAssistant.cs`
- Create: `src/Assistant/Assistant/Agents/IGeneralAssistant.cs`
- Create: `src/Assistant/Assistant/Agents/IWeather.cs`
- Create: `src/Assistant/Assistant/Agents/IFlightSearch.cs`
- Create: `src/Assistant/Assistant/Agents/IStaySearch.cs`
- Create: `src/Assistant/Assistant/Agents/IPlaceSearch.cs`
- Create: `src/Assistant/Assistant/Agents/IPriceTracker.cs`
- Create: `src/Assistant/Assistant/Agents/INotification.cs`

**Step 1: Create all interfaces**

All extend `IAgent` from IAW.Core. Minimal — IAW's `IAgent` already provides SendMessage, GetHistory, GetTools, HandleEvent, etc.

```csharp
// ITravelAssistant.cs — travel-domain LLM chat, publishes trip.approved
using IAW.Core;
namespace Assistant.Agents;

public interface ITravelAssistant : IAgent { }
```

```csharp
// IGeneralAssistant.cs — general LLM chat, no travel tools
using IAW.Core;
namespace Assistant.Agents;

public interface IGeneralAssistant : IAgent { }
```

```csharp
// IWeather.cs — subscribes to trip.approved, tracks weather every 4h
using IAW.Core;
namespace Assistant.Agents;

public interface IWeather : IAgent
{
    Task StartMonitoring(string city, string userId, long chatId, CancellationToken ct = default);
    Task StopMonitoring(string city, CancellationToken ct = default);
}
```

```csharp
// IFlightSearch.cs — searches TripRadar Server for flights
using IAW.Core;
namespace Assistant.Agents;

public interface IFlightSearch : IAgent
{
    Task<FlightSearchResult> SearchFlights(FlightSearchQuery query, CancellationToken ct = default);
}
```

```csharp
// IStaySearch.cs
using IAW.Core;
namespace Assistant.Agents;

public interface IStaySearch : IAgent
{
    Task<StaySearchResult> SearchStays(StaySearchQuery query, CancellationToken ct = default);
}
```

```csharp
// IPlaceSearch.cs
using IAW.Core;
namespace Assistant.Agents;

public interface IPlaceSearch : IAgent
{
    Task<PlaceSearchResult> SearchPlaces(PlaceSearchQuery query, CancellationToken ct = default);
}
```

```csharp
// IPriceTracker.cs — subscribes to tracking.requested, monitors prices hourly
using IAW.Core;
namespace Assistant.Agents;

public interface IPriceTracker : IAgent
{
    Task StartTracking(string queryHash, string queryJson, long chatId, CancellationToken ct = default);
    Task StopTracking(string queryHash, CancellationToken ct = default);
}
```

```csharp
// INotification.cs — subscribes to weather.alert, price.alert, routes to Telegram
using IAW.Core;
namespace Assistant.Agents;

public interface INotification : IAgent { }
```

**Step 2: Create search query/result models**

Create `src/Assistant/Assistant/Agents/Models/SearchModels.cs`:

```csharp
using Orleans;

namespace Assistant.Agents.Models;

[GenerateSerializer, Immutable]
public sealed record FlightSearchQuery(
    [property: Id(0)] string From,
    [property: Id(1)] string To,
    [property: Id(2)] DateOnly DepartureDate,
    [property: Id(3)] DateOnly? ReturnDate,
    [property: Id(4)] int Passengers);

[GenerateSerializer, Immutable]
public sealed record FlightSearchResult(
    [property: Id(0)] bool Success,
    [property: Id(1)] FlightOption[] Options,
    [property: Id(2)] string? Error);

[GenerateSerializer, Immutable]
public sealed record FlightOption(
    [property: Id(0)] string Airline,
    [property: Id(1)] decimal Price,
    [property: Id(2)] string Currency,
    [property: Id(3)] string DepartureTime,
    [property: Id(4)] string ArrivalTime,
    [property: Id(5)] int Stops);

// Similar records for StaySearchQuery/Result and PlaceSearchQuery/Result
```

**Step 3: Build**

```bash
dotnet build src/Assistant/Assistant/Assistant.csproj
```

**Step 4: Commit**

```bash
git add src/Assistant/Assistant/Agents/
git commit -m "feat: add travel-domain agent interfaces without Agent postfix"
```

---

## Phase 3: Agent Implementations

### Task 10: Implement Telegram agent (Bot API 9.4 with topics)

**Files:**
- Modify: `src/Assistant/Assistant.Silo/Grains/Telegram/TelegramGrain.cs` — rewrite as `Telegram` extending `Agent`

**Step 1: Rewrite TelegramGrain as Telegram (IAW Agent)**

The implementation wraps `Telegram.BotAPI` v9.4 TelegramBotClient. It extends `Agent` from IAW.Core and implements `ITelegram`. All send methods include optional `threadId` for topics support.

Key methods to implement:
- `SendMessage` — `bot.SendMessage(chatId, text, messageThreadId: threadId)`
- `SendInlineKeyboard` — builds `InlineKeyboardMarkup` from `InlineButton[][]`
- `CreateForumTopic` — `bot.CreateForumTopic(chatId, name, iconColor)`
- `SetWebhook` — `bot.SetWebhook(url, secretToken)`
- `AnswerCallbackQuery` — `bot.AnswerCallbackQuery(callbackQueryId, text)`

Constructor:
```csharp
public class Telegram(
    [Memory("agent-state")] IDurableDictionary<string, StateDescriptor> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    [Memory("tracking-items")] IDurableDictionary<string, TrackingItem> trackingItems,
    IChatClient chatClient,
    IOptions<TelegramHostOptions> telegramOptions)
    : Agent(state, eventLog, trackingItems, chatClient), ITelegram
```

**Step 2: Build**

```bash
dotnet build src/Assistant/Assistant.Silo/Assistant.Silo.csproj
```

**Step 3: Commit**

```bash
git add src/Assistant/Assistant.Silo/Grains/Telegram/
git commit -m "feat: implement Telegram agent with Bot API 9.4 topics support"
```

---

### Task 11: Implement TelegramUser agent (wizard state machine)

**Files:**
- Modify: `src/Assistant/Assistant.Silo/Grains/Telegram/TelegramUserGrain.cs` — rewrite as `TelegramUser`

**Step 1: Implement TelegramUser with wizard state machine**

This is the most complex agent. It handles:
- Routing updates by (chatId, threadId) pair
- Home thread (threadId=0): wizard buttons and callbacks
- Travel AI topic: forwards to web app launch
- General AI topic: forwards to GeneralAssistant agent
- Wizard state management in durable state

Key callback routing:
```csharp
private async Task HandleCallback(string callbackData, long chatId, string callbackQueryId)
{
    var parts = callbackData.Split('|');
    var domain = parts[0]; // "flight", "hotel", "place", "track", "settings"
    var action = parts[1]; // "step:from", "set_from:NYC", etc.

    switch (domain)
    {
        case "flight": await HandleFlightWizard(chatId, action); break;
        case "hotel": await HandleHotelWizard(chatId, action); break;
        case "place": await HandlePlaceWizard(chatId, action); break;
        case "track": await HandleTrackingAction(chatId, action); break;
        case "settings": await HandleSettings(chatId, action); break;
    }
}
```

**Step 2: Build and test**

```bash
dotnet build src/Assistant/Assistant.Silo/Assistant.Silo.csproj
```

**Step 3: Commit**

```bash
git add src/Assistant/Assistant.Silo/Grains/Telegram/
git commit -m "feat: implement TelegramUser agent with wizard state machine"
```

---

### Task 12: Implement TravelAssistant agent

**Files:**
- Create: `src/Assistant/Assistant.Silo/Grains/TravelAssistant.cs`

**Step 1: Implement TravelAssistant**

Extends `Agent`, uses `[Llm<Sonnet46>]`, has tools for flight/hotel/place search via grain calls. Publishes `trip.approved` on trip confirmation.

```csharp
[Publishes("trip.approved")]
[Publishes("trip.planned")]
[Capability("Travel")]
public class TravelAssistant(
    [Memory("agent-state")] IDurableDictionary<string, StateDescriptor> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    [Memory("tracking-items")] IDurableDictionary<string, TrackingItem> trackingItems,
    [Llm<Sonnet46>] IChatClient chatClient)
    : Agent(state, eventLog, trackingItems, chatClient), ITravelAssistant
```

Tools:
- `SearchFlights(from, to, date)` — calls `IFlightSearch` grain
- `SearchHotels(city, checkin, checkout)` — calls `IStaySearch` grain
- `SearchPlaces(city, category)` — calls `IPlaceSearch` grain
- `ApproveTripPlan(destination, dates)` — publishes `trip.approved` event

**Step 2: Build**

```bash
dotnet build src/Assistant/Assistant.Silo/Assistant.Silo.csproj
```

**Step 3: Commit**

```bash
git add src/Assistant/Assistant.Silo/Grains/TravelAssistant.cs
git commit -m "feat: implement TravelAssistant with search tools and trip approval"
```

---

### Task 13: Implement Weather agent with tracking

**Files:**
- Create: `src/Assistant/Assistant.Silo/Grains/Weather.cs`

**Step 1: Implement Weather agent**

Subscribes to `trip.approved` via `IStreamConsumer<AgentEvent>`. On activation, starts 4-hour tracking. Uses OpenWeatherMap API (already in `Directory.Packages.props` as `OpenWeatherMap.API 2.1.4-pre`).

```csharp
[Subscribes("trip.approved")]
[Publishes("weather.alert")]
[Capability("Monitoring")]
public class Weather(
    [Memory("agent-state")] IDurableDictionary<string, StateDescriptor> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    [Memory("tracking-items")] IDurableDictionary<string, TrackingItem> trackingItems,
    [Llm<Claude45Haiku>] IChatClient chatClient)
    : Agent(state, eventLog, trackingItems, chatClient), IWeather
```

Key behavior:
- `HandleEventAsync(AgentEvent)` — when `trip.approved`, extract destination, call `StartMonitoring`
- `StartMonitoring(city)` — `StartTracking(id, description, TimeSpan.FromHours(4))`
- `OnTrackingDueAsync(TrackingItem)` — fetch weather, compare with previous, publish alert if 6+°C diff

**Step 2: Build**

```bash
dotnet build src/Assistant/Assistant.Silo/Assistant.Silo.csproj
```

**Step 3: Commit**

```bash
git add src/Assistant/Assistant.Silo/Grains/Weather.cs
git commit -m "feat: implement Weather agent with 4h tracking and alert broadcasting"
```

---

### Task 14: Implement remaining agents (GeneralAssistant, FlightSearch, StaySearch, PlaceSearch, PriceTracker, Notification)

**Files:**
- Create: `src/Assistant/Assistant.Silo/Grains/GeneralAssistant.cs`
- Create: `src/Assistant/Assistant.Silo/Grains/FlightSearch.cs`
- Create: `src/Assistant/Assistant.Silo/Grains/StaySearch.cs`
- Create: `src/Assistant/Assistant.Silo/Grains/PlaceSearch.cs`
- Create: `src/Assistant/Assistant.Silo/Grains/PriceTracker.cs`
- Create: `src/Assistant/Assistant.Silo/Grains/Notification.cs`

**Step 1: Implement each agent**

- `GeneralAssistant` — Simple LLM agent with `[Llm<Claude45Haiku>]`, no tools, general-purpose system prompt
- `FlightSearch` — No LLM, calls TripRadar Server API for flight data
- `StaySearch` — No LLM, calls TripRadar Server API for hotel data
- `PlaceSearch` — No LLM, calls TripRadar Server API for place data
- `PriceTracker` — Subscribes to tracking events, uses IAW tracking system (1h intervals), publishes `price.alert`
- `Notification` — Subscribes to `weather.alert` and `price.alert`, routes messages to `ITelegram` agent

**Step 2: Build**

```bash
dotnet build src/Assistant/Assistant.Silo/Assistant.Silo.csproj
```

**Step 3: Commit**

```bash
git add src/Assistant/Assistant.Silo/Grains/
git commit -m "feat: implement remaining travel agents (search, tracking, notifications)"
```

---

## Phase 4: Webhook & Aspire Integration

### Task 15: Update Telegram webhook endpoints for topics routing

**Files:**
- Modify: `src/Assistant/Assistant.Silo/Telegram/TelegramEndpoints.cs` — add threadId routing
- Modify: `src/Assistant/Assistant.Silo/Program.cs` — update DI registration

**Step 1: Update TelegramEndpoints**

Add `message_thread_id` extraction and routing:

```csharp
var threadId = update.Message?.MessageThreadId ?? 0;
var chatId = update.Message?.Chat?.Id ?? update.CallbackQuery?.Message?.Chat?.Id;

// Route by threadId:
// 0 = Home (wizard) → TelegramUser handles
// travelAiThreadId = opens web app → no Telegram handling
// generalAiThreadId = GeneralAssistant → forward to agent
```

**Step 2: Update Program.cs DI**

Register new agents and services.

**Step 3: Build and run**

```bash
dotnet build src/Aspire/Aspire.csproj
dotnet run --project src/Aspire/Aspire.csproj
```

**Step 4: Commit**

```bash
git add src/Assistant/Assistant.Silo/
git commit -m "feat: update webhook routing with topics support"
```

---

### Task 16: Update Aspire AppHost for new agent structure

**Files:**
- Modify: `src/Aspire/AppHost.cs` — update AI resource references

**Step 1: Update AppHost.cs**

```csharp
using IAW.Core.AI.Models;
using Aspire.Hosting.AI.Config;

var builder = DistributedApplication.CreateBuilder(args);

var infrastructure = builder.AddInfrastructure();
var aiStateRootDirectory = builder.Configuration["AI__State__FileSystem__RootDirectory"]
    ?? Path.Combine(builder.AppHostDirectory, ".data", "ai-state");

var ai = builder.AddAI(ai => ai
    .UseDevelopmentClustering()
    .WithLLM<Claude45Haiku>()
    .WithLLM<Sonnet46>()
    .WithState<FileSystemStateProvider>(stateRootDirectory: aiStateRootDirectory)
    .WithMcpServer());

// ... rest of AppHost
```

**Step 2: Build and verify Aspire starts**

```bash
dotnet build src/Aspire/Aspire.csproj
dotnet run --project src/Aspire/Aspire.csproj
```

Verify all resources start correctly using Aspire MCP tools.

**Step 3: Commit**

```bash
git add src/Aspire/
git commit -m "feat: update Aspire AppHost for IAW.Core integration"
```

---

## Phase 5: Testing

### Task 17: Create AgentTest<T> contract tests for all agents

**Files:**
- Create: `tests/Assistant.Tests/` project (or update existing test project)
- Create: Test files for each agent

**Step 1: Create test project if needed**

```bash
dotnet new xunit -n Assistant.Tests -o tests/Assistant.Tests --framework net11.0
```

Add references:
```xml
<ProjectReference Include="..\..\src\AI\IAW.Core\IAW.Core.csproj" />
<ProjectReference Include="..\..\src\Assistant\Assistant\Assistant.csproj" />
<ProjectReference Include="..\..\src\Assistant\Assistant.Silo\Assistant.Silo.csproj" />
```

**Step 2: Write one-liner contract tests**

```csharp
using IAW.Core.Testing;
using Assistant.Telegram;
using Assistant.Agents;

namespace Assistant.Tests;

public class TelegramTest : AgentTest<ITelegram> { }
public class TelegramUserTest : AgentTest<ITelegramUser> { }
public class TravelAssistantTest : AgentTest<ITravelAssistant> { }
public class GeneralAssistantTest : AgentTest<IGeneralAssistant> { }
public class WeatherTest : AgentTest<IWeather> { }
public class FlightSearchTest : AgentTest<IFlightSearch> { }
public class StaySearchTest : AgentTest<IStaySearch> { }
public class PlaceSearchTest : AgentTest<IPlaceSearch> { }
public class PriceTrackerTest : AgentTest<IPriceTracker> { }
public class NotificationTest : AgentTest<INotification> { }
```

**Step 3: Run tests**

```bash
dotnet test tests/Assistant.Tests/
```

Expected: 12 contract tests x 10 agents = 120 tests, all passing.

**Step 4: Commit**

```bash
git add tests/
git commit -m "feat: add IAW contract tests for all travel agents (120 tests)"
```

---

### Task 18: Add custom business logic tests

**Files:**
- Create: `tests/Assistant.Tests/WizardStateTests.cs`
- Create: `tests/Assistant.Tests/EventBroadcastTests.cs`
- Create: `tests/Assistant.Tests/CallbackRoutingTests.cs`

**Step 1: Write wizard state transition tests**

Test the state machine logic: from → to → dates → passengers → confirm flow.

**Step 2: Write event broadcast tests**

Test that TripApproved event triggers Weather.StartMonitoring.

**Step 3: Write callback routing tests**

Test callback data parsing: `"flight|set_from:NYC"` → correct wizard action.

**Step 4: Run all tests**

```bash
dotnet test tests/Assistant.Tests/
```

**Step 5: Commit**

```bash
git add tests/
git commit -m "feat: add custom business logic tests for wizard and events"
```

---

## Phase 6: Final Verification

### Task 19: Full build, run, and smoke test

**Step 1: Clean build entire solution**

```bash
dotnet build TripRadar.slnx
```

Expected: 0 errors.

**Step 2: Run all tests**

```bash
dotnet test
```

Expected: All tests pass.

**Step 3: Start Aspire**

```bash
dotnet run --project src/Aspire/Aspire.csproj
```

**Step 4: Verify via Aspire MCP tools**

Use `mcp__aspire__list_resources` to confirm all resources are running.
Use `mcp__aspire__list_structured_logs` to check for errors.

**Step 5: Commit final state**

```bash
git add .
git commit -m "feat: complete IAW.Core migration with travel bot agents"
```
