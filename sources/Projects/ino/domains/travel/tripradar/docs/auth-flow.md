# Telegram Mini App Auth Flow

Describes the full authentication chain from Telegram `/start` to a per-user JWT session in the TripRadar API.

---

## Overview

```
/start
  └─ TelegramUserGrain.InitializeAndShowMenuAsync
        └─ [UseMiniAppFlow=true] → send auth button (Mini App URL)
               │
               ▼
        User opens Mini App → /auth page
               │  reads Telegram.WebApp.initData
               ▼
        POST /api/telegram/auth/session   (Assistant.Silo)
               │
               ▼
        AuthContextService.SyncSessionAsync
               │  calls TripRadarTokenClient
               ▼
        POST /api/v1/tokens/sessions/telegram   (TripRadar API)
               │  validates initData, upserts user
               ▼
        JWT tokens → stored in IUserAuthSessionStore
               │
               ▼
        TelegramEndpoints → userGrain.ShowMenuAsync()
               └─ Bot sends main menu inline keyboard
```

---

## Step 1 — Bot: `/start` command

**File:** `src/Assistant/Assistant.Silo/Grains/Telegram/TelegramUserGrain.cs`

`ProcessUpdate` detects `/start` → calls `InitializeAndShowMenuAsync(chatId, ct)`.

### `InitializeAndShowMenuAsync`

```csharp
// When UseMiniAppFlow=true: skip forum topics, clear stale state
registry = new BotTopicRegistry(0, 0);   // topicless mode

// Check auth
if (telegramHostOptions.Value.UseMiniAppFlow && !IsAuthenticated())
{
    await SendAuthButtonAsync(chatId, telegram, ct);
    return;
}

// Already authenticated → show menu directly
await ShowMainMenuAsync(chatId, null, telegram, ct);
```

**`UseMiniAppFlow`** is set via env var `Telegram__UseMiniAppFlow=true`.
Config class: `src/Assistant/Assistant.Silo/Telegram/TelegramConfiguration.cs`

**`IsAuthenticated()`** (`TelegramUserGrain.State.cs:58`):
- reads stored `user-username` from grain state
- checks `IUserAuthSessionStore.TryGetByUsername(username, out _)`
- **Note:** in-memory store — resets on silo restart

### `SendAuthButtonAsync`

```csharp
var authUrl = $"{botFlowOptions.Value.MiniAppUrl.TrimEnd('/')}/auth?chatId={chatId}";
await telegram.SendMiniAppLaunch(chatId, "Authenticate with TripRadar", authUrl, null, ct);
```

**`MiniAppUrl`** resolves from (in order):
1. `Telegram:WebAppUrl` config
2. `Telegram__WebAppUrl` env var
3. Derived from `TELEGRAM_WEBHOOK_URL` by replacing `/api/telegram/webhook` with `/miniapp/`

Config composition: `src/Assistant/Assistant.Silo/TelegramBotFlow/Composition/ServiceCollectionExtensions.cs`

---

## Step 2 — Mini App: Auth page

**File:** `src/Assistant/Assistant.MiniApp.Ui/Pages/Auth.razor`
Route: `@page "/auth"`

On load:
1. Reads `chatId` from query string (`NavigationManager.Uri`)
2. Calls `window.Telegram.WebApp.initData` via JS interop (`getTelegramInitData`)
3. `POST /api/telegram/auth/session` with body `{ InitData, ChatId }`
4. On success → `window.Telegram.WebApp.close()` closes the Mini App
5. On failure → shows `MudAlert` with error

JS helpers in `src/Assistant/Assistant.MiniApp.Ui/wwwroot/index.html`:
```js
window.getTelegramInitData = () =>
    (window.Telegram?.WebApp) ? window.Telegram.WebApp.initData : '';
window.closeTelegramWebApp = () => window.Telegram?.WebApp?.close();
```

**`initData`** is a URL-encoded string like:
```
query_id=AAF...&user=%7B%22id%22%3A123...%7D&auth_date=1774000437&hash=abc...
```

---

## Step 3 — Session sync endpoint

**File:** `src/Assistant/Assistant.Silo/Telegram/TelegramEndpoints.cs`
Route: `POST /api/telegram/auth/session`

```csharp
app.MapPost(SessionSyncRoute, async (
    TokenSessionSyncRequest request,
    IAuthContextService authContextService,
    IFlightTrackingRegistry trackingRegistry,
    IGrainFactory grains,
    CancellationToken ct) =>
{
    var synced = await authContextService.SyncSessionAsync(request, ct);
    if (!synced.Success)
        return Results.BadRequest(...);

    if (request.ChatId is > 0)
    {
        trackingRegistry.RegisterUser(synced.Value!, request.ChatId.Value);
        var userGrain = grains.GetGrain<ITelegramUser>($"user-{request.ChatId.Value}");
        _ = userGrain.ShowMenuAsync(CancellationToken.None);  // fire-and-forget
    }

    return Results.Ok(new TokenSessionSyncResponse(true, synced.Value, null));
});
```

`TokenSessionSyncRequest` model: `src/Assistant/Assistant.Silo/TelegramBotFlow/Models/TokenSessionSyncRequest.cs`

---

## Step 4 — AuthContextService: session sync

**File:** `src/Assistant/Assistant.Silo/TelegramBotFlow/Auth/AuthContextService.cs`

