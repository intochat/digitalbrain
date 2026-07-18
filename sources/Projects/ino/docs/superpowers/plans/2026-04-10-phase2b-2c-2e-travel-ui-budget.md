# Phase 2b+2c+2e: Travel Neurons + UI Templates + Token Budget

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Travel neurons handle flight/hotel/place queries and return structured results. Flutter renders results as rich cards via rfw templates. Token budget tracks per-user LLM usage.

**Architecture:** Three `ISynapseHandler` implementations (FlightSearch, HotelSearch, PlaceDiscovery) registered as keyed DI services. For V1 they return mock structured data (real SerpApi integration in a future pass). The gRPC Chat response is enhanced to carry structured result data. Flutter rfw components render flight/hotel/place cards. ITokenBudget grain tracks token usage per user with tier-based limits.

**Tech Stack:** Orleans 10 (keyed DI, IPersistentState), .NET 11, gRPC, Flutter (rfw, flutter_bloc)

---

## Task 1: Travel synapse handlers (C#)

**Files:**
- Create: `features/ino-new/InoNew.Core/Specialists/FlightSearchHandler.cs`
- Create: `features/ino-new/InoNew.Core/Specialists/HotelSearchHandler.cs`
- Create: `features/ino-new/InoNew.Core/Specialists/PlaceDiscoveryHandler.cs`
- Modify: `features/ino-new/InoNew.Core/InoNewSiloExtensions.cs` (register keyed handlers)

Each handler implements `ISynapseHandler`. For V1, return mock structured JSON results.

### FlightSearchHandler

```csharp
using System.Text.Json;

namespace InoNew.Core.Specialists;

public sealed class FlightSearchHandler : ISynapseHandler
{
    public Task<SynapseResult> HandleAsync(Synapse synapse, IGrainFactory grains, CancellationToken ct)
    {
        var query = synapse.Payload.Trim();

        var flights = new[]
        {
            new { airline = "Delta", from = "JFK", to = "BCN", price = 380, date = "2026-06-15", duration = "8h 20m" },
            new { airline = "United", from = "JFK", to = "BCN", price = 395, date = "2026-06-16", duration = "8h 45m" },
            new { airline = "Iberia", from = "JFK", to = "BCN", price = 412, date = "2026-06-17", duration = "7h 55m" },
        };

        var result = JsonSerializer.Serialize(new { type = "flight_results", query, flights });
        return Task.FromResult(new SynapseResult(true, result, "flight_search_result"));
    }
}
```

### HotelSearchHandler — same pattern, mock hotels

### PlaceDiscoveryHandler — same pattern, mock places

### Register in `InoNewSiloExtensions.cs`

Read the file first. Find where other handlers are registered (`AddKeyedSingleton<ISynapseHandler, ...>`). Add:

```csharp
silo.Services.AddKeyedSingleton<ISynapseHandler, FlightSearchHandler>("FlightSearchNeuron");
silo.Services.AddKeyedSingleton<ISynapseHandler, HotelSearchHandler>("HotelSearchNeuron");
silo.Services.AddKeyedSingleton<ISynapseHandler, PlaceDiscoveryHandler>("PlaceDiscoveryNeuron");
```

The key must match the `AgentType` from `TravelSkillSeeder.GetTravelSkills()`.

Build and test: `dotnet build ino.slnx && dotnet test ino.slnx`

Commit: `feat(travel): FlightSearch, HotelSearch, PlaceDiscovery synapse handlers with mock data`

---

## Task 2: ITokenBudget grain (C#)

**Files:**
- Create: `iaw/Core/Budget/ITokenBudget.cs`
- Create: `iaw/Core/Budget/TokenBudgetGrain.cs`
- Create: `iaw/Core/Budget/TokenBudgetState.cs`

### ITokenBudget interface

