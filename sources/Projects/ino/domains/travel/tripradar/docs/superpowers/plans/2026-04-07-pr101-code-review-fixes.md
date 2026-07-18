# PR #101 Code Review Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the 5 backend issues identified in the code review of PR #101 (Full flights search experience).

**Architecture:** All fixes are localized to the Application and Domain layers. No database migrations needed. Changes touch metric constants, cache logic, service type enums, preference category mapping, and airport city name normalization.

**Tech Stack:** C# / .NET 11 / xUnit v3 / FluentAssertions / Moq

---

## File Map

| File | Action | Responsibility |
|------|--------|---------------|
| `src/TripRadar.Server.Application/Constants/MetricConstants.cs` | Modify:65 | Add missing description entry |
| `src/TripRadar.Server.Application/UseCases/SearchEngine/Flights/Queries/GetFlightPriceCalendar/GetFlightPriceCalendarQueryHandler.cs` | Modify:37,150 | Fix cache key + remove Size |
| `src/TripRadar.Server.Application/UseCases/SearchEngine/Flights/Queries/GetFlightNearbyPrices/GetFlightNearbyPricesQueryHandler.cs` | Modify:70 | Remove Size from cache entry |
| `src/TripRadar.Server.Domain/Enums/ServiceType.cs` | Modify:26-48 | Add FlightPriceCalendar + FlightNearbyPrices |
| `src/TripRadar.Server.Domain/Enums/PreferenceCategoryType.cs` | Modify:35 | Map new service types to Travel |
| `src/TripRadar.Server.Application/UseCases/SearchEngine/Flights/Queries/GetFlightNearbyPrices/GetFlightNearbyPricesQuery.cs` | Modify:16 | Use new ServiceType |
| `src/TripRadar.Server.Application/UseCases/SearchEngine/Flights/Queries/GetFlightPriceCalendar/GetFlightPriceCalendarQuery.cs` | Modify:16 | Use new ServiceType |
| `src/TripRadar.Server.Application/UseCases/Airports/Queries/SearchAirports/SearchAirportsQueryHandler.cs` | Modify:44-65 | Replace dash heuristic with known-corrections map |
| `src/TripRadar.Server.Tests/Airports/SearchAirportsQueryHandlerTests.cs` | Modify | Add NormalizeCityName regression tests |

---

### Task 1: Add missing metric description for GetFlightNearbyPricesRequest

**Files:**
- Modify: `src/TripRadar.Server.Application/Constants/MetricConstants.cs:65`

- [ ] **Step 1: Add the missing dictionary entry**

In `MetricConstants.cs`, insert a new entry for `GetFlightNearbyPricesRequest` after line 65 (the `GetFlightPriceCalendarRequest` entry):

```csharp
// Line 65 (existing):
{ GetFlightPriceCalendarRequest, "Number of flight price calendar requests" },
// Line 66 (add this):
{ GetFlightNearbyPricesRequest, "Number of flight nearby prices requests" },
```

The edit: find `{ GetFlightPriceCalendarRequest, "Number of flight price calendar requests" },` and replace with:

```csharp
        { GetFlightPriceCalendarRequest, "Number of flight price calendar requests" },
        { GetFlightNearbyPricesRequest, "Number of flight nearby prices requests" },
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build src/TripRadar.Server.Application/TripRadar.Server.Application.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/TripRadar.Server.Application/Constants/MetricConstants.cs
git commit -m "fix: add missing GetFlightNearbyPricesRequest metric description"
```

---

### Task 2: Fix price calendar stale cache key

**Files:**
- Modify: `src/TripRadar.Server.Application/UseCases/SearchEngine/Flights/Queries/GetFlightPriceCalendar/GetFlightPriceCalendarQueryHandler.cs:150`

The `BuildCacheKey` method on line 150 omits today's date from the key, so a response cached on April 7 is returned on April 8 with April 7 still appearing as a bookable future date. The fix adds today's date to the cache key so the cache naturally invalidates at day boundaries.

- [ ] **Step 1: Update BuildCacheKey to include today's date**

Find the `BuildCacheKey` method (line 150):