`SyncSessionAsync` → detects `InitData` present → `SyncFromInitDataAsync`:
1. Calls `ITripRadarTokenClient.CreateTelegramSessionAsync(initData, ct)`
2. Reads `username` + `ExpiresAt` from returned JWT claims (`TokenClaimsReader`)
3. Upserts `UserAuthSession` into `IUserAuthSessionStore` (in-memory, keyed by username)

`IUserAuthSessionStore`: `src/Assistant/Assistant.Silo/TelegramBotFlow/Auth/IUserAuthSessionStore.cs`

---

## Step 5 — TripRadarTokenClient → TripRadar API

**File:** `src/Assistant/Assistant.Silo/TelegramBotFlow/TripRadar/TripRadarTokenClient.cs`

Sends:
```
POST http://_api-http.tripradar-api/api/v1/tokens/sessions/telegram
Body: { "initData": "..." }
```

Service discovery via Aspire: `_api-http.tripradar-api` resolves to the `tripradar-api` container.

---

## Step 6 — TripRadar API: validate & upsert user

**File:** `src/TripRadar/TripRadar.Server/TripRadar.Server.API/Controllers/TokenController.cs`
Route: `POST /api/v1/tokens/sessions/telegram`

Flow:
1. `ITelegramInitDataParser.TryParse(rawInitData)` → `TelegramAuthDataDTO`
   **File:** `src/TripRadar/TripRadar.Server/TripRadar.Server.Infrastructure/Services/Authentication/TelegramInitDataParser.cs`

2. Dispatches `TelegramLoginCommand` via MediatR

3. `TelegramLoginCommandHandler`:
   **File:** `src/TripRadar/TripRadar.Server/TripRadar.Server.Application/UseCases/Authentication/Commands/TelegramLogin/TelegramLoginCommandHandler.cs`
   - `ITelegramAuthValidationService.Validate(authData)` — HMAC-SHA256 check
     **File:** `src/TripRadar/TripRadar.Server/TripRadar.Server.Infrastructure/Services/TelegramAuthValidationService.cs`
     - Requires `TelegramSettings.BotToken` (env: `TelegramSettings__BotToken`)
     - Rejects `auth_date` older than **5 minutes** (`MaxAuthDateAgeSeconds = 300`)
   - `ITelegramAuthenticationService.UpsertUserAsync(authData, ct)` — create or find user
     **File:** `src/TripRadar/TripRadar.Server/TripRadar.Server.Infrastructure/Services/TelegramAuthenticationService.cs`

4. Returns `{ accessToken, refreshToken }`

---

## Step 7 — User upsert

**File:** `src/TripRadar/TripRadar.Server/TripRadar.Server.Infrastructure/Services/TelegramAuthenticationService.cs`

```csharp
// Try find by TelegramUserId
var existing = await unitOfWork.UserRepository.GetAuthByTelegramUserIdAsync(authData.Id, ct);
if (existing is not null) return existing;

// Create new user
var user = User.CreateFromTelegramAuth(authData.Id, authData.Username, ...);
// placeholder email: "{telegramUserId}@tg.local"
// auto-confirmed email, IsActive=true
```

Factory: `src/TripRadar/TripRadar.Server/TripRadar.Server.Domain/Aggregates/User.cs` → `CreateFromTelegramAuth`

---

## Step 8 — Bot: ShowMenuAsync

**File:** `src/Assistant/Assistant.Silo/Grains/Telegram/TelegramUserGrain.cs:225`

Called fire-and-forget from `TelegramEndpoints` after successful session sync:
```csharp
public async Task ShowMenuAsync(CancellationToken ct = default)
{
    var chatId = GetStoredChatId();
    var topics = LoadTopicRegistry();
    if (chatId == 0 || topics is null) return;

    var telegram = GrainFactory.GetGrain<ITelegram>("main");
    await ShowMainMenuAsync(chatId, NullIfZero(topics.WizardThreadId), telegram, ct);
}
```

Sends inline keyboard with: Flights / Stays / Maps / My trackings

---

## Configuration reference

| Parameter | Secret file key | Env var (in service) | Used by |
|---|---|---|---|
| Telegram bot token | `Parameters:telegram-bot-token` | `TelegramSettings__BotToken` | TripRadar API — HMAC validation |
| Telegram webhook URL | `Parameters:telegram-webhook-url` | `TELEGRAM_WEBHOOK_URL` | Assistant.Silo — webhook + MiniAppUrl derivation |
| `UseMiniAppFlow` | — | `Telegram__UseMiniAppFlow=true` | Assistant.Silo — auth gate |

Secrets file: `%APPDATA%\Microsoft\UserSecrets\c7c9b01e-2d0e-4c69-bfb4-b358bad4d324\secrets.json`

---

## Auth state lifecycle

- **Stored in grain:** `user-username` key in `IDurableDictionary<string, StateDescriptor>` (durable, file-backed)
- **Session store:** `IUserAuthSessionStore` — in-memory only, lost on silo restart
- **Consequence:** after silo restart, `IsAuthenticated()` returns `false` → user must re-open Mini App auth page

`IsAuthenticated()` checks BOTH grain state (has username) AND session store (has live JWT). If session store is empty (restart), the user will get the auth button again on next `/start`.
