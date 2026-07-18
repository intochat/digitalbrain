# ino Prod Integration — Built-in Neurons, Auth Cascade, and Production Layout

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Take ino from a developer prototype to production: surface the existing built-in neurons (System + Travel) through a redesigned Flutter UI, extend the working Google OAuth flow into a cascade that unlocks third-party services (Uber, Spotify), and lock in the production screen layout (persona top + single contextual card below).

**Architecture:** Four phases. Phase A (Discovery Surfacing) exposes the 16+ existing production-ready neurons in the Skills screen. Phase B (Production Layout) redesigns HomeScreen with persona on top and a single focused contextual card. Phase C (Auth Cascade) extends the existing Google OAuth to cover OAuth2 consent flows for Uber and Spotify. Phase D (Booking Closure) adds the missing leg — actually booking via the services ino can search.

**Tech Stack:** Flutter (go_router, flutter_bloc, rive), C# / Orleans (grains, persistent state, IPersistentState), gRPC + protobuf, Google OAuth 2.0, Uber OAuth 2.0, Aspire

**Spec:** `docs/superpowers/specs/2026-04-11-ino-200-domains-persona-design.md`
**Depends on:** `docs/superpowers/plans/2026-04-11-ino-200-domains-persona.md` (Phases 1-7 — completed, committed)

---

## Review: What Exists Today

### System Domain — All Compile-Time, All Production-Ready

These neurons already ship in the silo assembly, are auto-discovered at startup via reflection in `AgentRegistrationStartupTask.DiscoverAndBuildRecords()`, and are invocable today:

| Neuron | Interface | Location | Status |
|---|---|---|---|
| **IShell** | `iaw/Agents/Infrastructure/IShell.cs` | Command execution, PowerShell, dotnet wrapper | Production |
| **IGit** | `iaw/Agents/Infrastructure/IGit.cs` | Status, commit, diff, log, revert + metrics | Production |
| **IFileSystem** | `iaw/Agents/Infrastructure/IFileSystem.cs` | Read, write, list, search, archive, upload (15 tools) | Production |
| **IAspire** | `iaw/Agents/Infrastructure/IAspire.cs` | Resource restart, traces, logs, health monitoring | Production |
| **IDotNet** | `iaw/Agents.CSharp/DotNet/IDotNet.cs` | Build, test, format, run, list projects | Production |
| **IRoslyn** | `iaw/Agents.CSharp/Roslyn/IRoslyn.cs` | Type map, call graph, pattern detection, refactoring (13 tools) | Production |
| **INuGet** | `iaw/Agents.CSharp/NuGet/INuGet.cs` | Outdated packages, update monitoring | Production |
| **IGitHub** | `iaw/Agents.CSharp/GitHub/IGitHub.cs` | Releases, issues, repository | Production |
| **IPlaywright** | `iaw/Agents/Web/IPlaywright.cs` | Scrape, extract, browser automation | Production |
| **ICreator** | `iaw/Agents/Genesis/CreatorAgent.cs` (Phase 4) | Creates runtime skills on demand | Production |

**Gap:** None of these are visible to the end user through the Flutter Skills screen. The `SkillsBloc` → `ListSkills` pipeline exists, but no curation layer ensures these core neurons appear first.

### Travel Domain — Production-Ready SerpApi Integration

All travel neurons are in `domains/travel/Ino.Travel/Neurons/` and call real SerpApi + PostgreSQL:

| Neuron | Methods | Data Source | Status |
|---|---|---|---|
| **IFlightSearch** | SearchFlights, GetPriceCalendar, ExploreDestinations | SerpApi | Production — real prices |
| **IHotelSearch** | SearchHotels | SerpApi | Production — real properties |
| **IPlaceDiscovery** | FindPlaces, FindEvents | SerpApi (Google Local + Events) | Production |
| **IPriceTracker** | TrackFlight, GetTrackedPrices, StopTracking | Orleans + Kafka | Production — real alerts |
| **ITravelRecommender** | (orchestrator) | routes to other travel neurons | Production |
| **ITripVault** | SaveTrip, GetSavedTrips, RemoveTrip | PostgreSQL via EF Core | Production |
| **IUser** | Authenticate (Google), GetProfile, UpdatePreferences | Google OAuth 2.0 + PostgreSQL | Production |

