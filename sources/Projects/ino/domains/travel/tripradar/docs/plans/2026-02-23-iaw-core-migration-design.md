# IAW Core Migration & Travel Bot Architecture Design

**Date:** 2026-02-23
**Status:** Approved

## Summary

Replace Rosex.AI framework with IAW.Core (copied from `demo/src/IAW/IAW.Core`). Build new travel-domain agents using IAW patterns. Implement Telegram Bot 9.4 topics-based UX with three conversation threads: deterministic Home wizard, Travel AI (web app), and General AI (pure Telegram).

## Approach

**Incremental Replace** — Copy IAW.Core source into the project, delete Rosex.AI entirely, build fresh agents from scratch using IAW's `Agent` base class, event broadcasting, and tracking system.

## Key Decisions

- **IAW namespace** kept for core library; **TripRadar namespace** for silo/agents
- **No "Agent" postfix** on agent names — everything is an agent by definition
- **No SQLite** — Orleans durable state (journaling) is the persistence layer
- **IAW.MCP** copied from demo, replacing Rosex.AI.MCP
- **`.mcp.json`** copied from demo for proper IAW MCP setup

---

## Project Structure

### Before (Current)

```
src/AI/
├── Rosex.AI/              # Core framework (135 files)
├── Rosex.AI.MCP/          # MCP server
└── Rosex.AI.ConsoleClient/ # Console client
```

### After (Target)

```
src/AI/
├── IAW.Core/              # Copied from demo/src/IAW/IAW.Core (IAW namespace)
├── IAW.MCP/               # Copied from demo/src/IAW/IAW.MCP (replaces Rosex.AI.MCP)
└── Rosex.AI.ConsoleClient/ # Updated references to IAW.Core
```

### Deleted

- `src/AI/Rosex.AI/` — entire directory (replaced by IAW.Core)
- `src/AI/Rosex.AI.MCP/` — replaced by IAW.MCP

---

## Agent Inventory

All agents extend IAW's `Agent` base class. No "Agent" postfix.

### Travel Domain Agents (TripRadar namespace)

| Interface | Implementation | Purpose | LLM | Key Events |
|-----------|---------------|---------|-----|------------|
| `ITelegram` | `Telegram` | Telegram Bot 9.4 API, topics, webhooks, message sending | None | Subscribes: `weather.alert`, `price.alert` |
| `ITelegramUser` | `TelegramUser` | Per-user processing, wizard state machine, topic routing | None | Publishes: `search.completed` |
| `ITravelAssistant` | `TravelAssistant` | Travel AI chat (Topic 1), TripRadar data, travel-only | Sonnet46 | Publishes: `trip.approved`, `trip.planned` |
| `IGeneralAssistant` | `GeneralAssistant` | General AI chat (Topic 2), pure Telegram, no travel tools | Claude45Haiku | — |
| `IWeather` | `Weather` | Weather monitoring at destination, 6+°C change alerts | Claude45Haiku | Subscribes: `trip.approved`. Tracking: 4h |
| `IFlightSearch` | `FlightSearch` | Flight search via TripRadar Server API | None | — |
| `IStaySearch` | `StaySearch` | Hotel/stay search via TripRadar Server API | None | — |
| `IPlaceSearch` | `PlaceSearch` | Places/attractions search via TripRadar Server API | None | — |
| `IPriceTracker` | `PriceTracker` | Hourly price monitoring, diff alerts, chart generation | None | Subscribes: `tracking.requested`. Tracking: 1h |
| `INotification` | `Notification` | Routes alerts to user via Telegram | None | Subscribes: `weather.alert`, `price.alert` |

---

## Event Flow (IAW Broadcasting)

IAW's `PublishAsync()` + `IStreamConsumer<T>` enables broadcast: multiple agents subscribe to the same event stream.

```
User approves trip in TravelAssistant
  → TravelAssistant.PublishAsync("trip.approved", {destination, dates, userId, chatId})
    → Weather (IStreamConsumer): StartTracking(4h) → checks weather API
    → PriceTracker (IStreamConsumer): StartTracking(1h) → monitors prices
    → Notification: logs trip approval

Weather detects 6°C+ change:
  → Weather.PublishAsync("weather.alert", {city, oldTemp, newTemp, userId, chatId})
    → Notification → ITelegram.SendMessage(chatId, "Weather alert: ...")

PriceTracker detects significant price drop:
  → PriceTracker.PublishAsync("price.alert", {route, oldPrice, newPrice, userId, chatId})
    → Notification → ITelegram.SendMessage(chatId, "Price alert: ...")
```

### Event Types

```csharp
record TripApproved(string UserId, long ChatId, string Destination,
    DateOnly DepartureDate, DateOnly ReturnDate);

record WeatherAlert(string UserId, long ChatId, string City,
    double OldTemp, double NewTemp, string Summary);

record PriceAlert(string UserId, long ChatId, string Route,
    decimal OldPrice, decimal NewPrice, string Currency);

record SearchCompleted(string UserId, string SearchType, string QueryJson);
```

