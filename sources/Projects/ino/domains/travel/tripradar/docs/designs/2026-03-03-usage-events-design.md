# Feature: Usage Events Analytics Page

## Summary

Build a dedicated usage analytics feature that shows token consumption over time and a paged list of usage events, independent from trip query history.

The existing `TripQueryHistory` model is intentionally not reused. It stores request history details, while this feature needs a billing/consumption-oriented model: when the event happened, what service type it was, how many tokens were consumed, and how much quota remains.

The implementation uses a new `UsageController` and a new `UsageEvents` persistence model with a lookup table for source type (`Api`, `Scheduled`, `Telegram`). The page will follow existing Profile UI patterns and expose the same behavior as shown in the provided screenshot:

- Daily usage chart for a selected period (`1d`, `7d`, `30d`, custom range support via API params).
- Filterable usage events table/list.
- Empty state (`No events found`) when data is absent.

Privacy requirement is strict:

- Paid user + no-trace enabled request -> usage event must not be stored and must not be shown.
- Non-paid users or paid users with no-trace disabled -> usage events are stored and shown.

Given selected constraints (`small scope`, `urgent`, `fail-fast`, `no tests`), the design prioritizes a narrow MVP that can be delivered quickly without introducing optional analytics complexity.

## Requirements

### Product requirements

- Goal: `New Functionality`.
- Primary user: `End Users`.
- Scope: `Small (1-2 days)`.
- Timeline: `Urgent`.
- UI approach: `Existing Pattern`.

### Technical requirements

- Layers touched: `Data Model`, `Business Logic`, `API`, `UI`.
- Quality priorities: `High Performance`, `Strong Security`, `High Reliability`, `Easy Maintenance`.
- Error handling strategy: `Fail Fast`.
- Testing strategy: `No Tests` (manual validation only).

### Integration/dependency requirements

- Integrations: `None` (no new third-party dependency required).
- Dependencies: `Other Features` (depends on existing token and privacy pipeline).
- Backward compatibility: `Not Applicable` (new feature surface).
- Documentation: `None` requested, but this design doc is added for implementation handoff.

## Architecture

### Architectural choice

Selected option: **Option B** (separate usage model + pipeline), because usage analytics semantics differ from query history semantics.

### High-level flow

1. User executes a successful token-consuming request.
2. Existing token pipeline calculates consumption based on service token cost.
3. Usage write component receives normalized event input.
4. Privacy gate checks no-trace mode in request payload.
5. If recording is allowed, event is stored in `UsageEvents`.
6. `UsageController` reads and aggregates usage events for chart + list + summary.
7. Profile Usage page renders data with filters and empty states.

### Why separate from `TripQueryHistory`

- `TripQueryHistory` answers: what was requested and what payload/summary existed.
- `UsageEvents` answers: how many tokens were consumed, when, by what service/source.
- Independent model keeps both features simple and avoids overloading one table with mixed concerns.

## Data Model

### New table: `UsageEventSources`

Lookup table for source classification.

- `UsageEventSourceId` (`int`, PK)
- `Name` (`varchar(50)`, unique)
- `Description` (`varchar(200)`, nullable)
- `IsActive` (`boolean`, default `true`)

Seed values:

- `1 = Api`
- `2 = Scheduled`
- `3 = Telegram`

### New table: `UsageEvents`

- `UsageEventId` (`bigint`, PK, identity)
- `UniqueId` (`uuid`, unique, external identifier)
- `UserId` (`bigint`, FK -> `Users`)
- `ServiceTypeId` (`int`, FK -> `ServiceTypes`)
- `TripVaultId` (`bigint`, nullable FK -> `TripVaults`)
- `UsageEventSourceId` (`int`, FK -> `UsageEventSources`)
- `TokensConsumed` (`decimal(10,2)`)
- `OccurredAt` (`timestamptz`)
- `CreatedAt` (`timestamptz`)

Indexes:

- `UX_UsageEvents_UniqueId`
- `IX_UsageEvents_UserId_OccurredAt`
- `IX_UsageEvents_UserId_ServiceTypeId_OccurredAt`
- `IX_UsageEvents_UserId_TripVaultId_OccurredAt`
- `IX_UsageEvents_UserId_UsageEventSourceId_OccurredAt`