**Flutter cards (already rendering real data):**
- `ino.flutter/lib/ui/components/flight_card.dart`
- `ino.flutter/lib/ui/components/hotel_card.dart`
- `ino.flutter/lib/ui/components/place_card.dart`

**Critical finding:** The travel domain already has working Google OAuth via `UserNeuron.Authenticate(googleIdToken)`. Token validation happens via `GoogleIdTokenValidator.cs` using `Google.Apis.Auth.GoogleJsonWebSignature.ValidateAsync()`. User persistence via `TripRadarDbContext`. This is the foundation the auth cascade extends.

**Gap:** The Flutter `ino.flutter` app doesn't call `IUser.Authenticate` — it currently has no sign-in flow. The auth lives in the TripRadar-specific backend.

### What's Missing for Full User Experience

1. **Flutter Skills screen** doesn't feature the built-in neurons prominently (shows whatever `ListSkills` returns, unordered)
2. **Flutter HomeScreen** is a chat scroll with dense cards — not the production persona-top + single-card-below layout
3. **No Google Sign-In button** in ino.flutter (exists in TripRadar backend)
4. **No OAuth cascade** to third-party services (Uber, Spotify) — Google auth gives us 8 Google services but stops there
5. **No booking/payment flow** — ino can search flights and hotels but can't book them
6. **No Uber neuron** — ground transport is the most-requested card design but the integration doesn't exist

---

## Phase A: Discovery Surfacing — Built-in Neurons in Skills

Make the 16+ existing production-ready neurons visible and prominent in the Skills screen. Tag each with domain (System, Coding, Travel, Genesis) and readiness (READY).

### Task A.1: Curate Skills display order

**Files:**
- Modify: `iaw/Core/Registry/AgentRegistrationStartupTask.cs` (around line 90-97, ExtractDomain)
- Modify: `iaw/Core/Registry/AgentRegistryGrain.cs` — GetAllAsync or ListSkillsAsync

- [ ] **Step 1: Widen domain extraction**

Current extractor returns "coding" for Agents.CSharp, "system" for Agents/Orchestration, "general" otherwise. Add travel:

```csharp
static string ExtractDomain(Type agentType)
{
    var ns = agentType.Namespace ?? "";
    if (ns.Contains("Ino.Travel")) return "travel";
    if (ns.Contains("Agents.Genesis")) return "genesis";
    if (ns.Contains("Agents.CSharp")) return "coding";
    if (ns.Contains("Agents")) return "system";
    return "general";
}
```

- [ ] **Step 2: Add a "Featured" ordering**

In the ListSkills pipeline, sort by domain priority: genesis → system → coding → travel → general. Within each domain, alphabetical.

- [ ] **Step 3: Expose skill metadata to Flutter**

Check the `SkillItem` proto message in `iaw/Telegram/Protos/ino.proto`. If it doesn't have `domain` and `capabilities` fields, add them. Fill from `AgentRecord.Domain` and `AgentRecord.Capabilities` in `InoService.ListSkills`.

- [ ] **Step 4: Verify**

```bash
dotnet build ino.slnx
```

Start Aspire, open the Flutter Skills screen, verify System and Travel neurons appear tagged correctly.

### Task A.2: Redesign Skills screen with domain sections

**File:** `ino.flutter/lib/screens/skills/skills_screen.dart`

- [ ] **Step 1: Group skills by domain in the UI**

Current screen shows a flat list. Change to grouped list with section headers: "System", "Coding", "Travel", "Genesis", "User-Created". Each section shows skill cards with:
- Icon (emoji from domain)
- Name in white 14px bold
- One-line description in #8b949e 11px
- Capability pills (max 3) as small pastel chips
- "READY" green tag if installed

- [ ] **Step 2: Skill detail sheet**

Tap any skill → bottom sheet showing full description, all capabilities, last-used timestamp, invocation count (from Phase 5 telemetry).

---

## Phase B: Production Screen Layout — Persona Top, Quality Card Bottom

The current HomeScreen is a chat scroll with messages. The production layout is fundamentally different: persona fills the top third of the screen (always visible, always reacting), and the bottom two-thirds is a single **contextual card zone** that shows what ino is doing right now. No chat thread.

### Task B.1: New HomeScreen structure

**File:** `ino.flutter/lib/screens/home/home_screen.dart`

- [ ] **Step 1: Replace chat scroll with three zones**