```csharp
    private static string BuildCacheKey(GetFlightPriceCalendarQuery query) =>
        $"price-calendar:{query.Request.DepartureId}:{query.Request.ArrivalId}:{query.Request.Year}:{query.Request.Month}:{query.Request.Currency ?? "USD"}";
```

Replace with:

```csharp
    private static string BuildCacheKey(GetFlightPriceCalendarQuery query) =>
        $"price-calendar:{query.Request.DepartureId}:{query.Request.ArrivalId}:{query.Request.Year}:{query.Request.Month}:{query.Request.Currency ?? "USD"}:{DateOnly.FromDateTime(DateTime.UtcNow)}";
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build src/TripRadar.Server.Application/TripRadar.Server.Application.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/TripRadar.Server.Application/UseCases/SearchEngine/Flights/Queries/GetFlightPriceCalendar/GetFlightPriceCalendarQueryHandler.cs
git commit -m "fix: include today's date in price calendar cache key to prevent stale entries"
```

---

### Task 3: Remove no-op Size = 1 from cache entries

**Files:**
- Modify: `src/TripRadar.Server.Application/UseCases/SearchEngine/Flights/Queries/GetFlightPriceCalendar/GetFlightPriceCalendarQueryHandler.cs:35-39`
- Modify: `src/TripRadar.Server.Application/UseCases/SearchEngine/Flights/Queries/GetFlightNearbyPrices/GetFlightNearbyPricesQueryHandler.cs:68-72`

`Size = 1` is meaningless without a `SizeLimit` on the `IMemoryCache` registration. Both handlers set it, both are no-ops. Remove the misleading property. The 24-hour absolute expiration is the actual eviction mechanism.

- [ ] **Step 1: Remove Size from GetFlightPriceCalendarQueryHandler**

Find (lines 35-39):

```csharp
        cache.Set(cacheKey, response, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheDuration,
            Size = 1
        });
```

Replace with:

```csharp
        cache.Set(cacheKey, response, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheDuration
        });
```

- [ ] **Step 2: Remove Size from GetFlightNearbyPricesQueryHandler**

Find (lines 68-72):

```csharp
                    cache.Set(dayKey, price, new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = CacheDuration,
                        Size = 1
                    });
```

Replace with:

```csharp
                    cache.Set(dayKey, price, new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = CacheDuration
                    });
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build src/TripRadar.Server.Application/TripRadar.Server.Application.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/TripRadar.Server.Application/UseCases/SearchEngine/Flights/Queries/GetFlightPriceCalendar/GetFlightPriceCalendarQueryHandler.cs src/TripRadar.Server.Application/UseCases/SearchEngine/Flights/Queries/GetFlightNearbyPrices/GetFlightNearbyPricesQueryHandler.cs
git commit -m "fix: remove no-op Size=1 from cache entries (no SizeLimit configured)"
```

---

### Task 4: Add dedicated ServiceType entries for price calendar and nearby prices

**Files:**
- Modify: `src/TripRadar.Server.Domain/Enums/ServiceType.cs:26,47`
- Modify: `src/TripRadar.Server.Domain/Enums/PreferenceCategoryType.cs:35`
- Modify: `src/TripRadar.Server.Application/UseCases/SearchEngine/Flights/Queries/GetFlightNearbyPrices/GetFlightNearbyPricesQuery.cs:16`
- Modify: `src/TripRadar.Server.Application/UseCases/SearchEngine/Flights/Queries/GetFlightPriceCalendar/GetFlightPriceCalendarQuery.cs:16`

Both queries fan out to many SerpApi calls (up to 31 for calendar, 11 for nearby) but share `ServiceType.FlightExplore` with the single-call explore query. This makes them indistinguishable in monitoring, quota, and billing. Add dedicated entries.

The seeder in `DbSeeder.Catalog.cs:SeedServiceTypes` automatically picks up new entries from `GetAllServices()`, so no migration is needed -- just adding the enum values and wiring them.

- [ ] **Step 1: Add new ServiceType entries**

In `src/TripRadar.Server.Domain/Enums/ServiceType.cs`, after line 26 (`GoogleLightSearch`), add:

```csharp
    public static readonly ServiceType FlightPriceCalendar = new(19, nameof(FlightPriceCalendar), "Flight price calendar lookup");
    public static readonly ServiceType FlightNearbyPrices = new(20, nameof(FlightNearbyPrices), "Flight nearby prices lookup");
```