---

## Telegram Bot 9.4 Topics Architecture

### Topic Structure

| Topic | threadId | Handler | Behavior |
|-------|----------|---------|----------|
| Home | 0 (general) | `TelegramUser` wizard | Pure buttons: Search Flights / Hotels / Places. No AI. |
| Travel AI | created dynamically | Opens Web App (AG-UI) → `TravelAssistant` | LLM + TripRadar tools, travel-only |
| General AI | created dynamically (gated by settings) | `GeneralAssistant` via Telegram API | LLM, no tools, general chat |

Chat must be a **supergroup with topics enabled** (Telegram requirement).

### ITelegram Agent API

```csharp
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

---

## Home Thread Wizard (threadId=0)

Pure state machine driven by inline keyboard buttons. No AI. Uses TripRadar data types.

### Entry Point

Three persistent buttons:
```
🔍 Search Flights  |  🏨 Search Hotels  |  📍 Search Places
```

### Flight Wizard Steps

1. **From city** → inline keyboard with popular cities + "Type manually"
2. **To city** → same pattern
3. **Dates** → departure date picker (month → day buttons)
4. **Passengers** → 1-4 quick select
5. **Confirm & Search** → calls `IFlightSearch` → results with "Track Price" buttons

### Hotel Wizard Steps

1. **City** → destination
2. **Check-in / Check-out dates**
3. **Guests / Rooms**
4. **Confirm & Search** → calls `IStaySearch`

### Places Wizard Steps

1. **City** → destination
2. **Category** → Restaurants / Attractions / Shopping / Nightlife
3. **Search** → calls `IPlaceSearch`

### Callback Data Format

```
flight|step:from           → Show "from" city selection
flight|set_from:NYC        → Set from=NYC, advance
hotel|step:city            → Show hotel city selection
place|set_cat:restaurants  → Set category
track|create:{queryHash}   → Start price tracking
settings|toggle:agents     → Toggle EnableAllAgents
```

### Wizard State

Stored in `TelegramUser` durable state:

```
Key: wizard:{chatId}
Value: { step, searchType, from, to, dates, passengers, category, optionsJson }
```

---

## Data Model (Orleans Durable State)

| Agent | State Keys | Description |
|-------|-----------|-------------|
| `TelegramUser` | `wizard:{chatId}` | Current wizard step and selections |
| `TelegramUser` | `topics:{chatId}` | Topic registry (travelAiThreadId, generalAiThreadId) |
| `TelegramUser` | `settings:{chatId}` | User settings (agentsEnabled, locale, tz) |
| `TravelAssistant` | `msg-*` | Conversation history (IAW built-in) |
| `Weather` | tracking items (IAW built-in) | 4h weather monitoring per destination |
| `PriceTracker` | tracking items + `prices:{queryHash}` | Price history for charts |

---

## Testing Strategy

IAW's `AgentTest<T>` — each agent gets 12 universal contract tests:

```csharp
public class TelegramTest : AgentTest<ITelegram> { }
public class WeatherTest : AgentTest<IWeather> { }
public class TravelAssistantTest : AgentTest<ITravelAssistant> { }
public class FlightSearchTest : AgentTest<IFlightSearch> { }
public class StaySearchTest : AgentTest<IStaySearch> { }
public class PlaceSearchTest : AgentTest<IPlaceSearch> { }
public class PriceTrackerTest : AgentTest<IPriceTracker> { }
public class NotificationTest : AgentTest<INotification> { }
public class GeneralAssistantTest : AgentTest<IGeneralAssistant> { }
public class TelegramUserTest : AgentTest<ITelegramUser> { }
```

Plus custom tests for:
- Wizard state transitions
- Event handling (TripApproved → Weather tracking starts)
- Price diff calculations and alert thresholds
- Callback routing logic
- Topic creation and registry

---

## Aspire Integration

```csharp
var assistant = builder.AddAssistant()
    .WithReference(iawCore)
    .WithTelegramBot(bot => bot
        .UseTopics()
        .UseLocalVoice2Text())
    .WithCloudflareTunnel();
```

IAW.MCP server exposes all agents as tools via `.mcp.json`.

---

## Migration Checklist

1. Copy `demo/src/IAW/IAW.Core/` → `src/AI/IAW.Core/`
2. Copy `demo/src/IAW/IAW.MCP/` → `src/AI/IAW.MCP/`
3. Copy `.mcp.json` from demo
4. Delete `src/AI/Rosex.AI/` and `src/AI/Rosex.AI.MCP/`
5. Update all project references from Rosex.AI → IAW.Core
6. Build agent interfaces (ITelegram, IWeather, etc.) — no "Agent" postfix
7. Build agent implementations using IAW's Agent base
8. Port Telegram webhook/topics logic to new Telegram agent
9. Implement Home wizard state machine in TelegramUser
10. Wire event broadcasting (TripApproved → Weather, PriceTracker)
11. Write AgentTest<T> for each agent
12. Update Aspire AppHost
13. Build, run, verify via Aspire MCP tools