```
┌─────────────────────────────────┐
│                                 │
│      PERSONA ZONE (33%)         │  ← Rive/CustomPaint persona
│      • Large orb centered       │     emotion-reactive
│      • Name + status line       │     signal-pulse driven
│      • Ambient glow             │
│                                 │
├─────────────────────────────────┤
│                                 │
│      CONTEXTUAL CARD (55%)      │  ← Single focused card
│      • Morning briefing OR      │     changes with active task
│      • Active task card OR      │     quality > density
│      • Last result OR           │     never overloaded
│      • Empty state              │
│                                 │
├─────────────────────────────────┤
│      INPUT BAR (12%)            │  ← Text input + mic button
│      [Ask ino anything...]  🎤  │
└─────────────────────────────────┘
```

The chat history moves out to a separate `/chat-history` route accessible from a small "history" icon in the AppBar.

- [ ] **Step 2: Define the card zone state**

Add to InoBloc a new `activeCard` field on state:

```dart
sealed class ActiveCard {}
class EmptyCard extends ActiveCard {}
class BriefingCard extends ActiveCard {
  final WeatherSummary weather;
  final List<CalendarEvent> upcomingEvents;
  final int unreadEmails;
  final PortfolioSummary? portfolio;
  BriefingCard({required this.weather, required this.upcomingEvents, required this.unreadEmails, this.portfolio});
}
class DomainResultCard extends ActiveCard {
  final String domainKey;     // "flight" | "hotel" | "ride" | "music" | ...
  final Uint8List rfwDescription;
  final Uint8List rfwData;
  DomainResultCard({required this.domainKey, required this.rfwDescription, required this.rfwData});
}
class TelemetryCard extends ActiveCard {
  final TelemetryResponse response;
  TelemetryCard(this.response);
}
```

The UI watches `state.activeCard` and switches rendering accordingly.

- [ ] **Step 3: Build the card zone widget**

```dart
class _ContextualCardZone extends StatelessWidget {
  @override
  Widget build(BuildContext context) {
    return BlocBuilder<InoBloc, InoBlocState>(
      buildWhen: (p, c) => p.activeCard != c.activeCard,
      builder: (context, state) {
        return AnimatedSwitcher(
          duration: const Duration(milliseconds: 400),
          switchInCurve: Curves.easeOutCubic,
          child: switch (state.activeCard) {
            EmptyCard() => const _EmptyState(key: ValueKey('empty')),
            BriefingCard b => _BriefingCard(key: const ValueKey('briefing'), card: b),
            DomainResultCard d => _RfwResultCard(key: ValueKey('rfw-${d.domainKey}'), card: d),
            TelemetryCard t => BarChartCard(key: const ValueKey('telemetry'), title: t.response.title, entries: t.response.entries.map((e) => BarChartEntry(e.label, e.value)).toList()),
          },
        );
      },
    );
  }
}
```

- [ ] **Step 4: Empty state design**

When `activeCard = EmptyCard`, show a subtle suggestion grid — 4 soft-tap prompts based on time of day:
- Morning: "Brief me on today" / "What's the weather?" / "Any urgent emails?" / "Portfolio check"
- Afternoon: "Find lunch nearby" / "Plan a trip" / "Check my tasks" / "Play something"
- Evening: "Wrap up the day" / "Book a table" / "Get me home" / "Tomorrow's schedule"
- Night: "Sleep mode" / "Set alarm" / "Tomorrow at a glance" / "Good night"

Each prompt is a ghost-button with soft border and subtle glow on hover.

- [ ] **Step 5: Quality over density principle**

**Never put more than 4 distinct data points in a single card.** If a flight search returns 20 results, show the top 3 with a "View all 20" ghost link. If the portfolio has 50 tickers, show the top mover and a "Full portfolio" link. The card is a glance-and-act surface, not a dashboard.

### Task B.2: Extract chat history to a separate route

**File to create:** `ino.flutter/lib/screens/chat_history/chat_history_screen.dart`

- [ ] **Step 1: Create the screen**

Full chat scroll with all messages, bubble renderer, RFW cards inline. Basically the current HomeScreen rebuilt as `ChatHistoryScreen`.

- [ ] **Step 2: Add route in `app.dart`**

```dart
GoRoute(path: '/chat-history', builder: (_, __) => const ChatHistoryScreen()),
```

- [ ] **Step 3: Small history icon in HomeScreen AppBar**