### Data retention and migration

- MVP does not include retention policy automation.
- MVP does not include historical backfill from `TripQueryHistory`.
- EF migration adds only new tables/indexes and lookup seeds.

## API Design

### New controller

`UsageController` under:

- Route prefix: `api/v{version:apiVersion}/usage`

### Endpoint

`GET /api/v1.0/usage/events`

Query params:

- `from` (`date`, optional)
- `to` (`date`, optional)
- `groupBy` (`day|week`, default `day`)
- `serviceType` (`string`, optional)
- `tripVaultUniqueId` (`guid`, optional)
- `source` (`api|scheduled|telegram`, optional)
- `page` (`int`, default `1`)
- `pageSize` (`int`, default `20`, max `100`)

Response:

- `summary`:
  - `currentUsage`
  - `monthlyLimit`
  - `remainingTokens`
- `timeline[]`:
  - `date`
  - `tokensConsumed`
  - `eventsCount`
- `events[]`:
  - `uniqueId`
  - `occurredAt`
  - `serviceType`
  - `source`
  - `tokensConsumed`
  - `tripVault` (`uniqueId`, `name`, optional)
- `pagination`:
  - `page`
  - `pageSize`
  - `totalCount`
  - `totalPages`

Validation:

- `from <= to`
- Max date range: 365 days
- `page >= 1`
- `1 <= pageSize <= 100`
- Invalid enum/filter values -> `400`

## Component Design

### Backend components

- `UsageEvent` and `UsageEventSource` domain entities.
- `IUsageEventRepository` + `UsageEventRepository` for write/read and aggregations.
- `IUsageEventWriter` + `UsageEventWriter` for normalized write API.
- `IUsageSourceResolver` + `UsageSourceResolver` for source lookup.
- `PostSuccessUsageEventBehavior<TRequest, TResponse>`:
  - Applies to `ITokenConsumingRequest`.
  - Executes only on successful request responses.
  - Resolves token cost and request metadata.
  - Applies privacy gate before persistence.
- `GetUsageEventsQuery` + handler for read model aggregation.

### Frontend components

- `entities/usage/api`:
  - `types.ts`
  - `usageApi.ts`
  - `useUsageEventsQuery.ts`
- `pages/profile/ui/ProfileUsage.tsx`:
  - Date preset controls (`1d`, `7d`, `30d`)
  - Filter controls (`service`, `source`, `vault`)
  - Timeline chart block
  - Events list block
  - Empty/loading/error states
- Profile navigation and routes:
  - Add `/profile/usage` route
  - Add `Usage` entry in `ProfileLayout` navigation

### Chart rendering

No new chart library is required in MVP. Use existing UI stack and a simple SVG/CSS-based chart if needed to avoid dependency churn in urgent scope.

## Error Handling

### Fail-fast policy

- Usage write failure on successful token-consuming request is treated as request failure for this feature scope.
- Invalid usage query params fail immediately with `400`.
- Unauthorized access fails with `401`.
- Unexpected server/database error returns `500`.

### Empty states are not errors

- No events in selected range -> `200` with:
  - `timeline = []`
  - `events = []`
  - valid `summary` and `pagination`

### Privacy-specific behavior

- If request payload indicates no-trace mode (`ZeroTrace`/`NoTraceMode`) and user is eligible for paid privacy behavior, event is not persisted.
- This is enforced server-side only.

### Logging

Structured logs:

- `usage_event_write_failed`
- `usage_events_query_failed`
- `usage_events_query_validation_failed`

Do not log raw request payloads containing sensitive data.

## Security

- Input validation for all query parameters.
- Reuse existing API rate limiting configuration.
- Enforce current-user scope only (no username parameter in usage endpoint).
- Add audit log entries for usage endpoint access with non-sensitive metadata.
- Persist only required usage metadata (no raw query payload).

## Testing Strategy

Selected strategy: **No automated tests** for this urgent small-scope delivery.

Manual validation checklist:

1. Standard user performs token-consuming requests -> usage events appear.
2. Paid user with no-trace enabled performs requests -> usage events do not appear.
3. Paid user with no-trace disabled performs requests -> usage events appear.
4. `service/source/vault/date` filters return expected subsets.
5. Empty period returns `200` with empty arrays and proper empty UI state.
6. Summary values align with existing monthly token counters and tier limits.

## Out Of Scope (MVP)

- CSV export endpoint and UI export action.
- Historical backfill from trip history.
- Long-term data retention jobs.
- New external telemetry or BI integrations.
- Automated test suite additions.

## Implementation Tasks

- [ ] **Add usage data tables and schema wiring** `priority:1` `phase:model` `time:45min`
  - files: `src/TripRadar/TripRadar.Server/TripRadar.Server.Db/Constants/DbConstants.cs`, `src/TripRadar/TripRadar.Server/TripRadar.Server.Infrastructure/Database/TripRadarDbContext.cs`, `src/TripRadar/TripRadar.Server/TripRadar.Server.Infrastructure/Database/EntityFrameworkConfigurations/UsageEventsConfiguration.cs`, `src/TripRadar/TripRadar.Server/TripRadar.Server.Infrastructure/Database/EntityFrameworkConfigurations/UsageEventSourcesConfiguration.cs`
  - [ ] Add `UsageEvents` and `UsageEventSources` table constants.
  - [ ] Register new `DbSet`s in `TripRadarDbContext`.
  - [ ] Add EF configurations with required keys, FKs, and indexes.

- [ ] **Create usage domain entities** `priority:1` `phase:model` `deps:Add usage data tables and schema wiring` `time:20min`
  - files: `src/TripRadar/TripRadar.Server/TripRadar.Server.Domain/Entities/UsageEvent.cs`, `src/TripRadar/TripRadar.Server/TripRadar.Server.Domain/Entities/UsageEventSource.cs`
  - [ ] Define immutable core fields and constructors.
  - [ ] Ensure `UniqueId` and timestamps are set consistently.

- [ ] **Add lookup seed for usage sources** `priority:1` `phase:model` `deps:Create usage domain entities` `time:20min`
  - files: `src/TripRadar/TripRadar.Server/TripRadar.Server.Db/Seeding/*`
  - [ ] Add seed values `Api`, `Scheduled`, `Telegram`.
  - [ ] Keep seed id values stable.

- [ ] **Add usage repository contracts and implementation** `priority:1` `phase:data` `deps:Add usage data tables and schema wiring` `time:45min`
  - files: `src/TripRadar/TripRadar.Server/TripRadar.Server.Application/Contracts/Repositories/IUsageEventRepository.cs`, `src/TripRadar/TripRadar.Server/TripRadar.Server.Infrastructure/Repositories/UsageEventRepository.cs`, `src/TripRadar/TripRadar.Server/TripRadar.Server.Application/Contracts/Repositories/IUnitOfWork.cs`, `src/TripRadar/TripRadar.Server/TripRadar.Server.Infrastructure/Repositories/UnitOfWork.cs`
  - [ ] Implement write method and read aggregation queries.
  - [ ] Add repository into unit of work surfaces.

- [ ] **Implement usage write service and source resolver** `priority:1` `phase:logic` `deps:Add usage repository contracts and implementation` `time:35min`
  - files: `src/TripRadar/TripRadar.Server/TripRadar.Server.Application/Contracts/Services/IUsageEventWriter.cs`, `src/TripRadar/TripRadar.Server/TripRadar.Server.Application/Contracts/Services/IUsageSourceResolver.cs`, `src/TripRadar/TripRadar.Server/TripRadar.Server.Infrastructure/Services/UsageEventWriter.cs`, `src/TripRadar/TripRadar.Server/TripRadar.Server.Infrastructure/Services/UsageSourceResolver.cs`
  - [ ] Normalize write input and map to source lookup ids.
  - [ ] Resolve optional `TripVaultId`.

- [ ] **Add post-success usage behavior with privacy gate** `priority:1` `phase:logic` `deps:Implement usage write service and source resolver` `time:50min`
  - files: `src/TripRadar/TripRadar.Server/TripRadar.Server.Application/Behaviors/PostSuccessUsageEventBehavior.cs`, `src/TripRadar/TripRadar.Server/TripRadar.Server.Application/Extensions/ServiceCollectionExtensions.cs`
  - [ ] Hook behavior for `ITokenConsumingRequest`.
  - [ ] Apply `ZeroTrace/NoTraceMode` suppression rule.
  - [ ] Resolve token cost and write usage event.
  - [ ] Keep fail-fast behavior consistent with design.

