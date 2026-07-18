# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Repository location

This product was relocated from `D:\ino\tripradar\` to `D:\ino\domains\travel\tripradar\` on 2026-05-02 as part of the ino Phase 4 epilogue. Tripradar still ships as an independent service with its own Aspire AppHost (`src/Aspire/Aspire.csproj`). Build it from inside this folder or via the merged `D:\ino\ino.slnx`.

## Build & Run

The system is orchestrated by .NET Aspire. The AppHost project is `src/Aspire/Aspire.csproj`.

```bash
# Build everything
dotnet build src/Aspire/Aspire.csproj

# Run the full Aspire orchestration
dotnet run --project src/Aspire/Aspire.csproj
```

After making changes: build, run via Aspire, and verify using Aspire MCP tools.

### Rebuilding individual services while Aspire is running

When you modify MiniApp code (or any project referenced by another running service), the running service continues serving **stale static assets**. Use the Aspire `rebuild` command to pick up changes without restarting everything:

```bash
# Via Aspire MCP (preferred)
mcp__aspire__execute_resource_command(resourceName="bot", commandName="rebuild")
```

Available commands on project resources:
- `rebuild` — Stop, rebuild from source (picks up all dependency changes), restart.
- `restart` — Restart process only. Does NOT recompile — use when config changed but code didn't.
- `stop` — Stop resource.

### Dev Login & Testing

The MiniApp dev login (`/auth` page) provides preset test users with different subscription tiers:

| User | Telegram ID | Tier | Capabilities |
|------|------------|------|-------------|
| Free User | 100001 | Basic | Search only, no tracking |
| Essential User | 100002 | Essential | Price tracking, scheduled queries |
| Advanced User | 100003 | Advanced | All features, higher limits |

The dev login calls `POST /api/v1/tokens/dev` with `{ telegramUserId, tier }`. The `tier` parameter is applied on every login — you can switch a user between tiers by logging in again with a different preset or using the custom login form.

This endpoint returns 404 in production — all tier logic is development-only.

See `docs/dev-testing.md` for the full dev testing guide.

## Tests

```bash
# Run all tests
dotnet test

# Run bot tests
dotnet test src/TripRadar.Bot.Tests/TripRadar.Bot.Tests.csproj

# Run a single test by filter
dotnet test --filter "FullyQualifiedName~SomeTestClass.SomeTestMethod"
```

- **TripRadar.Bot.Tests**: xUnit v3 + FluentAssertions + Moq.
- Always run tests with high severity. Ensure Aspire integration tests are green.

## Architecture

### Aspire AppHost (`src/Aspire/`)
Orchestrates all services. Kafka is wired directly in AppHost; server infrastructure is encapsulated in `AddTripRadar()`.

- `AppHost.cs` — Entry point: wires Kafka, Telegram parameters, TripRadar server (returns `TripRadarServices` record), bot project, website, and cloudflared tunnel.
- `Hosting/TripRadar/` — `TripRadarExtensions` + `TripRadarServices` record — Postgres, Redis, Elasticsearch, Flagd, Stripe, API, Jobs, Migrations. All config via Aspire parameters.
- `Hosting/Cloudflared/` — Single quick tunnel (auto-discovered URL from cloudflared log file) for bot webhook and MiniApp.
- Services: `bot`, `api`, `jobs`, `migrations`, `website`.

**Configuration approach:** All secrets and config flow through Aspire parameters (persisted in `~/.aspire/secrets.json`). No `.env` files, no `Environment.GetEnvironmentVariable()` in app code. App-side services use `IOptions<T>` pattern. Connection strings injected via `.WithReference(resource)`.

### TripRadar.ServiceDefaults (`src/TripRadar.ServiceDefaults/`)
Shared project referenced by all services. Provides: OpenTelemetry (logging, tracing, metrics with OTLP export), health checks (`/health/live`, `/health/ready`), service discovery, HTTP client resilience.

### TripRadar.Bot (`src/TripRadar.Bot/`)
Plain ASP.NET service — Telegram notification channel and Mini App entry point. No Orleans, no AI agents.

- `Telegram/` — Webhook endpoint (`/api/telegram/webhook`), auth session sync endpoint (`/api/telegram/auth/session`), `TelegramBotService` (wraps `ITelegramBotClient`), `TelegramWebhookSetup` (registers webhook on startup).
- `Auth/` — Mini App authentication flow: `AuthSessionSyncHandler`, `TripRadarTokenClient` (calls TripRadar API for JWT exchange), `TokenClaimsReader`, `UserSessionStore` (in-memory session cache).
- `Notifications/` — Kafka-driven flight price alerts: `FlightPriceConsumer` (BackgroundService consuming Kafka), `FlightPriceAlertService`, `PriceDeltaCalculator`, `FlightTrackingRegistry` (in-memory tracking state), `MessageFormatter`.
- `TripRadarApi/` — Slim HTTP client for loading active trackings at startup.
- `Configuration/` — `BotOptions`, `KafkaConsumerOptions` via `IOptions<T>`.

### TripRadar.MiniApp (`src/TripRadar.MiniApp/`)
Blazor WebAssembly Mini App served as static files by the bot. Contains `Auth.razor` page for Telegram Mini App authentication with JS interop.

### TripRadar.Server (`src/TripRadar.Server.*/`)
Full server backend — all projects live directly under `src/`:
  - `TripRadar.Server.API` — REST + GraphQL (HotChocolate) + Swagger.
  - `TripRadar.Server.Application` — Application layer.
  - `TripRadar.Server.Domain` — Domain models.
  - `TripRadar.Server.Infrastructure` — Data access, external API providers (SerpApi, Stripe, etc.).
  - `TripRadar.Server.Db` — EF Core migrations (runs as `migrations`).
  - `TripRadar.Server.Jobs.API` — Hangfire background jobs.
  - `TripRadar.Server.Comms.Core` — Communications core.
  - `TripRadar.Server.API.Contracts` — Shared API contracts.
  - `TripRadar.Server.Mocks` — Mock external API providers.
  - `TripRadar.Infrastructure` — Deployment configs.

### TripRadar.WebUI (`src/TripRadar.WebUI/`)
Vite/React frontend.

### Key Patterns
- **Aspire parameters**: Secrets use `builder.AddParameter("name", secret: true)`. Non-secret config uses `builder.AddParameter()` with `publishValueAsDefault`.
- **IOptions pattern**: App-side services bind configuration sections via `IOptions<T>`. No raw `IConfiguration` injection except for dynamic key lookup.
- **Kafka notifications**: `FlightPriceConsumer` (BackgroundService) consumes Kafka events, calculates price deltas, sends Telegram notifications. Manual commit after processing (at-least-once delivery).
- **Central package management**: `Directory.Packages.props` at repo root — all versions managed there.
- **Target framework**: net11.0 across all projects.
- **Solution file**: `D:\ino\ino.slnx` (merged; `TripRadar.slnx` removed).

## Code Style

- No default `/// <summary>` XML doc comments with no meaningful content. Only add small inline comments in exceptional cases.
- Focus on self-explanatory C# variable/method naming instead of comments.
- Always run code review before returning results — check naming quality and unnecessary comments.
- Use latest versions of NuGet packages (versions in `Directory.Packages.props`).