```dart
IconButton(
  icon: const Icon(Icons.history),
  onPressed: () => context.push('/chat-history'),
),
```

---

## Phase C: Auth Cascade — Google Sign-In → OAuth Extensions

The travel domain already has real Google OAuth via `UserNeuron.Authenticate`. Phase C extends it so the same Google Sign-In flow unlocks Uber, Spotify, and other OAuth2 services via ino's secure token vault.

### Task C.1: Add Google Sign-In to ino.flutter

**Files:**
- `ino.flutter/pubspec.yaml`
- `ino.flutter/lib/auth/google_sign_in_flow.dart` (new)
- `ino.flutter/lib/screens/onboarding/onboarding_screen.dart`

- [ ] **Step 1: Add google_sign_in dependency**

```bash
cd ino.flutter && flutter pub add google_sign_in
```

- [ ] **Step 2: Create Google sign-in wrapper**

```dart
// ino.flutter/lib/auth/google_sign_in_flow.dart
import 'package:google_sign_in/google_sign_in.dart';

class GoogleSignInFlow {
  static final _googleSignIn = GoogleSignIn(
    scopes: [
      'email',
      'profile',
      'openid',
      'https://www.googleapis.com/auth/calendar.readonly',
      'https://www.googleapis.com/auth/gmail.readonly',
      'https://www.googleapis.com/auth/drive.readonly',
    ],
  );

  /// Returns a Google ID token that the backend validates via UserNeuron.Authenticate.
  static Future<String?> signIn() async {
    try {
      final account = await _googleSignIn.signIn();
      if (account == null) return null;
      final auth = await account.authentication;
      return auth.idToken;
    } catch (e) {
      return null;
    }
  }

  static Future<void> signOut() async {
    await _googleSignIn.signOut();
  }

  static Future<GoogleSignInAccount?> currentUser() async {
    return await _googleSignIn.signInSilently();
  }
}
```

- [ ] **Step 3: Wire onboarding to call backend**

Modify `onboarding_screen.dart` to show a "Continue with Google" button on the final step. On tap:

```dart
final idToken = await GoogleSignInFlow.signIn();
if (idToken != null) {
  // Call the backend via a new gRPC method that forwards to IUser.Authenticate
  final session = await inoClient.authenticateWithGoogle(idToken);
  // Save session locally (secure storage)
  // Navigate to home
  context.go('/home');
}
```

### Task C.2: Expose IUser.Authenticate via gRPC

**Files:**
- `iaw/Telegram/Protos/ino.proto`
- `iaw/Telegram/Services/InoService.cs`

- [ ] **Step 1: Add auth proto messages**

```protobuf
message AuthenticateWithGoogleRequest {
  string id_token = 1;
}

message AuthenticateWithGoogleResponse {
  string session_token = 1;
  string user_id = 2;
  string email = 3;
  string display_name = 4;
  int64 expires_at_unix = 5;
}

rpc AuthenticateWithGoogle(AuthenticateWithGoogleRequest) returns (AuthenticateWithGoogleResponse);
```

- [ ] **Step 2: Implement in InoService**

Route to `IUser.Authenticate` on the travel domain's User grain. Since the travel domain has its own auth, import its DTOs or call through a shared interface.

```csharp
public override async Task<AuthenticateWithGoogleResponse> AuthenticateWithGoogle(
    AuthenticateWithGoogleRequest request, ServerCallContext context)
{
    var user = _clusterClient.GetGrain<IUser>(GetUserKey(request.IdToken));
    var result = await user.Authenticate(request.IdToken);
    // Map result to response
    return new AuthenticateWithGoogleResponse
    {
        SessionToken = result.SessionToken,
        UserId = result.UserId,
        Email = result.Email,
        DisplayName = result.DisplayName,
        ExpiresAtUnix = result.ExpiresAt.ToUnixTimeSeconds(),
    };
}
```

### Task C.3: Build the OAuth Token Vault

**Files to create:**
- `iaw/Core/Auth/IOAuthVault.cs` — Orleans grain interface
- `iaw/Core/Auth/OAuthVaultGrain.cs` — implementation with IPersistentState
- `iaw/Core/Auth/OAuthToken.cs` — record type

- [ ] **Step 1: Define the vault interface**