- [ ] **Step 2: Add to GetAllServices() list**

In the same file, add the two new entries to the `GetAllServices()` return list (after `GoogleLightSearch` on line 47):

```csharp
        GoogleLightSearch,
        FlightPriceCalendar,
        FlightNearbyPrices
```

- [ ] **Step 3: Map new service types to Travel preference category**

In `src/TripRadar.Server.Domain/Enums/PreferenceCategoryType.cs`, add the two new types to the Travel category check in `GetByServiceType` (after the `FlightExplore` check on line 35):

Find:

```csharp
            Equals(serviceType, ServiceType.FlightExplore) ||
            Equals(serviceType, ServiceType.TripAdvisorSearch) ||
```

Replace with:

```csharp
            Equals(serviceType, ServiceType.FlightExplore) ||
            Equals(serviceType, ServiceType.FlightPriceCalendar) ||
            Equals(serviceType, ServiceType.FlightNearbyPrices) ||
            Equals(serviceType, ServiceType.TripAdvisorSearch) ||
```

- [ ] **Step 4: Update GetFlightPriceCalendarQuery to use new ServiceType**

In `src/TripRadar.Server.Application/UseCases/SearchEngine/Flights/Queries/GetFlightPriceCalendar/GetFlightPriceCalendarQuery.cs`, find line 16:

```csharp
    public ServiceType ServiceType => ServiceType.FlightExplore;
```

Replace with:

```csharp
    public ServiceType ServiceType => ServiceType.FlightPriceCalendar;
```

- [ ] **Step 5: Update GetFlightNearbyPricesQuery to use new ServiceType**

In `src/TripRadar.Server.Application/UseCases/SearchEngine/Flights/Queries/GetFlightNearbyPrices/GetFlightNearbyPricesQuery.cs`, find line 16:

```csharp
    public ServiceType ServiceType => ServiceType.FlightExplore;
```

Replace with:

```csharp
    public ServiceType ServiceType => ServiceType.FlightNearbyPrices;
```

- [ ] **Step 6: Build to verify**

Run: `dotnet build src/Aspire/Aspire.csproj`
Expected: Build succeeded. All references resolve.

- [ ] **Step 7: Commit**

```bash
git add src/TripRadar.Server.Domain/Enums/ServiceType.cs src/TripRadar.Server.Domain/Enums/PreferenceCategoryType.cs src/TripRadar.Server.Application/UseCases/SearchEngine/Flights/Queries/GetFlightPriceCalendar/GetFlightPriceCalendarQuery.cs src/TripRadar.Server.Application/UseCases/SearchEngine/Flights/Queries/GetFlightNearbyPrices/GetFlightNearbyPricesQuery.cs
git commit -m "fix: add dedicated ServiceType entries for price calendar and nearby prices"
```

---

### Task 5: Replace NormalizeCityName dash heuristic with known-corrections map

**Files:**
- Modify: `src/TripRadar.Server.Application/UseCases/Airports/Queries/SearchAirports/SearchAirportsQueryHandler.cs:44-65`
- Modify: `src/TripRadar.Server.Tests/Airports/SearchAirportsQueryHandlerTests.cs`

The current dash heuristic replaces the municipality with text before the first `-` in the airport name. This was intended for Paris-Beauvais but fires globally, producing wrong city names for airports like "Cross-Country Estates" or "Frankfurt-Hahn" (where the result happens to be correct by luck). Replace with an explicit known-corrections map that is safe and extensible.

- [ ] **Step 1: Write failing tests for NormalizeCityName**

The `NormalizeCityName` method is `private static`, so we test it through the public `Handle` method using the existing test infrastructure. Add tests to `SearchAirportsQueryHandlerTests.cs`.

Add these tests after the existing tests (after line 48):