## LSP Servers

Configured in `.lsp.json` — gives Claude Code real-time diagnostics, go-to-definition, and code intelligence.

- **csharp** (`csharp-ls`) — C# language server, uses `D:\ino\ino.slnx` (via `.lsp.json` relative path `../../../ino.slnx`).
- **typescript** (`typescript-language-server`) — TypeScript/JavaScript/JSX/TSX.

### LSP Setup

Fully automatic — no manual steps. `Directory.Build.targets` runs `dotnet tool restore` on first build (installs `csharp-ls`). Aspire runs `npm install` for WebUI (installs `typescript-language-server`). `.lsp.json` uses `dotnet tool run` and `npx` wrappers.

## MCP Servers

Configured in `.mcp.json`:
- **aspire** — Aspire dashboard MCP for monitoring resources, logs, traces, and metrics.
- **context7** — Library documentation search.
- **playwright** — Browser automation for simulating user activity.

### Context7 Usage

**ALWAYS use Context7 to look up package/framework APIs before writing any code or dispatching any subagent.** No exceptions — every API must be verified via Context7 first.

1. Resolve the library ID: `mcp__context7__resolve-library-id` (e.g. "react", "aspire", "@opentelemetry/sdk-trace-web").
2. Query the docs: `mcp__context7__query-docs` with the resolved ID and your topic.
3. Only then write code based on verified API signatures.

This prevents stale training-data assumptions from producing incorrect code.

## Verification Flow (Post-Implementation)

After making any changes, follow this full verification flow before returning results:

### 1. Build & Start
```bash
dotnet build src/Aspire/Aspire.csproj
```
Then use Aspire MCP to start the application and confirm all resources are running:
- `mcp__aspire__list_resources` — verify all services are in a Running state.

### 2. Simulate User Activity (Playwright MCP)
Use the Playwright MCP server to simulate real user interactions:
- Navigate to the website URL (get it from Aspire resource endpoints).
- Perform user actions (e.g. visit pages, attempt login, interact with UI).
- Take screenshots to confirm pages render correctly.

### 3. Verify Telemetry (Aspire MCP)
After simulating activity, use Aspire MCP tools to confirm telemetry is flowing:
- **Traces**: `mcp__aspire__list_traces` — verify spans are being collected from both frontend and backend services.
- **Logs**: `mcp__aspire__list_structured_logs` — check for expected log entries (no errors/warnings unless expected).
- **Trace details**: `mcp__aspire__list_trace_structured_logs` — drill into specific traces to confirm end-to-end propagation.

### 4. Return Results
Only after the full flow (build → start → simulate → verify telemetry) is confirmed working should you return results to the user. If any step fails, debug and fix before proceeding.