```csharp
namespace IAW.Core.Auth;

public interface IOAuthVault : IGrainWithStringKey
{
    Task StoreAsync(string service, OAuthToken token);
    Task<OAuthToken?> GetAsync(string service);
    Task RemoveAsync(string service);
    Task<IReadOnlyList<string>> ListServicesAsync();
}

public record OAuthToken(
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset ExpiresAt,
    string Scopes);
```

The grain key is the user ID. Per-user vault.

- [ ] **Step 2: Implement with persistent state**

```csharp
public class OAuthVaultGrain : Grain, IOAuthVault
{
    private readonly IPersistentState<OAuthVaultState> _state;
    
    public OAuthVaultGrain([PersistentState("oauth-vault", "oauth-store")] IPersistentState<OAuthVaultState> state)
    {
        _state = state;
    }
    
    public async Task StoreAsync(string service, OAuthToken token)
    {
        _state.State.Tokens[service] = token;
        await _state.WriteStateAsync();
    }
    
    public Task<OAuthToken?> GetAsync(string service)
    {
        if (_state.State.Tokens.TryGetValue(service, out var token))
        {
            if (token.ExpiresAt > DateTimeOffset.UtcNow)
                return Task.FromResult<OAuthToken?>(token);
        }
        return Task.FromResult<OAuthToken?>(null);
    }
    
    public async Task RemoveAsync(string service)
    {
        if (_state.State.Tokens.Remove(service))
            await _state.WriteStateAsync();
    }
    
    public Task<IReadOnlyList<string>> ListServicesAsync() 
        => Task.FromResult<IReadOnlyList<string>>(_state.State.Tokens.Keys.ToList());
}

[GenerateSerializer]
public class OAuthVaultState
{
    [Id(0)] public Dictionary<string, OAuthToken> Tokens { get; set; } = new();
}
```

- [ ] **Step 3: Configure encryption at rest**

Tokens are sensitive. Use Aspire parameter encryption for the storage provider, or wrap values with `IDataProtectionProvider` before storing. Check existing TripRadar secret-handling patterns in `EnvironmentSecretsProvider.cs`.

### Task C.4: Add Uber OAuth flow card

**Files:**
- `ino.flutter/lib/ui/components/auth_request_card.dart` (new)
- `iaw/Agents/Integrations/UberAuthNeuron.cs` (new)

- [ ] **Step 1: Create UberAuthNeuron**

Neuron that initiates Uber OAuth2 flow. When ino needs Uber (user says "get me home") and the OAuthVault has no Uber token, UberAuthNeuron returns an auth request card instead of a ride card.

```csharp
[LLMAgent("UberAuth",
    "Initiates Uber OAuth2 consent flow. Called when the user wants a ride but hasn't authorized Uber yet.",
    Model = "gpt-54-mini")]
public class UberAuthNeuron : Agent<IUberAuth>
{
    protected override IEnumerable<Tool> DefineTools() =>
    [
        Tool.Create("request_authorization",
            "Generate an Uber OAuth authorization URL the user can tap to grant consent. Returns the URL and a state token for the callback.",
            async () =>
            {
                var state = Guid.NewGuid().ToString("N");
                var clientId = Environment.GetEnvironmentVariable("UBER__CLIENT_ID")!;
                var redirectUri = Environment.GetEnvironmentVariable("UBER__REDIRECT_URI")!;
                var scope = "profile request history";
                var url = $"https://login.uber.com/oauth/v2/authorize" +
                    $"?response_type=code&client_id={clientId}" +
                    $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                    $"&scope={Uri.EscapeDataString(scope)}&state={state}";
                // Save state for callback validation
                await _cache.SetAsync($"uber-state:{state}", "pending", TimeSpan.FromMinutes(10));
                return $"{{\"authUrl\":\"{url}\",\"state\":\"{state}\"}}";
            }),
    ];
}
```

- [ ] **Step 2: Callback handler**

Add a gRPC method `CompleteUberAuth(code, state)` that exchanges the code for tokens and stores in OAuthVault:

```csharp
public override async Task<CompleteUberAuthResponse> CompleteUberAuth(
    CompleteUberAuthRequest request, ServerCallContext context)
{
    // Validate state
    var pending = await _cache.GetAsync($"uber-state:{request.State}");
    if (pending is null) throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid state"));
    
    // Exchange code for tokens
    using var http = new HttpClient();
    var response = await http.PostAsync("https://login.uber.com/oauth/v2/token",
        new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = Environment.GetEnvironmentVariable("UBER__CLIENT_ID")!,
            ["client_secret"] = Environment.GetEnvironmentVariable("UBER__CLIENT_SECRET")!,
            ["code"] = request.Code,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = Environment.GetEnvironmentVariable("UBER__REDIRECT_URI")!,
        }));
    var json = await response.Content.ReadAsStringAsync();
    var tokens = JsonSerializer.Deserialize<UberTokenResponse>(json)!;
    
    // Store in vault
    var userId = GetUserIdFromContext(context);
    var vault = _clusterClient.GetGrain<IOAuthVault>(userId);
    await vault.StoreAsync("uber", new OAuthToken(
        AccessToken: tokens.AccessToken,
        RefreshToken: tokens.RefreshToken,
        ExpiresAt: DateTimeOffset.UtcNow.AddSeconds(tokens.ExpiresIn),
        Scopes: tokens.Scope));
    
    return new CompleteUberAuthResponse { Success = true };
}
```

- [ ] **Step 3: Flutter AuthRequestCard**

```dart
class AuthRequestCard extends StatelessWidget {
  final String serviceName;        // "Uber"
  final String serviceIcon;        // "🚗"
  final String reason;             // "to call you a ride"
  final String authUrl;            // OAuth URL
  final VoidCallback onComplete;   // poll for callback

  const AuthRequestCard({
    super.key,
    required this.serviceName,
    required this.serviceIcon,
    required this.reason,
    required this.authUrl,
    required this.onComplete,
  });

  @override
  Widget build(BuildContext context) {
    return Card(
      color: const Color(0xFF161b22),
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(14),
        side: const BorderSide(color: Color(0xFF21262d)),
      ),
      child: Padding(
        padding: const EdgeInsets.all(20),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Text(serviceIcon, style: const TextStyle(fontSize: 42)),
            const SizedBox(height: 12),
            Text('ino needs permission to use $serviceName',
              style: const TextStyle(fontSize: 14, fontWeight: FontWeight.w700, color: Color(0xFFe6e6e6)),
              textAlign: TextAlign.center),
            const SizedBox(height: 6),
            Text('$reason — one tap, one time.',
              style: const TextStyle(fontSize: 11, color: Color(0xFF8b949e)),
              textAlign: TextAlign.center),
            const SizedBox(height: 16),
            FilledButton(
              onPressed: () async {
                await launchUrl(Uri.parse(authUrl), mode: LaunchMode.externalApplication);
                // Poll backend for completion, then onComplete
              },
              style: FilledButton.styleFrom(
                backgroundColor: const Color(0xFF000000),
                foregroundColor: Colors.white,
                padding: const EdgeInsets.symmetric(horizontal: 32, vertical: 12),
              ),
              child: Text('Continue with $serviceName'),
            ),
            const SizedBox(height: 8),
            Text('ino never sees your password.',
              style: const TextStyle(fontSize: 9, color: Color(0xFF555))),
          ],
        ),
      ),
    );
  }
}
```

### Task C.5: UberRide neuron (uses vault)

**File:** `iaw/Agents/Integrations/UberRideNeuron.cs` (new)

- [ ] **Step 1: Implement ride search + request**

```csharp
[LLMAgent("UberRide",
    "Call an Uber ride. Requires Uber OAuth authorization (handled by UberAuth neuron).",
    Model = "gpt-54-regular")]
public class UberRideNeuron : Agent<IUberRide>
{
    protected override IEnumerable<Tool> DefineTools() =>
    [
        Tool.Create("search_rides",
            "Get ride estimates between two coordinates. Returns UberX, UberXL, UberBlack options with prices and ETAs.",
            async (double startLat, double startLng, double endLat, double endLng) =>
            {
                var userId = GetCurrentUserId();
                var vault = IAW.Get<IOAuthVault>(userId);
                var token = await vault.GetAsync("uber");
                if (token is null)
                    return "ERROR: No Uber authorization. Call UberAuth.request_authorization first.";
                
                using var http = new HttpClient();
                http.DefaultRequestHeaders.Authorization = 
                    new AuthenticationHeaderValue("Bearer", token.AccessToken);
                var url = $"https://api.uber.com/v1.2/estimates/price" +
                    $"?start_latitude={startLat}&start_longitude={startLng}" +
                    $"&end_latitude={endLat}&end_longitude={endLng}";
                var response = await http.GetStringAsync(url);
                return response; // JSON, formatted by UI layer
            }),
        
        Tool.Create("request_ride",
            "Request an actual Uber ride. Uses the product_id returned by search_rides.",
            async (string productId, double startLat, double startLng, double endLat, double endLng) =>
            {
                // Similar pattern: get token, POST to /v1.2/requests
            }),
    ];
}
```

