---
inclusion: fileMatch
fileMatchPattern: 'src/TripRadar.WebUI/**'
---

# API & Data Fetching Patterns

## Stack

- HTTP client: custom `apiClient` from `shared/api` (wraps fetch with interceptors)
- Server state: `@tanstack/react-query` v5
- Generated types: `shared/api/generated-types.ts` (from OpenAPI spec)

## API Client Location

API client functions live in the entities layer:

```
entities/{name}/api/
  types.ts          # Request/response interfaces
  {name}Api.ts      # API client object with methods
  index.ts          # Barrel export
```

### API Client Pattern

```ts
// entities/tripVault/api/tripVaultApi.ts
import { apiClient } from 'shared/api';
import type { TripVaultItem, CreateTripVaultRequest } from './types';

const BASE_PATH = '/api/v1/trips';

export const tripVaultApi = {
  getUserTrips: async (): Promise<TripVaultItem[]> => {
    return apiClient.get(BASE_PATH);
  },
  createTripVault: async (request: CreateTripVaultRequest): Promise<TripVaultItem> => {
    return apiClient.post(BASE_PATH, request);
  },
};
```

Rules:
- Export a single `const xxxApi` object with async methods
- Use `apiClient.get/post/put/delete` — never raw `fetch`
- Always type return values with explicit `Promise<T>`
- URL-encode dynamic path segments with `encodeURIComponent`

## React Query Hooks

Each query and mutation gets its own file in the entity's `api/` directory.

### Query Hook Pattern

```ts
// entities/tripVault/api/useTripVaultsQuery.ts
import { useQuery } from '@tanstack/react-query';
import { tripVaultApi } from './tripVaultApi';

interface UseTripVaultsQueryOptions {
  enabled?: boolean;
}

export const useTripVaultsQuery = ({ enabled = true }: UseTripVaultsQueryOptions = {}) => {
  return useQuery({
    queryKey: ['trip-vaults'],
    queryFn: () => tripVaultApi.getUserTrips(),
    enabled,
  });
};
```

### Mutation Hook Pattern

```ts
// entities/tripVault/api/useCreateTripVaultMutation.ts
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { tripVaultApi } from './tripVaultApi';
import type { CreateTripVaultRequest } from './types';

export const useCreateTripVaultMutation = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (request: CreateTripVaultRequest) => tripVaultApi.createTripVault(request),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['trip-vaults'] });
    },
  });
};
```

## Naming Conventions

| Type | Pattern | Example |
|------|---------|---------|
| Query hook | `use{Entity}Query` or `use{Entity}{Action}Query` | `useTripVaultsQuery`, `useTripQueryHistoryQuery` |
| Mutation hook | `use{Action}{Entity}Mutation` | `useCreateTripVaultMutation`, `useDeleteTripVaultMutation` |
| API client | `{entity}Api` | `tripVaultApi`, `paymentApi` |
| Query key | kebab-case string array | `['trip-vaults']`, `['trip-vault-history', tripUniqueId]` |
| Types file | `types.ts` | Always `types.ts` inside `api/` |

## Query Key Rules

- Use kebab-case strings: `['trip-vaults']`, not `['tripVaults']`
- Include dynamic parameters: `['trip-vault-history', tripUniqueId]`
- Paginated queries include page params: `['trip-vault-history', tripUniqueId, pageNumber, pageSize]`
- Invalidate by prefix: `queryClient.invalidateQueries({ queryKey: ['trip-vaults'] })`

## Hook Options

- Always accept an `enabled` option for conditional fetching
- Default `enabled` to `true` unless the hook requires a parameter
- Use interface for options: `interface UseXxxQueryOptions { enabled?: boolean; }`

## Error Handling

- API errors are caught in feature components, not in hooks
- Use `try/catch` around `mutateAsync` calls
- Show errors via `useToast()` from `app/providers/ToastProvider`
- Pattern: `showError(t('Title'), t('Description'))`
- Log errors to console: `console.error('Context:', error)`

## Cache Invalidation

- Mutations invalidate related queries in `onSuccess`
- Use `queryClient.invalidateQueries({ queryKey: [...] })` — not `refetchQueries`
- Invalidate by key prefix to catch all related queries

## Types

- Request/response types live in `entities/{name}/api/types.ts`
- For types generated from OpenAPI, re-export from `shared/api/index.ts`
- Prefer explicit interfaces over `components['schemas']['...']` in feature code
