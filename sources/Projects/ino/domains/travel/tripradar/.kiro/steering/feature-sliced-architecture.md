---
inclusion: fileMatch
fileMatchPattern: 'src/TripRadar.WebUI/**'
---

# Feature-Sliced Design Architecture

The frontend (`src/TripRadar.WebUI/src/`) follows Feature-Sliced Design (FSD) with strict layer boundaries.

## Layers (top to bottom)

| Layer | Path | Purpose |
|-------|------|---------|
| `app` | `src/app/` | App shell: providers, router, layout, i18n init, global types |
| `pages` | `src/pages/` | Route-level components. Thin shells that compose features |
| `widgets` | `src/widgets/` | Composite UI blocks used across pages (header, footer) |
| `features` | `src/features/` | User-facing business logic: forms, sections, interactive flows |
| `entities` | `src/entities/` | Domain models: API clients, hooks, types, pure logic |
| `shared` | `src/shared/` | Reusable foundation: UI components, api client, config, i18n, lib utils |

## Import Rules

A layer can only import from layers below it. Never import upward or sideways within the same layer.

```
app → pages → widgets → features → entities → shared
```

| From \ To | shared | entities | features | widgets | pages | app |
|-----------|--------|----------|----------|---------|-------|-----|
| shared | — | ❌ | ❌ | ❌ | ❌ | ❌ |
| entities | ✅ | own slice | ❌ | ❌ | ❌ | ❌ |
| features | ✅ | ✅ | own slice | ❌ | ❌ | ❌ |
| widgets | ✅ | ✅ | ✅ | own slice | ❌ | ❌ |
| pages | ✅ | ✅ | ✅ | ✅ | own slice | ❌ |
| app | ✅ | ✅ | ✅ | ✅ | ✅ | — |

Cross-slice imports within the same layer are forbidden. For example, `features/tripVault` must not import from `features/scheduledRequests`.

Exception: `app/providers` can be imported from any layer via `useFrontendLanguage`, `useToast`, etc. — these are global context hooks.

## Slice Internal Structure

Each slice (entity, feature, page) follows this pattern:

```
entities/tripVault/
  api/
    types.ts              # TypeScript interfaces for API responses/requests
    tripVaultApi.ts       # API client functions (uses shared/api)
    useTripVaultsQuery.ts # React Query hook
    useCreateTripVaultMutation.ts
    index.ts              # Barrel export
  index.ts                # Re-exports from api/
```

```
features/tripVault/
  ui/
    TripVaultSection.tsx  # Main feature component
    TripVaultCard.tsx     # Sub-components
    TripVaultForm.tsx
    tripVaultUtils.ts     # Feature-specific helpers
    index.ts              # Barrel export
  index.ts                # Re-exports from ui/
```

```
pages/profile/
  ui/
    ProfileTrips.tsx      # Thin page shell
    ProfileLayout.tsx     # Shared layout for profile pages
    tripHistory/          # Sub-page components
      HistoryList.tsx
      index.ts
  index.ts                # Barrel export
```

## Key Rules

### Entities layer
- Contains API clients, React Query hooks, TypeScript types, and pure domain logic
- No UI components — entities never render anything
- API client functions live in `api/xxxApi.ts`, use `apiClient` from `shared/api`
- Types live in `api/types.ts`
- Each query/mutation gets its own file: `useXxxQuery.ts`, `useXxxMutation.ts`

### Features layer
- Contains interactive UI: forms, sections, cards, lists
- Composes entities (imports hooks and types from entities)
- Uses shared UI components (Button, Input, Dropdown, etc.)
- Business logic (validation, formatting) lives in feature-local `xxxUtils.ts`
- Feature components receive data via hooks, not props from pages

### Pages layer
- Thin shells — minimal logic, mostly composition
- Pattern: `<ProfileLayout><div className="px-4 ..."><FeatureSection /></div></ProfileLayout>`
- Pages pass only essential props (isPaidUser, noTraceEnabled) to features
- Loading/error/empty states are handled inside feature sections, not in pages

### Shared layer
- `shared/ui/` — reusable UI components (Button, Input, DatePicker, Dropdown, etc.)
- `shared/api/` — base API client, interceptors, generated types
- `shared/config/` — routes, environment variables
- `shared/i18n/` — translations, language utilities
- `shared/lib/` — utility functions (cn, trackEvent, hooks)
- Never contains business logic or domain-specific code

### Barrel Exports
- Every slice has an `index.ts` that re-exports its public API
- Import from the slice root: `import { useTripVaultsQuery } from 'entities/tripVault'`
- Never import from internal paths: ~~`import { useTripVaultsQuery } from 'entities/tripVault/api/useTripVaultsQuery'`~~
- Exception: feature-internal sub-components can import siblings directly

## Adding a New Slice

### New entity
1. Create `entities/{name}/api/types.ts` with interfaces
2. Create `entities/{name}/api/{name}Api.ts` with API client
3. Create query/mutation hooks in separate files
4. Create `entities/{name}/api/index.ts` barrel
5. Create `entities/{name}/index.ts` re-exporting from `api/`

### New feature
1. Create `features/{name}/ui/{Name}Section.tsx` as the main component
2. Extract sub-components into separate files in `ui/`
3. Create `features/{name}/ui/index.ts` barrel
4. Create `features/{name}/index.ts` re-exporting from `ui/`

### New page
1. Create `pages/{name}/ui/{PageName}.tsx` as a thin shell
2. Import feature sections and compose them
3. Create `pages/{name}/index.ts` barrel
4. Add route in `app/router/routes.tsx`