- [ ] **Step 2: Wire to RideCard RFW template**

Create `iaw/Agents/Integrations/UI/RideCardTemplate.cs` that converts Uber JSON responses to RFW card data. Follow the pattern from `FlightCardTemplate.cs` in the travel domain.

---

## Phase D: Booking Closure (Deferred)

Flight and hotel booking endpoints. This closes the travel flow: search → select → book → pay → confirm.

**Depends on:** Booking.com partner API credentials OR a meta-booking provider (Skyscanner doesn't actually book, just redirects). Payment via existing Stripe integration in TripRadar.

**Scope:** Separate plan. Not needed for demo — the search flow alone is impressive.

---

## Execution Order

```
Phase A (Discovery Surfacing) — ~1 day
  │
  ├─→ Task A.1 domain extraction
  └─→ Task A.2 Skills UI grouping
       │
Phase B (Production Layout) — ~2 days
  │
  ├─→ Task B.1 HomeScreen rewrite (persona + card zone + input)
  └─→ Task B.2 ChatHistory extraction
       │
Phase C (Auth Cascade) — ~3 days
  │
  ├─→ Task C.1 Google Sign-In in Flutter
  ├─→ Task C.2 AuthenticateWithGoogle gRPC
  ├─→ Task C.3 OAuth Vault grain (can run parallel with C.2)
  ├─→ Task C.4 AuthRequestCard + UberAuth neuron
  └─→ Task C.5 UberRide neuron
       │
Phase D — DEFERRED
```

Phases A and B are independent — can run in parallel. Phase C depends on the vault grain existing, but Google Sign-In in Flutter can happen without the vault.

---

## Production Quality Gate

Before shipping:

1. **End-to-end user scenario test:**
   - Fresh install of ino.flutter
   - Tap "Continue with Google" → real Google OAuth flow → persisted session
   - "Brief me" → BriefingCard with real weather (AccuWeather API key required), real Google Calendar events, real Gmail unread count
   - "Get me home" → UberAuth card → user taps → browser OAuth → return to app → RideCard with real estimate → tap "Request" → real Uber ride
   - "Find flights to Bali next July" → FlightSearch neuron → real SerpApi results → 3 best options shown in card

2. **Aspire telemetry verification:**
   - Dashboard shows ino.skills.active gauge moving as skills install
   - ino.persona.switches counter increments on "you are Jarvis"
   - Trace view shows gRPC → UserNeuron → GoogleIdTokenValidator span chain

3. **Performance gates:**
   - Cold start to interactive: < 3s on mobile
   - Google sign-in round-trip: < 2s (network-bound)
   - Domain card render (post-skill-response): < 500ms
   - Persona signal-pulse animation: 60fps sustained

4. **Safety gates:**
   - OAuth tokens encrypted at rest (IDataProtectionProvider)
   - State parameter validated on every OAuth callback
   - Refresh tokens rotated on use
   - User can revoke any service from Skills screen → "Revoke Uber"

---

## Design Principles (Locked In)

These are non-negotiable for anyone touching the Flutter UI:

1. **Persona is always visible.** Never hide it. It's the OS.
2. **One card at a time.** Never more than one contextual card below the persona. If you need to show multiple things, make one aggregate card with a "View all" link.
3. **Max 4 data points per card.** If you're tempted to show 5, you're designing a dashboard, not a card.
4. **Every card is an action surface.** A flight card has "Book". A ride card has "Request". A calendar card has "Join". Cards without actions are just information — reserve them for telemetry and briefings.
5. **Authorization is a first-class UI.** The AuthRequestCard is a legitimate card type, not an afterthought. It's how ino grows its reach.
6. **Signals drive animations.** The persona reacts to real system state. Any animation that's not driven by a signal is suspicious — delete it or tie it to something real.
7. **Quality > density.** Users who want density can scroll to chat history. The home view is a glance-and-act surface.
