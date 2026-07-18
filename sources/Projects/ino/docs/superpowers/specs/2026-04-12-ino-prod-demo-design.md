# ino Prod Demo — Telegram Miniapp, Auth Cascade, Self-Evolve in 60 Seconds

> **Status:** design spec, brainstorm output, awaiting user review before handoff to `writing-plans`.
> **Date:** 2026-04-12
> **Supersedes:** portions of `2026-04-11-ino-200-domains-persona-design.md` and the `2026-04-12-ino-prod-integration.md` plan, specifically the auth-cascade section (which wrongly put per-user tokens in Aspire parameters).

## Goal

Deliver a 60-second, single-take Telegram-miniapp demo that hits every pillar of the ino vision: instant open (no modal gate), persona-top + one-card-below production layout, progressive OAuth cascade (Google + Uber), and visible L1 self-evolution (a novel query births a new neuron mid-flow). The demo is backed by a production-shaped codebase restructure that integrates TripRadar as the user/auth/billing backbone.

## Non-goals

- Booking closure (Phase D of the earlier plan) — search is the demo, book is a later pass.
- Real Uber integration — the demo ships `UberMockNeuron` behind a real-shape OAuth cascade, swappable to real Uber when sandbox access lands.
- Multi-persona switching (Jarvis/Luna/Cortex/Coach) — the demo ships one persona with the Rive state machine that will later drive variants.
- Cleanup of the 100+ `docs/superpowers/plans/*` and `docs/product_features/*` — separate pass.
- Full VGV-monorepo packaging — this spec uses a prod layout, not per-package publishing.

## The demo (60-second scripted scenario)

```
0:00  Tap Telegram /app → miniapp loads, Rive persona fades sleeping→idle,
      input auto-focused. No sign-in modal. No loading spinner after persona boot.

0:08  User types: "brief me on today"
0:09  Persona idle→thinking (orange), signal pulse ripples out.
0:10  BriefingNeuron → Tools.Auth.GetTokenAsync("google") → null.
0:11  AuthRequestCard (RFW): "ino needs your Google calendar & inbox" [Continue with Google]
0:14  Tap → Telegram.WebApp.openLink → Google consent in system browser.
0:22  Redirect back to bot → StoreOAuthTokenCommand writes to UserOAuthTokens (encrypted).
      Bot sends a rejoin message with an inline WebApp button + ?q=brief me on today.
      User taps → miniapp reopens → BriefingNeuron re-runs → BriefingCard renders.

0:28  User types: "get me a ride home"
0:30  UberMockNeuron → Tools.Auth.GetTokenAsync("uber") → null → Uber AuthRequestCard.
0:34  Tap → Uber OAuth (real initiator, mock token exchange endpoint) → token stored.
0:38  UberMockNeuron re-runs — but "home" doesn't resolve to coordinates.

0:40  EvolutionHandler fires. Persona thinking→evolving (purple). Timeline emits
      SelfImprovementL1. Inline SelfImprovementMicroCard: "Learning a new skill: home_resolver".
      LLM emits Neuron blueprint (SynapseSchema + ScriptSource + RfwTemplateSource).
0:44  ino asks via LocationPromptCard: "Where's home?"
0:46  User types: "Kyiv, Podil"
0:48  home_resolver stores location, resolves coordinates, fires back to UberMockNeuron.
0:50  RideCard: mini route map, UberX $4.20, 6 min ETA, [Request ride].
0:55  AppBar brain icon flashes (500ms purple pulse). Tap → Brain View shows new
      home_resolver node with pulse trails to UberMockNeuron and BriefingNeuron.
```

## Architecture — the four decisions that compose the system

| Decision | Resolved to | Rationale |
|---|---|---|
| Demo scope | **Full scope** — instant open + auth cascade + self-evolve + restructure + docs | Self-evolve is the product; any slice without it leaves the most important claim unbacked |
| Identity & auth | **Progressive** — Telegram `initData` = free identity, OAuth scopes granted per-skill on demand, AuthRequestCard is the shared prompt | No first-run modal; one template covers every future integration; Telegram already signs users in |
| Neuron shape | **Six-facet Neuron** — `SynapseSchema`, `ScriptSource`, `ToolRefs`, `ModelHints`, `FeatureSchema`, `RfwTemplateSource` | Only shape where a runtime-born neuron can ship a brand-new card in one LLM call |
| Solution restructure | **Prod layout** — `src/Core`, `src/Neurons`, `src/Gateways`, `src/Host`, `src/ServiceDefaults`, `Aspire/`, `clients/`, `domains/` with TripRadar-stays-put | Matches `deployment/Synapse/` reference; Orchestration→Synapse rename rides along |

## TripRadar is the prod backbone

**This is the most load-bearing discovery of the brainstorm.** TripRadar is not a domain pack ino uses — it's the user-management / auth / billing / persistence backbone for the whole ecosystem. ino's neuron layer sits on top.

### What TripRadar already provides (zero new code)