- [ ] **Add usage query use case** `priority:1` `phase:api` `deps:Add usage repository contracts and implementation` `time:45min`
  - files: `src/TripRadar/TripRadar.Server/TripRadar.Server.Application/UseCases/Usage/Queries/GetUsageEvents/*`
  - [ ] Add request DTO/query model with validator.
  - [ ] Implement handler to build summary + timeline + paged events.

- [ ] **Add API contracts and AutoMapper profile for usage** `priority:1` `phase:api` `deps:Add usage query use case` `time:35min`
  - files: `src/TripRadar/TripRadar.Server/TripRadar.Server.API.Contracts/Responses/Get/GetUsageEventsResponse.cs`, `src/TripRadar/TripRadar.Server/TripRadar.Server.API.Contracts/Models/UsageEventItemResponse.cs`, `src/TripRadar/TripRadar.Server/TripRadar.Server.API/Mappings/UsageProfile.cs`
  - [ ] Define response contracts.
  - [ ] Map application DTOs to API responses.

- [ ] **Create UsageController endpoint** `priority:1` `phase:api` `deps:Add API contracts and AutoMapper profile for usage` `time:30min`
  - files: `src/TripRadar/TripRadar.Server/TripRadar.Server.API/Controllers/UsageController.cs`
  - [ ] Implement `GET /api/v{version}/usage/events`.
  - [ ] Enforce auth and parameter validation.

- [ ] **Register DI and repository/service bindings** `priority:1` `phase:infrastructure` `deps:Create UsageController endpoint` `time:20min`
  - files: `src/TripRadar/TripRadar.Server/TripRadar.Server.Infrastructure/Extensions/ServiceCollectionExtensions.cs`, `src/TripRadar/TripRadar.Server/TripRadar.Server.Infrastructure/Extensions/ServiceCollectionExtensions.Repositories.cs`
  - [ ] Register usage repository and services.
  - [ ] Ensure behavior dependencies resolve.

- [ ] **Add profile usage frontend data layer** `priority:2` `phase:ui` `deps:Create UsageController endpoint` `time:40min`
  - files: `src/TripRadar/TripRadar.WebUI/src/entities/usage/api/types.ts`, `src/TripRadar/TripRadar.WebUI/src/entities/usage/api/usageApi.ts`, `src/TripRadar/TripRadar.WebUI/src/entities/usage/api/useUsageEventsQuery.ts`, `src/TripRadar/TripRadar.WebUI/src/entities/usage/api/index.ts`
  - [ ] Add typed request/response models.
  - [ ] Add React Query hook with stable query key composition.

- [ ] **Implement profile usage page and navigation** `priority:2` `phase:ui` `deps:Add profile usage frontend data layer` `time:60min`
  - files: `src/TripRadar/TripRadar.WebUI/src/pages/profile/ui/ProfileUsage.tsx`, `src/TripRadar/TripRadar.WebUI/src/pages/profile/index.ts`, `src/TripRadar/TripRadar.WebUI/src/app/router/routes.tsx`, `src/TripRadar/TripRadar.WebUI/src/pages/profile/ui/ProfileLayout.tsx`, `src/TripRadar/TripRadar.WebUI/src/shared/config/routes.ts`
  - [ ] Add `/profile/usage` route.
  - [ ] Add Usage navigation item in profile sidebar/mobile.
  - [ ] Render summary, timeline chart block, event list, filters, and empty state.

- [ ] **Manual verification and integration check** `priority:1` `phase:verification` `deps:Implement profile usage page and navigation` `time:45min`
  - files: N/A
  - [ ] Build AppHost: `dotnet build src/Aspire/Aspire.csproj`.
  - [ ] Run orchestration: `dotnet run --project src/Aspire/Aspire.csproj`.
  - [ ] Validate scenarios from manual checklist.
  - [ ] Verify API and resource health via Aspire dashboard/MCP.