```csharp
    [Theory]
    [InlineData("Paris-Beauvais Airport", "Tillé", "Paris")]
    [InlineData("Barcelona-El Prat Airport", "El Prat de Llobregat", "Barcelona")]
    [InlineData("Stockholm-Skavsta Airport", "Nyköping", "Stockholm")]
    public async Task Handle_NormalizesKnownCityOverrides(string airportName, string municipality, string expectedCity)
    {
        var airports = CreateAirports((Code: "TST", Name: airportName, City: municipality, Country: "xx", Lat: 0.0, Lng: 0.0, Type: "large_airport"));
        var countries = new List<Country> { new("XX", "Testland") };

        _airportRepo.Setup(r => r.SearchAsync("test", 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(airports);
        _unitOfWork.Setup(u => u.GetCountriesByCodesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(countries);

        var result = await _handler.Handle(new SearchAirportsQuery("test"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.First().City.Should().Be(expectedCity);
    }

    [Theory]
    [InlineData("Cross-Country Estates Airport", "Springfield", "Springfield")]
    [InlineData("Sharm el-Sheikh International Airport", "Sharm el-Sheikh", "Sharm El-Sheikh")]
    [InlineData("Al-Bateen Executive Airport", "Abu Dhabi", "Abu Dhabi")]
    public async Task Handle_DoesNotOverrideCityForNonKnownAirports(string airportName, string municipality, string expectedCity)
    {
        var airports = CreateAirports((Code: "TST", Name: airportName, City: municipality, Country: "xx", Lat: 0.0, Lng: 0.0, Type: "large_airport"));
        var countries = new List<Country> { new("XX", "Testland") };

        _airportRepo.Setup(r => r.SearchAsync("test", 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(airports);
        _unitOfWork.Setup(u => u.GetCountriesByCodesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(countries);

        var result = await _handler.Handle(new SearchAirportsQuery("test"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.First().City.Should().Be(expectedCity);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/TripRadar.Server.Tests/TripRadar.Server.Tests.csproj --filter "FullyQualifiedName~SearchAirportsQueryHandlerTests" -v minimal`
Expected: `Handle_DoesNotOverrideCityForNonKnownAirports` tests FAIL (Cross-Country returns "Cross" instead of "Springfield", etc.)

- [ ] **Step 3: Replace the dash heuristic with a known-corrections map**

In `SearchAirportsQueryHandler.cs`, replace the `NormalizeCityName` method (lines 44-65) with:

```csharp
    private static readonly Dictionary<string, string> KnownCityOverrides = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Paris-Beauvais"] = "Paris",
        ["Paris-Orly"] = "Paris",
        ["Paris-Charles de Gaulle"] = "Paris",
        ["Barcelona-El Prat"] = "Barcelona",
        ["Stockholm-Skavsta"] = "Stockholm",
        ["Stockholm-Arlanda"] = "Stockholm",
        ["Frankfurt-Hahn"] = "Frankfurt",
        ["Milan-Malpensa"] = "Milan",
        ["Milan-Bergamo"] = "Milan",
        ["London-Stansted"] = "London",
        ["London-Luton"] = "London",
        ["London-Gatwick"] = "London",
        ["London-Southend"] = "London"
    };

    private static string NormalizeCityName(string municipality, string airportName)
    {
        if (string.IsNullOrWhiteSpace(municipality)) return "";
        var trimmed = municipality.Trim();
        var parenIndex = trimmed.IndexOf('(');
        if (parenIndex > 0) trimmed = trimmed[..parenIndex].Trim();

        if (!string.IsNullOrWhiteSpace(airportName))
        {
            foreach (var (prefix, cityName) in KnownCityOverrides)
            {
                if (airportName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return ToTitleCase(cityName);
            }
        }

        return ToTitleCase(trimmed);
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/TripRadar.Server.Tests/TripRadar.Server.Tests.csproj --filter "FullyQualifiedName~SearchAirportsQueryHandlerTests" -v minimal`
Expected: All tests PASS.

- [ ] **Step 5: Run full test suite**

Run: `dotnet test`
Expected: All tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/TripRadar.Server.Application/UseCases/Airports/Queries/SearchAirports/SearchAirportsQueryHandler.cs src/TripRadar.Server.Tests/Airports/SearchAirportsQueryHandlerTests.cs
git commit -m "fix: replace broad dash heuristic with known city overrides map"
```

---

## Verification

After all 5 tasks are complete:

- [ ] **Full build**: `dotnet build src/Aspire/Aspire.csproj`
- [ ] **Full test suite**: `dotnet test`
- [ ] Start via Aspire and verify all resources are running with `mcp__aspire__list_resources`