| Feature | File | What it gives us |
|---|---|---|
| User DDD aggregate | `TripRadar.Server.Domain/Aggregates/User.cs` | `CreateFromTelegramAuth(telegramUserId, ...)`, `CreateFromGoogleAuth(...)`, `UpdateGoogleData`, `UpdateTelegramUserId`, domain events |
| Telegram login | `TripRadar.Server.Application/UseCases/Authentication/Commands/TelegramLogin/` | `TelegramLoginCommand(TelegramAuthDataDTO)` validates initData HMAC, returns JWT |
| Google login | `.../Commands/GoogleLogin/` | `GoogleLoginCommand(email, firstName, lastName, googleId, profilePictureUrl)` creates or links user |
| JWT issuance | `TripRadar.Server.Infrastructure/Services/Authentication/AuthenticationTokenIssuer.cs` | Signed access + refresh token pair |
| UserProfiles table | `TripRadar.Server.Db/Models/UserProfiles.cs` | Unique indexes on `TelegramUserId`, `GoogleId`, `EmailHash`. Has `SecurityStamp`, refresh token, lockout columns. Encrypted-column-ready (see migration `20260320153000_ExpandEncryptedUserProfileColumns`) |
| Tier + subscription | `User.TierId`, `UserSubscription`, Stripe wiring | Apple-like $500/mo domain-pack billing foundation already in place |
| Telegram bot service | `TripRadar.Bot/Auth/AuthSessionSyncHandler.cs` | `/api/telegram/auth/session` endpoint + session cache + `TripRadarTokenClient` |
| Infra stack | `domains/travel/TripRadar/src/Aspire/` | Postgres + Redis + Elasticsearch + Kafka + Stripe + cloudflared tunnel, all wired via `builder.AddTripRadar()` extension method |
| ServiceDefaults | `TripRadar.ServiceDefaults` | OTel (logging, traces, metrics with OTLP export), health checks, service discovery, HTTP resilience |

### What's actually new (all additions)

1. **`UserOAuthTokens` table** in `TripRadar.Server.Db`. One migration. Columns: `Id`, `UserId` (FK), `Service` (string: "google" | "uber" | "spotify" | ...), `AccessToken` (encrypted via EF value converter), `RefreshToken` (encrypted), `Scopes` (csv), `ExpiresAt`, `CreatedOn`, `UpdatedOn`. Unique index on `(UserId, Service)`.

2. **`OAuthToken` value record** in `TripRadar.Server.Domain`.

3. **Four MediatR commands/queries** in `TripRadar.Server.Application/UseCases/Authentication/`:
   - `StoreOAuthTokenCommand(userId, OAuthToken)` — upsert
   - `GetOAuthTokenQuery(userId, service)` — returns `OAuthToken?` (null if missing/expired)
   - `RefreshOAuthTokenCommand(userId, service)` — calls provider refresh endpoint, upserts
   - `RevokeOAuthTokenCommand(userId, service)` — deletes row and revokes at provider if supported

4. **EF value converter** for `AccessToken`/`RefreshToken` columns in `TripRadar.Server.Infrastructure`. Uses `IDataProtectionProvider.CreateProtector("ino.oauth")`. Same pattern hinted at by the existing `ExpandEncryptedUserProfileColumns` migration.

5. **OAuth callback endpoints** on the consolidated bot: `POST /api/auth/google/callback`, `POST /api/auth/uber/callback`.

## The six-facet Neuron

### Record shape

```csharp
// src/Core/Neurons/Neuron.cs
[GenerateSerializer]
public sealed record Neuron(
    [property: Id(0)]  string Id,
    [property: Id(1)]  string Name,
    [property: Id(2)]  string Purpose,
    [property: Id(3)]  IReadOnlyList<string> Capabilities,
    [property: Id(4)]  DateTimeOffset CreatedAt,
    [property: Id(5)]  IReadOnlyDictionary<string, string> Metadata,
    [property: Id(6)]  string? SynapseSchema,       // facet 1: C# interface source — the verbs
    [property: Id(7)]  string? ScriptSource,        // facet 2: CSharpScript source — the logic
    [property: Id(8)]  IReadOnlyList<string> ToolRefs,    // facet 3: NEW — allowed grain interface names
    [property: Id(9)]  ModelHints? ModelHints,      // facet 4: NEW — model + system prompt + temperature
    [property: Id(10)] FeatureSchema? FeatureSchema, // facet 5: ML optimizer input vector (exists)
    [property: Id(11)] string? RfwTemplateSource,   // facet 6: NEW — CSharpScript producing (libDesc, data)
    [property: Id(12)] string? AuthorId,
    [property: Id(13)] string DomainId = "default");

[GenerateSerializer]
public sealed record ModelHints(
    [property: Id(0)] string Model,          // e.g. "gpt-54-mini" — baked in, not user-configurable
    [property: Id(1)] string SystemPrompt,
    [property: Id(2)] float Temperature = 0.2f);
```

### Facet semantics