```csharp
namespace Core.Budget;

public interface ITokenBudget : IGrainWithStringKey
{
    Task<BudgetCheckResult> CheckBudgetAsync(CancellationToken ct = default);
    Task RecordUsageAsync(int tokensUsed, string source, CancellationToken ct = default);
    Task<TokenUsageSummary> GetUsageAsync(CancellationToken ct = default);
    Task SetLimitAsync(int monthlyLimit, CancellationToken ct = default);
    Task ResetAsync(CancellationToken ct = default);
}
```

### TokenBudgetState

```csharp
namespace Core.Budget;

[GenerateSerializer]
public sealed class TokenBudgetState
{
    [Id(0)] public int MonthlyLimit { get; set; } = 100_000;
    [Id(1)] public int TokensUsedThisMonth { get; set; }
    [Id(2)] public DateTimeOffset MonthStart { get; set; } = DateTimeOffset.UtcNow;
    [Id(3)] public List<TokenUsageEntry> RecentEntries { get; set; } = [];
}

[GenerateSerializer]
public sealed class TokenUsageEntry
{
    [Id(0)] public DateTimeOffset Timestamp { get; set; }
    [Id(1)] public int Tokens { get; set; }
    [Id(2)] public string Source { get; set; } = "";
}

[GenerateSerializer]
public sealed record BudgetCheckResult([property: Id(0)] bool Allowed, [property: Id(1)] int Remaining, [property: Id(2)] int Used, [property: Id(3)] int Limit);

[GenerateSerializer]
public sealed record TokenUsageSummary([property: Id(0)] int Used, [property: Id(1)] int Limit, [property: Id(2)] int Remaining, [property: Id(3)] DateTimeOffset MonthStart);
```

### TokenBudgetGrain

```csharp
namespace Core.Budget;

[GrainType("token-budget")]
public sealed class TokenBudgetGrain(
    [PersistentState("budget", "Default")] IPersistentState<TokenBudgetState> store)
    : Grain, ITokenBudget
{
    public Task<BudgetCheckResult> CheckBudgetAsync(CancellationToken ct = default)
    {
        ResetIfNewMonth();
        var remaining = store.State.MonthlyLimit - store.State.TokensUsedThisMonth;
        return Task.FromResult(new BudgetCheckResult(
            remaining > 0, remaining, store.State.TokensUsedThisMonth, store.State.MonthlyLimit));
    }

    public async Task RecordUsageAsync(int tokensUsed, string source, CancellationToken ct = default)
    {
        ResetIfNewMonth();
        store.State.TokensUsedThisMonth += tokensUsed;
        store.State.RecentEntries.Add(new TokenUsageEntry
        {
            Timestamp = DateTimeOffset.UtcNow, Tokens = tokensUsed, Source = source
        });
        if (store.State.RecentEntries.Count > 100)
            store.State.RecentEntries.RemoveRange(0, store.State.RecentEntries.Count - 100);
        await store.WriteStateAsync();
    }

    public Task<TokenUsageSummary> GetUsageAsync(CancellationToken ct = default)
    {
        ResetIfNewMonth();
        var remaining = store.State.MonthlyLimit - store.State.TokensUsedThisMonth;
        return Task.FromResult(new TokenUsageSummary(
            store.State.TokensUsedThisMonth, store.State.MonthlyLimit, remaining, store.State.MonthStart));
    }

    public async Task SetLimitAsync(int monthlyLimit, CancellationToken ct = default)
    {
        store.State.MonthlyLimit = monthlyLimit;
        await store.WriteStateAsync();
    }

    public async Task ResetAsync(CancellationToken ct = default)
    {
        store.State.TokensUsedThisMonth = 0;
        store.State.RecentEntries.Clear();
        store.State.MonthStart = DateTimeOffset.UtcNow;
        await store.WriteStateAsync();
    }

    void ResetIfNewMonth()
    {
        if (DateTimeOffset.UtcNow.Month != store.State.MonthStart.Month ||
            DateTimeOffset.UtcNow.Year != store.State.MonthStart.Year)
        {
            store.State.TokensUsedThisMonth = 0;
            store.State.RecentEntries.Clear();
            store.State.MonthStart = DateTimeOffset.UtcNow;
        }
    }
}
```

Add `"token-budget"` to `IAWConstants.GrainTypes` if following the pattern.

Build and test. Commit: `feat(budget): ITokenBudget grain with persistent per-user monthly tracking`

---

## Task 3: Travel rfw components (Flutter)

**Files:**
- Create: `ino.flutter/lib/ui/components/flight_card.dart`
- Create: `ino.flutter/lib/ui/components/hotel_card.dart`
- Create: `ino.flutter/lib/ui/components/place_card.dart`
- Modify: `ino.flutter/lib/ui/ino_runtime.dart` (register new libraries)

### FlightCard rfw component

```dart
LocalWidgetLibrary createFlightWidgets() {
  return LocalWidgetLibrary(<String, LocalWidgetBuilder>{
    'FlightCard': (BuildContext context, DataSource source) {
      final airline = source.v<String>(['airline']) ?? '';
      final from = source.v<String>(['from']) ?? '';
      final to = source.v<String>(['to']) ?? '';
      final price = source.v<int>(['price']) ?? 0;
      final date = source.v<String>(['date']) ?? '';
      final duration = source.v<String>(['duration']) ?? '';

      return Card(
        color: Theme.of(context).colorScheme.surfaceContainerHighest,
        margin: const EdgeInsets.symmetric(vertical: 4, horizontal: 8),
        child: Padding(
          padding: const EdgeInsets.all(12),
          child: Row(children: [
            const Icon(Icons.flight, size: 32),
            const SizedBox(width: 12),
            Expanded(child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text('$airline  $from → $to', style: Theme.of(context).textTheme.titleSmall),
                Text('$date · $duration', style: Theme.of(context).textTheme.bodySmall),
              ],
            )),
            Text('\$$price', style: Theme.of(context).textTheme.titleMedium?.copyWith(
              color: Theme.of(context).colorScheme.primary)),
          ]),
        ),
      );
    },
  });
}
```

### HotelCard — same pattern with name, location, price, rating, stars

### PlaceCard — same pattern with name, type, rating, reviewCount

### Register in ino_runtime.dart

Add new library registrations:
```dart
runtime.update(const LibraryName(<String>['ino', 'travel']), createFlightWidgets());
// merge hotel and place into same library or separate
```

Commit: `feat(flutter): rfw FlightCard, HotelCard, PlaceCard travel components`

---

## Task 4: Wire travel results into chat UI (Flutter)

**Files:**
- Modify: `ino.flutter/lib/state/ino_bloc.dart` — parse structured results from chat response
- Modify: `ino.flutter/lib/screens/home/home_screen.dart` — render travel cards when result type matches

The Chat response carries the specialist's structured JSON in the reply text. The InoBloc can detect travel results (JSON with `type: "flight_results"`) and store them as structured data. The home screen renders them as rfw cards.

For V1 simplicity: parse the reply text, if it starts with `{` try JSON decode, if it has a `type` field render the appropriate card list. Otherwise render as plain text.

### ChatMessage enhancement

Add optional structured data:
```dart
class ChatMessage {
  const ChatMessage({required this.text, required this.isUser, this.resultType, this.resultData});
  final String text;
  final bool isUser;
  final String? resultType;
  final List<Map<String, dynamic>>? resultData;
}
```

### Home screen card rendering

When a message has `resultType == 'flight_results'`, render a column of FlightCard-like widgets instead of a plain text bubble. Use native Flutter widgets (not rfw) for V1 simplicity.

Build, test, commit: `feat(flutter): render travel result cards in chat (flights, hotels, places)`

---

## Task 5: Integration verification

Build everything, run all tests, verify travel chat flow end-to-end.

```bash
dotnet build ino.slnx && dotnet test ino.slnx
cd ino.flutter && flutter analyze --no-fatal-infos && flutter test
```