| Facet | How it's used |
|---|---|
| `SynapseSchema` | Rendered into the LLM catalog by `SearchEngineGrain` for routing. Parsed via Roslyn `CSharpSyntaxTree` to extract verb/arg shapes (known-problem #7). |
| `ScriptSource` | Compiled and cached per-neuron-per-silo by `NeuronGrain.ExecuteScriptAsync` (already works). Returns `SynapseResult`. Globals inject `Grains`, `Synapse`, `Log`, **`Tools`**, **`Chat`**, **`Rfw`** (new). |
| `ToolRefs` | Whitelist of typed grain interfaces this neuron is allowed to call. The `Tools` facade is generated per-neuron from this list — calling anything not in the list fails at compile time. For the demo, `BriefingNeuron.ToolRefs = ["IAuthVault", "IGoogleCalendar", "IGoogleGmail", "IWeather"]`. |
| `ModelHints` | `Chat.AskAsync(...)` uses these. Model stays **baked in** per user feedback — authored per neuron, not user-configurable. |
| `FeatureSchema` | `NeuronMLOptimizer` vector dims. Already wired in `NeuronGrain.HandleAsync` today. |
| `RfwTemplateSource` | Second script, compiled separately, cached in parallel with `ScriptSource`. Called only if `SynapseResult` doesn't already have RFW bytes populated. Globals: `Result` (the SynapseResult), `Rfw` (builder). Returns `(byte[] desc, byte[] data)`. |

### Script runtime globals (the god-object facade)

```csharp
public class NeuronScriptGlobals
{
    public IGrainFactory Grains { get; init; }   // raw escape hatch
    public string NeuronId { get; init; }
    public Synapse Synapse { get; init; }
    public ILogger Log { get; init; }

    // NEW:
    public ToolFacade Tools { get; init; }       // sandboxed typed grain access
    public ChatFacade Chat { get; init; }        // LLM with ModelHints baked in
    public RfwBuilder Rfw { get; init; }         // fluent RFW tree + data
}
```

Example `BriefingNeuron.ScriptSource`:

```csharp
var token = await Tools.AuthVault.GetTokenAsync("google");
if (token is null)
    return SynapseResult.AuthRequired("google", ["calendar.readonly", "gmail.readonly"]);

var events = await Tools.GoogleCalendar.GetUpcoming(token, 3);
var unreadCount = await Tools.GoogleGmail.CountUnread(token);
var summary = await Chat.AskAsync(
    $"Narrate a morning brief: {events.Length} events, {unreadCount} emails.");

return SynapseResult.Ok("brief", new { summary, events, unreadCount });
```

### `Agent<T>` is a reflection adapter

Existing `IShell`, `IRoslyn`, `IFileSystem`, etc. don't change interfaces. `AgentRegistrationStartupTask` walks every `Agent<T>` subclass and generates a shadow `Neuron` record via reflection: `SynapseSchema` from typed methods, `ToolRefs` from prompt listings, `ScriptSource = null` (dispatches to the typed instance via specialist handler), `ModelHints` from the existing `[LLMAgent]` attribute, `RfwTemplateSource` from a new optional `[RfwCard]` attribute. ~50 LOC of glue. Zero edits to existing agents.

### How the demo neurons get registered

| Neuron | Registration style | Why |
|---|---|---|
| `BriefingNeuron`, `GoogleCalendarNeuron`, `GoogleGmailNeuron`, `UberMockNeuron` | **Compile-time** — `Agent<T>` subclass with `[LLMAgent]` + `[RfwCard]` attributes. Auto-registered at silo startup via `AgentRegistrationStartupTask` as shadow `Neuron` records. | They need typed method dispatch for grain refs + stable identity across restarts. The reflection adapter gives them the six-facet shape for free. |
| `home_resolver` (for a specific user) | **Runtime — L1 self-evolve.** Born via `EvolutionHandler` the first time `UberMockNeuron` can't resolve the word "home" for that user. | This is what the demo is proving. The handler emits a `Blueprint` with `ScriptSource` + `RfwTemplateSource` + `SynapseSchema`, `NeuronRegistryGrain.CreateAsync` persists it, `NeuronGrain.ActivateAsync` activates it. |

**Per-user neuron ID convention.** Runtime neurons born via evolution are scoped per user: ID format is `{baseId}_{userId}`, e.g. `home_resolver_demo_tg_100099`. The `EvolutionHandler` prompt (see `features/ino-new/InoNew.Core/Specialists/EvolutionHandler.cs` today) gets updated to emit IDs in this form when the triggering synapse has a non-null `userId` in its payload. This is how two users end up with independent `home_resolver` neurons pointing at different addresses.

### How `home_resolver` evolution is triggered

`UberMockNeuron.ScriptSource` handles the "get me a ride home" verb. Its pseudocode:

```csharp
var startCoords = await Tools.DeviceLocation.GetCurrentAsync();          // user's current position
var endText     = Synapse.Args.GetString("destination");                  // "home"

// Try to resolve via any registered location-resolver neurons for this user
var resolverId = "home_resolver_" + Synapse.UserId;
var resolver   = Grains.GetGrain<INeuron>(resolverId);
string? endCoords;
try {
    var result = await resolver.HandleAsync(
        new Synapse { Verb = "resolve", Payload = endText, UserId = Synapse.UserId });
    endCoords = result.Success ? result.Payload : null;
} catch (NeuronNotFoundException) {
    endCoords = null;
}

if (endCoords is null) {
    // Fire evolution with an explicit blueprint hint — not the default catch-all
    return SynapseResult.NeedsEvolution(
        baseId: "home_resolver",
        purpose: "Store and recall a user's saved locations (home, work, gym, ...)",
        hint: $"User wants to resolve '{endText}' to coordinates; no resolver exists");
}

// ... proceed with the ride estimate ...
```

`SynapseResult.NeedsEvolution` is a new factory that sets a flag on the result. `NeuronGrain.HandleAsync` sees the flag, dispatches to `EvolutionHandler` with the base id + purpose + hint, which builds the prompt, gets the LLM to emit a `Blueprint`, compiles it, persists it with the per-user scoped ID, and fires the original synapse at the new neuron (which then asks for "Where's home?" via the `LocationPromptCard` RFW template declared in its `RfwTemplateSource`).

### `NeuronGrain.HandleAsync` extension

Today's dispatch: specialist handler → script → default. New dispatch: specialist handler → typed `Agent<T>` adapter → script → default. After `ScriptSource` returns a `SynapseResult`, if no RFW bytes are set and `Neuron.RfwTemplateSource` is not null, run the RFW script with the result as input and attach the emitted bytes.

## Auth cascade

### Identity flow (uses existing TripRadar commands)

```
1. User taps Telegram /app — webview opens Flutter miniapp
2. Flutter reads window.Telegram.WebApp.initData
3. Flutter POSTs to ino.Bot/api/telegram/auth/session
4. AuthSessionSyncHandler (existing) → TripRadarTokenClient.GetTokenByTelegramAuthAsync
5. TripRadar.Server.API → TelegramLoginCommand (existing):
   - validates initData HMAC
   - if user doesn't exist: User.CreateFromTelegramAuth(telegramUserId, ...)
   - else: lookup by TelegramUserId
   - AuthenticationTokenIssuer → JWT (access + refresh)
6. Flutter receives JWT, stores in flutter_secure_storage
7. Every subsequent gRPC call attaches Authorization: Bearer <jwt>
8. ino's Orleans silo validates JWT using the same issuer secret from TripRadar.ServiceDefaults
```

**Net new code for identity: zero.** Wiring: Flutter calls existing endpoint; ino silo imports TripRadar's JWT validator.

### OAuth cascade (Google example)

```
 1. User types "brief me on today" → BriefingNeuron script fires
 2. Script calls Tools.AuthVault.GetTokenAsync("google")
 3. ino.AuthToolAdapter dispatches MediatR GetOAuthTokenQuery(userId, "google")
     → EF query on UserOAuthTokens → null (first time)
 4. Script returns SynapseResult.AuthRequired("google", [calendar.readonly, gmail.readonly])
 5. NeuronGrain runs shared AuthRequestCard RFW template → Flutter renders card
 6. User taps "Continue with Google"
 7. RFW event → gRPC FireSynapse("start_oauth", {service:"google", returnTo:"brief me on today"})
 8. src/Core/Auth/GoogleOAuthInitiator:
     - generates state GUID → stores {userId, returnTo, scopes, codeVerifier, exp} in Redis
     - generates PKCE S256 codeVerifier + challenge
     - builds accounts.google.com/o/oauth2/v2/auth URL with scopes
     - returns URL as SynapseResult payload
 9. Flutter → Telegram.WebApp.openLink(url) → system browser → Google consent
10. Google redirects to {cloudflared-url}/api/auth/google/callback?code=...&state=...
11. GoogleCallbackEndpoint (on ino.Bot, new):
     - validate state from Redis
     - exchange code for tokens at oauth2.googleapis.com/token
     - dispatch StoreOAuthTokenCommand(userId, new OAuthToken(...))
     - EF writes row to UserOAuthTokens (encrypted via value converter)
     - TelegramBotService sends the user a rejoin button:
         "✅ Google connected" + [Open ino] WebApp URL with ?q={returnTo}
12. User taps button → miniapp reopens → Flutter replays "brief me on today"
13. BriefingNeuron re-runs → Tools.AuthVault.GetTokenAsync("google") → token → briefing renders
```

### OAuthVaultGrain pivot

The already-committed `OAuthVaultGrain` in `iaw/Core/Auth/` becomes an Orleans-side per-user cache over the DB. First `GetAsync` per activation fetches via MediatR; subsequent calls within the same activation return from in-memory state. `StoreAsync` goes DB-first, then updates local state. Grain state is transient. **The DB is the source of truth.**

### Secrets split

| Secret | Home | Why |
|---|---|---|
| `GOOGLE__CLIENT_ID`, `GOOGLE__CLIENT_SECRET` | Aspire parameter → Key Vault in prod | Framework config, one per env |
| `TELEGRAM__BOT_TOKEN` | Aspire parameter | Framework config, one per env |
| `JWT__SIGNING_KEY` | Aspire parameter | Same key in TripRadar.API and ino silo |
| DataProtection master key | Aspire parameter (Azure Key Vault keyring in prod) | Encrypts column values |
| **User's Google access_token** | **`UserOAuthTokens` row, encrypted** | **Per-user, revocable, auditable** |
| **User's refresh token** | **`UserOAuthTokens` row, encrypted** | **Rotates via `RefreshOAuthTokenCommand`** |
| **User's Uber token** | **Same table, different `Service` value** | **One schema covers every provider** |

### UberMock path

For the demo, `UberOAuthInitiator` uses the real `https://login.uber.com/oauth/v2/authorize` URL (real state, real redirect) but the token-exchange endpoint is a local mock that returns a canned token. `UberMockNeuron` reads from the vault (same grain) and returns canned estimates shaped exactly like Uber v1.2 `/estimates/price`. Swapping to real Uber is a one-file change: `UberOAuthInitiator`'s token-exchange URL + `UberMockNeuron` → `UberRideNeuron`.

## Demo walk — Flutter + Rive + RFW

### HomeScreen rewrite

Today's `ino.flutter/lib/screens/home/home_screen.dart` is a chat-scroll with a shrinking persona. The new layout:

```
AppBar (history | brain | skills icons — 42px tall)
PersonaZone (33% of screen height) — RiveAnimation.asset('assets/rive/ino_persona.riv')
CardZone (remainder) — AnimatedSwitcher(400ms) over ActiveCard state:
    EmptyCard | BriefingCard(RFW) | RideCard(RFW) | AuthRequestCard(RFW) | MicroCard
InputBar (text + mic)
```

Chat history extracts to `/chat-history` route. The old `_ChatBubble` is moved wholesale. ~300 LOC removed from home_screen.dart, ~220 LOC added across `card_zone.dart`, `empty_state.dart`, `chat_history_screen.dart`.

### Rive persona state machine

One file: `assets/rive/ino_persona.riv`. State machine `"persona"`.

- **Inputs:** `emotion : enum { sleeping, idle, thinking, presenting, evolving, celebrating, confused }`, `pulse : trigger`, `energy : number 0..1`
- **States:** each emotion has a corresponding visual (colors match the existing `_colorForEmotion` map in `persona_widget.dart:181-196`)
- **Transitions:** any → any, cross-fade 250ms, via `emotion` enum input
- **Pulse:** fires a ripple overlay without transition

Fallback: if the .riv asset fails to load, the existing `_PersonaPainter` CustomPaint body (currently behind a loading spinner stub) becomes the emergency renderer.

**Spec note:** producing the .riv file needs a designer pass. Either adapt a Rive Community template that matches the orb aesthetic or hand-design. Flagged as an explicit task owner question in the plan.

### RFW templates shipped

| Template | Used by | Notes |
|---|---|---|
| `AuthRequestCardTemplate` | Every neuron on auth miss | Shared — service name drives icon + tint. One CTA fires `start_oauth` synapse. |
| `BriefingCardTemplate` | `BriefingNeuron` success | Max 4 data points: weather, top 3 events, urgent unreads, one action |
| `RideCardTemplate` | `UberMockNeuron` success | Mini route map, UberX price, ETA, Request button. Stitch "Uber Ride Card" is the visual target. |
| `LocationPromptCard` | `home_resolver` initial ask | Small inline card: "Where's home?" + text input |
| `SelfImprovementMicroCard` | `EvolutionHandler` | Ghost card: "Learning a new skill: {neuronId}" purple tint, 3s on-screen fade |

Existing travel templates (`FlightCardTemplate`, `HotelCardTemplate`, `PlaceCardTemplate`, `DestinationCardTemplate`) in `domains/travel/Ino.Travel/UI/` stay as reference patterns.

### Brain View new-neuron animation

`ino.flutter/lib/screens/brain/brain_view_screen.dart` already exists. Add a subscription to `StreamEvents(kind == "SelfImprovementL1")`: on receipt, animate a new node sliding in at a position computed from the new neuron's `DomainId` + `Id` hash, with a pulse trail to the parent neuron. AppBar brain icon gets a small purple dot badge when a new neuron is born + a 500ms flash during the demo moment. ~50 LOC added.

### Exactly what `ino.flutter/` changes

**New:**
- `assets/rive/ino_persona.riv`
- `lib/auth/telegram_init_data.dart` (JS interop to read `window.Telegram.WebApp.initData`)
- `lib/auth/telegram_auth_flow.dart` (POST /api/telegram/auth/session, store JWT)
- `lib/auth/oauth_return_listener.dart` (handle deep-link return)
- `lib/screens/home/card_zone.dart` (PersonaZone + CardZone + MicroCard)
- `lib/screens/home/empty_state.dart` (time-of-day ghost prompts)
- `lib/screens/chat_history/chat_history_screen.dart` (extracted from current home)
- `lib/ui/components/auth_request_card.dart` (fallback widget if RFW fails)

**Modified:**
- `lib/screens/home/home_screen.dart` (rewrite to persona-top + card-zone)
- `lib/persona/persona_widget.dart` (replace `_RivePlaceholder` stub with real Rive)
- `lib/grpc/ino_client.dart` (attach Authorization header from JWT)
- `lib/state/ino_bloc.dart` (`activeCard` state field + handlers)
- `lib/screens/brain/brain_view_screen.dart` (SelfImprovementL1 animation subscription)
- `lib/main.dart` + `lib/app.dart` (remove onboarding gate)
- `pubspec.yaml` (add `flutter_secure_storage`; `rive` already present at 0.14.5)

**Deleted:**
- `lib/screens/onboarding/onboarding_screen.dart` (no first-run gate anymore)

## Solution restructure

### Target tree

```
E:\ino\
├── src/                           ino-specific kernel + neurons
│   ├── Core/
│   │   ├── Neurons/               six-facet Neuron runtime (merged from features/ino-new/InoNew.Core)
│   │   ├── Synapse/               was iaw/Agents/Orchestration — rename pass lands here
│   │   ├── Auth/                  OAuthVaultGrain (cache) + AuthToolAdapter → MediatR
│   │   ├── Timeline/              decay + consolidation + SelfImprovementL1 events
│   │   ├── Registry/              NeuronRegistryGrain (was AgentRegistry)
│   │   ├── ML/                    NeuronMLOptimizer + FeatureCatalog
│   │   ├── Rfw/                   RfwBuilder globals + shared template loader
│   │   └── AI/                    ChatFacade + model mappers + IChatClient wrappers
│   ├── Neurons/                   capability implementations
│   │   ├── System/                IShell, IFileSystem, IGit, IAspire
│   │   ├── Coding/                IRoslyn, IDotNet, INuGet, IGitHub
│   │   ├── Web/                   IPlaywright
│   │   ├── Briefing/              NEW — BriefingNeuron
│   │   ├── Locations/             NEW — HomeResolverSeed
│   │   └── Integrations/          NEW — GoogleCalendarNeuron, GoogleGmailNeuron, UberMockNeuron
│   ├── Gateways/
│   │   ├── Grpc/                  InoService + Protos/ino.proto
│   │   └── Mcp/                   MCP server at :5300
│   ├── Host/                      ino Orleans silo
│   ├── ServiceDefaults/           NEW — pulled from TripRadar.ServiceDefaults pattern
│   └── Testing/                   InoTestHost, TestCluster fixtures
├── Aspire/
│   ├── ino.AppHost/               unified: wires TripRadar stack + ino silo + MCP + cloudflared
│   ├── ino.Hosting/               AddIno / WithLLM extensions
│   ├── ino.Client/                cluster client reg + OTel
│   └── ino.Deployment/            Pulumi stack (pulled from deployment/Synapse template)
├── clients/
│   ├── ino.flutter/               Flutter miniapp (moved from root)
│   └── ino.windows/               Windows Terminal fork (moved from root)
├── domains/
│   └── travel/
│       ├── Ino.Travel/            travel-specific neurons + RFW templates
│       └── TripRadar/             ★ STAYS EXACTLY AS IS ★
│           ├── TripRadar.slnx     own solution, own contributors
│           ├── CLAUDE.md          own agent rules
│           ├── src/Aspire/        own AppHost — kept for standalone build
│           ├── src/TripRadar.Server.*   referenced by ino.slnx
│           ├── src/TripRadar.Bot/       renamed ino.Bot — absorbs iaw/Telegram/ responsibilities
│           ├── src/TripRadar.MiniApp/   Blazor WASM — deprecated, kept as /auth-legacy
│           └── src/TripRadar.ServiceDefaults/   referenced, not duplicated
├── features/
│   └── timetravel/                stays; ino-new/ deleted after merge into src/Core
├── tests/                         was test/
│   ├── Core.Tests/
│   ├── Integration.Tests/
│   ├── E2E.Tests/                 Playwright + demo preflight harness
│   └── E2E.AppHost/
├── deployment/
│   └── Synapse/                   Aspire prod reference template — stays as documentation
├── docs/ website/ CLAUDE.md README.md ino.slnx
```

### Solution file strategy

- `ino.slnx` at root references new `src/*` projects + the TripRadar backbone projects (`TripRadar.Server.Domain`, `Application`, `Infrastructure`, `Db`, `Comms.Core`, `API.Contracts`, `ServiceDefaults`, `ino.Bot` aka `TripRadar.Bot` after rename).
- `domains/travel/TripRadar/TripRadar.slnx` stays for standalone travel dev (respects TripRadar's own CLAUDE.md rules).
- A project belonging to two solutions simultaneously is natively supported by .NET.

### Unified Aspire AppHost

`Aspire/ino.AppHost/AppHost.cs` calls TripRadar's existing `builder.AddTripRadar()` extension method (which returns a services record with Postgres/Redis/Elasticsearch/Kafka/Stripe/cloudflared already wired), then adds ino-specific services on top:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var googleClientId     = builder.AddParameter("google-client-id");
var googleClientSecret = builder.AddParameter("google-client-secret", secret: true);
var telegramBotToken   = builder.AddParameter("telegram-bot-token", secret: true);
var jwtSigningKey      = builder.AddParameter("jwt-signing-key", secret: true);
var dpMasterKey        = builder.AddParameter("dp-master-key", secret: true);

var tripradar = builder.AddTripRadar(config => config
    .WithPostgres()
    .WithRedis()
    .WithElasticsearch()
    .WithKafka()
    .WithStripe()
    .WithCloudflaredTunnel());

var inoSilo = builder.AddProject<Projects.ino_Host>("ino-silo")
    .WithReference(tripradar.Postgres)
    .WithReference(tripradar.Redis)
    .WithEnvironment("JWT__SIGNING_KEY", jwtSigningKey)
    .WithEnvironment("DP__MASTER_KEY", dpMasterKey);

var inoMcp = builder.AddProject<Projects.ino_Gateways_Mcp>("ino-mcp")
    .WithReference(inoSilo);

// The consolidated bot hosts everything user-facing on one ASP.NET service:
// Telegram webhook, Flutter miniapp static files, browser gRPC-Web + native gRPC,
// OAuth callbacks, OTLP bridge. This is the pattern iaw/Telegram already uses
// (two Kestrel listeners — HTTP/2-only for native gRPC, HTTP/1.1+2 for browser +
// static files) and avoids a CORS hop between the miniapp and gRPC.
tripradar.Bot
    .WithReference(inoSilo)                                      // Orleans client ↓
    .WithEnvironment("GOOGLE__CLIENT_ID", googleClientId)
    .WithEnvironment("GOOGLE__CLIENT_SECRET", googleClientSecret)
    .WithEnvironment("TELEGRAM__BOT_TOKEN", telegramBotToken)
    .WithEnvironment("JWT__SIGNING_KEY", jwtSigningKey)           // same key as TripRadar.API
    .WithEnvironment("DP__MASTER_KEY", dpMasterKey)
    .WithInoFrontend("clients/ino.flutter/build/web");            // new extension, see below

builder.Build().Run();
```

**`WithInoFrontend` extension** lives in `Aspire/ino.Hosting/InoHostingExtensions.cs`. It takes the Flutter web build directory, wires a `PhysicalFileProvider` into the bot's static-file middleware (with `Cache-Control: no-store` in dev), and registers the browser-facing gRPC-Web endpoint on the HTTP/1.1+2 Kestrel listener. Single service, two endpoints, zero CORS.

**JWT signing key is shared** between `TripRadar.Server.API` and ino-silo/ino.Bot via the same `jwt-signing-key` Aspire parameter. The existing TripRadar AppHost already reads it; the unified AppHost just passes it into the new services too.

TripRadar's own AppHost at `domains/travel/TripRadar/src/Aspire/` stays for standalone travel dev — it doesn't wire `ino-silo`, `ino-mcp`, or `WithInoFrontend`, so TripRadar devs see an unchanged experience.

### Bot consolidation (three phases)

**Phase 1 — absorb ino endpoints into TripRadar.Bot.** Add `/api/auth/google/callback`, `/api/auth/uber/callback`, `/ino` command dispatcher, `/otlp/v1/*` bridge, static file hosting for `clients/ino.flutter/build/web`, the `/app` bot command, **the native-gRPC and browser-gRPC-Web listeners** (both backed by the existing `InoService` implementation, which moves from `iaw/Telegram/Services/` to `src/Gateways/Grpc/`), and the Orleans cluster client registration so the bot can talk to `ino-silo`. Both bot services run in parallel during this phase.

**Phase 2 — switch the webhook over.** Re-register the Telegram webhook to point at TripRadar.Bot's existing `/api/telegram/webhook`. `iaw/Telegram/` becomes dead code.

**Phase 3 — delete and rename.** `git rm -r iaw/Telegram/`. `git mv domains/travel/TripRadar/src/TripRadar.Bot/ .../ino.Bot/`. Update `TripRadar.slnx` paths + CLAUDE.md.

### File move manifest (six batches)

1. **Rename Orchestration → Synapse** in place. `CodeOrchestratorAgent` → `SynapseNeuron`, `ICodeOrchestrator` → `ISynapseNeuron`, `OrchestrationResult` → `SynapseResult`, `iaw/Agents/Orchestration/` → `iaw/Agents/Synapse/`. Build + test checkpoint.
2. **`iaw/Core` + `features/ino-new/InoNew.Core` → `src/Core/`**. Fold the six-facet Neuron runtime. Delete `InoNew.Core` project. Build + test checkpoint.
3. **`iaw/Agents*`, `iaw/MCP`, `iaw/Agents.Host` → `src/Neurons/`, `src/Gateways/`, `src/Host/`**. One commit per top-level folder. Scripted namespace find/replace. Build + test per commit.
4. **`iaw/Aspire*`, `iaw/Testing` → `Aspire/`, `src/Testing/`**. Unify the AppHost (merge TripRadar extensions). Verify `aspire start` + all resources healthy + scripted chat scenario.
5. **Client moves + feature folds.** `git mv ino.flutter clients/ino.flutter`, `git mv ino.windows clients/ino.windows`, `git mv test tests`. Update paths in `ino.slnx`, `aspire.config.json`, `README.md`, `CLAUDE.md`. Final full E2E + aspire start.
6. **Bot consolidation** — the three phases above.

Total: ~140 files moved, ~15–20 commits, ~1–1.5 days of restructure work if no surprises.

## Docs pass

| File | Change |
|---|---|
| `README.md` (repo root) | Rewrite: one-screenshot hero (persona + briefing card), 60-second demo gif, three-primitives callout, `dotnet build ino.slnx && aspire start` quickstart, domain packs section listing TripRadar first. Delete old "kernel source is iaw/*" language. |
| `CLAUDE.md` (repo root) | Update all `iaw/*` → `src/*` paths. Add "Prod base: TripRadar" section. Replace known-problem #1 with "Synapse rename completed YYYY-MM-DD". Add "Auth cascade / OAuth vault" section. Update build/test commands for new paths. Leave `domains/travel/TripRadar/CLAUDE.md` untouched. |
| `website/` | VitePress "How it works" rewrite: six-facet Neuron + progressive auth cascade. Update Brain View / Genesis growth animation to reflect real L1 flow. No new pages. |
| `docs/superpowers/specs/2026-04-12-ino-prod-demo-design.md` | This file. |
| `docs/product/vision.md` | Short refresh — reframe "Apple-like ecosystem" with concrete example: TripRadar = travel domain pack, Coding = next. Existing content mostly stands. |
| `docs/superpowers/plans/2026-04-12-ino-prod-integration.md` | Add "SUPERSEDED by 2026-04-12-ino-prod-demo-design.md" banner atop. |

**Out of scope:** the 100+ `docs/superpowers/plans/*`, `docs/product_features/*` archival, `docs/deployment/*` pass, Telegram help text. Separate cleanup pass after demo.

## Tests

### Unit — `tests/Core.Tests/`

- `NeuronGrain` six-facet round-trip: activate, fire, handle, `RfwTemplateSource` render
- `OAuthVaultGrain` cache semantics: DB-miss → MediatR → cached → DB-hit
- `TelegramInitDataValidator` reuse: delegates to existing `TelegramLoginCommand` path
- `EvolutionHandler` end-to-end: BriefingNeuron-catalog → blueprint JSON → compile → activate

### Integration — `tests/Integration.Tests/` (Testcontainers Postgres)

- `UserOAuthTokens` migration applies cleanly
- EF value converter round-trips encrypted values
- `StoreOAuthTokenCommand` + `GetOAuthTokenQuery` MediatR flow (real DB)
- `GoogleCallbackEndpoint`: state validation + code exchange (mocked Google) + DB write

### E2E — `tests/E2E.Tests/`

- `BriefMeScenario`: full flow through `BriefingNeuron` with stubbed Google responses
- `RideHomeScenario`: full UberMock cascade with in-cluster OAuth state validation
- `EvolutionScenario`: novel "home" query → `EvolutionHandler` → new neuron visible in registry
- Playwright screenshots per scenario — published as demo-walk artifacts

### Demo preflight harness — `tests/E2E.Tests/DemoPreflight.cs`

```csharp
public class DemoPreflight : NeuronE2ETest
{
    [Fact] public async Task PreflightForLiveDemo()
    {
        // 1. framework readiness
        await AssertResource("tripradar-postgres").IsHealthy();
        await AssertResource("ino-silo").IsHealthy();
        await AssertResource("ino-mcp").IsHealthy();
        await AssertResource("tripradar-bot").IsHealthy();   // or "ino-bot" after Phase 3 rename

        // 2. demo user state reset
        var demoUserId = "demo_tg_100099";
        await Mediator.Send(new RevokeOAuthTokenCommand(demoUserId, "google"));
        await Mediator.Send(new RevokeOAuthTokenCommand(demoUserId, "uber"));
        await NeuronRegistry.DeleteNeuronAsync("home_resolver_" + demoUserId);
        await Timeline.ClearCorrelation("demo");

        // 3. scripted dry-run of each beat
        var brief = await ChatAsync("brief me on today", demoUserId);
        brief.RfwData.Should().Contain("auth_required");
        brief.Service.Should().Be("google");

        await SimulateGoogleCallback(demoUserId, ["calendar", "gmail"]);
        brief = await ChatAsync("brief me on today", demoUserId);
        brief.NeuronId.Should().Be("briefing");
        brief.RfwData.Should().Contain("events");

        var ride = await ChatAsync("get me a ride home", demoUserId);
        ride.Service.Should().Be("uber");  // first miss

        await SimulateUberCallback(demoUserId);
        ride = await ChatAsync("get me a ride home", demoUserId);
        ride.EvolutionEvents.Should().Contain(e => e.Kind == "SelfImprovementL1");
        ride.NeuronsCreated.Should().Contain("home_resolver");

        await ChatAsync("Kyiv, Podil", demoUserId);
        ride = await ChatAsync("get me a ride home", demoUserId);
        ride.RfwData.Should().Contain("estimate");

        // 4. reset to pristine for the live run
        await RevokeAllUserTokens(demoUserId);
        await NeuronRegistry.DeleteNeuronAsync("home_resolver_" + demoUserId);
    }
}
```

Run 30 seconds before the live demo via `dotnet test --filter DemoPreflight tests/E2E.Tests/`. Green = every beat verified and state pristine. Doubles as a permanent regression test.

## Open questions / flagged risks

1. **Rive asset authoring.** The `ino_persona.riv` file requires a designer pass — either adapting a Rive Community template or hand-design. Flagged as an explicit task-owner question for the implementation plan.
2. **Hot-reload vs rolling restart.** Known-problem #4 (Aspire topology frozen post-Build, Orleans grain-type manifest at silo startup) means L1 neuron creation via `EvolutionHandler` works (no assembly loading), L3 requires a rolling restart. The demo hits L1 only, so this is a non-blocker for the demo — but it's a spec-level risk to name.
3. **TripRadar schema ownership.** Adding `UserOAuthTokens` to `TripRadar.Server.Db` means ino and TripRadar share migration history. The spec commits to this direction; the alternative (a separate `ino.Db` project with its own migrations) would fragment user data. Confirming the shared-schema decision is correct.
4. **Encryption key rotation.** `IDataProtectionProvider` supports key rotation but we need to commit to a prod strategy (Azure Key Vault keyring vs managed DPAPI key ring). Not blocking for the demo; must be decided before public launch.
5. **Google OAuth consent screen verification.** Production Google OAuth with `calendar.readonly` and `gmail.readonly` scopes requires Google's consent-screen verification (weeks). For the demo, we use a dev/unverified consent screen capped at 100 test users. The demo user goes in the test-user list.
6. **Uber real sandbox.** Decision: ship `UberMockNeuron` for the demo. Real Uber is a one-file swap when sandbox access lands.

## Build order (sequence for the implementation plan)

```
Phase 0 — Preparation (parallelizable, ~half day)
  0.1 Context7 lookups: Rive Flutter API, google_sign_in OAuth flow, PKCE S256,
      Orleans IPersistentState encryption, EF value converters, Telegram WebApp spec
  0.2 Designer task: acquire/produce ino_persona.riv
  0.3 Google Cloud project + OAuth client ID + test user list
  0.4 Decide DataProtection key provider for dev (user-secrets is fine)

Phase 1 — TripRadar extensions (parallel with Phase 2, ~1 day)
  1.1 Create UserOAuthTokens DB model + migration in TripRadar.Server.Db
  1.2 Add OAuthToken domain record in TripRadar.Server.Domain
  1.3 Add MediatR Store/Get/Refresh/Revoke commands in TripRadar.Server.Application
  1.4 Add EF value converter for encrypted columns
  1.5 Integration tests: migration + MediatR + round-trip

Phase 2 — Six-facet Neuron runtime (parallel with Phase 1, ~1 day)
  2.1 Extend Neuron record with ToolRefs, ModelHints, RfwTemplateSource
  2.2 Add NeuronScriptGlobals.Tools, .Chat, .Rfw facades
  2.3 Generate per-neuron ToolFacade from ToolRefs
  2.4 Extend NeuronGrain.HandleAsync to run RfwTemplateSource after ScriptSource
  2.5 Reflection adapter for existing Agent<T> subclasses
  2.6 Core.Tests: facet round-trips

Phase 3 — Solution restructure (blocks everything downstream, ~1 day)
  3.1 Batch 1 — Orchestration → Synapse rename
  3.2 Batch 2 — Core folds
  3.3 Batch 3 — Agents/MCP/Host moves
  3.4 Batch 4 — Aspire/Testing + unified AppHost
  3.5 Batch 5 — Client moves + tests/ rename
  3.6 Batch 6 — Bot consolidation (three phases)

Phase 4 — Auth cascade wiring (depends on 1 + 2 + 3, ~1 day)
  4.1 GoogleOAuthInitiator + callback endpoint
  4.2 UberOAuthInitiator + mock token-exchange endpoint
  4.3 AuthRequestCard RFW template (shared)
  4.4 Flutter: TelegramInitData JS interop + auth session flow
  4.5 OAuthVaultGrain pivot to cache-over-DB
  4.6 E2E: BriefMeScenario + RideHomeScenario

Phase 5 — Demo neurons + Flutter UX (depends on 4, ~1.5 days)
  5.1 BriefingNeuron with six facets (ScriptSource + RfwTemplateSource)
  5.2 GoogleCalendarNeuron, GoogleGmailNeuron (tool adapters)
  5.3 UberMockNeuron + RideCardTemplate
  5.4 HomeResolverSeed (evolution target template)
  5.5 Flutter HomeScreen rewrite (PersonaZone + CardZone)
  5.6 Real Rive integration in PersonaWidget
  5.7 SelfImprovementMicroCard + Brain View L1 animation
  5.8 E2E: EvolutionScenario

Phase 6 — Demo preflight + docs (~half day)
  6.1 DemoPreflight harness
  6.2 README rewrite
  6.3 CLAUDE.md update
  6.4 Website "How it works" rewrite
  6.5 Dry run the live demo preflight

Total: ~5–6 days end-to-end, assuming Phase 0 prerequisites land in parallel.
```

## Acceptance criteria

1. `aspire start` brings up the full ecosystem: TripRadar (Postgres, Redis, Elasticsearch, Kafka, api, jobs, migrations, bot — with gRPC + Flutter static + OAuth callbacks layered in) + ino-silo + ino-mcp + cloudflared tunnel. Every resource Healthy in Aspire dashboard.
2. Opening `https://t.me/<bot>/app` (or the cloudflared-hosted Flutter web app) on a fresh phone loads the Flutter miniapp, validates Telegram `initData`, issues a JWT, and lands in the new persona-top layout with no sign-in modal.
3. The `DemoPreflight` harness passes end-to-end with resetting demo user state.
4. The scripted 60-second demo walk plays cleanly with: auth cascade (Google), auth cascade (Uber/mock), self-evolve (`home_resolver` created + visible in timeline + Brain View animation), final RideCard with a computed estimate.
5. All existing unit tests pass (`dotnet test ino.slnx`). E2E scenarios pass (`tests/E2E.Tests/`).
6. README renders a new hero screenshot + 60s demo gif. CLAUDE.md references `src/*` paths, mentions TripRadar as prod base, has the auth-cascade section.
7. The `iaw/` directory no longer exists at the repo root. `ino.slnx` references `src/*` projects. The Orchestration rename is complete.
