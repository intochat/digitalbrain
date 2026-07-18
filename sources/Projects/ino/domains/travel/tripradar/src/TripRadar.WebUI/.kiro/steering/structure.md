# Project Structure

## Architecture: Feature-Sliced Design (FSD)

The project follows FSD architecture with strict layer hierarchy and import rules.

## Layer Structure (bottom-up)

```
src/
├── shared/         # Reusable resources (UI, utils, hooks, API config)
├── entities/       # Business entities (user, payment, pricing)
├── features/       # Business features (auth, trip management)
├── widgets/        # Composite UI blocks (header, footer)
├── pages/          # Application pages
└── app/            # App configuration (providers, router, layout)
```

## Import Rules

Each layer can only import from layers below it:

```
app → pages → widgets → features → entities → shared
```

**Never import:**

- From layers above (e.g., shared cannot import entities)
- From internal files directly (use barrel exports via index.ts)
- Across same-level layers (e.g., one feature cannot import another feature)

## Code Style Conventions

### Always Use Arrow Functions

```typescript
// ✅ Correct
export const MyComponent = () => {
  return <div>Hello</div>;
};

// ❌ Wrong
export default function MyComponent() {
  return <div>Hello</div>;
}
```

### Always Use Named Exports

```typescript
// ✅ Correct
export const Button = () => { ... };

// ❌ Wrong
export default Button;
```

### API Hook Naming

- Queries: `useSomethingQuery` (e.g., `usePricingQuery`)
- Mutations: `useSomethingMutation` (e.g., `useRegisterMutation`)

### File Naming

- Components: `PascalCase.tsx` (Button.tsx, LoginForm.tsx)
- Hooks: `camelCase.ts` with `use` prefix (useAuth.ts, useForm.ts)
- Types: `types.ts` or `camelCase.ts`
- API: `camelCase.ts` with `Api` suffix (authApi.ts, userApi.ts)
- Folders: `kebab-case` (trip-management, user-profile)

## Layer Details

### shared/

Reusable resources without business logic:

- `ui/` - UI components (Button, Input, Modal)
- `lib/` - Utilities, hooks, validation
- `api/` - API configuration, interceptors, types
- `config/` - Constants, routes, environment variables

### entities/

Business entities and their APIs:

- `user/` - User types and API
- `payment/` - Payment types and API
- `pricing/` - Pricing types and API

Each entity has:

- `api/` - API methods and React Query hooks
- `model/` - Types and constants (optional)
- `index.ts` - Barrel exports

### features/

Business features and processes:

- `auth/` - Authentication (login, signup, OAuth)

Each feature has:

- `ui/` - Feature-specific components
- `api/` - Feature API hooks
- `lib/` - Feature utilities (optional)
- `model/` - Feature constants/types (optional)
- `index.ts` - Barrel exports

### widgets/

Composite UI blocks:

- `header/` - Navigation header
- `footer/` - Site footer

Each widget has:

- `ui/` - Widget components
- `index.ts` - Barrel exports

### pages/

Application pages:

- `auth/` - Auth pages (login, signup, email confirmation)
- `marketing/` - Marketing pages (home, pricing)
- `profile/` - User profile pages

Each page has:

- `ui/` - Page components
- `components/` - Page-specific components (optional)
- `index.ts` - Barrel exports

### app/

Application configuration:

- `providers/` - React providers (Query, Theme)
- `router/` - Routing configuration
- `layout/` - App layout components
- `types.ts` - Global types

## Import Examples

```typescript
// ✅ Correct - using barrel exports
import { Button } from 'shared/ui';
import { useAuth } from 'features/auth';
import { User } from 'entities/user';
import { Header } from 'widgets/header';

// ❌ Wrong - direct file imports
import { Button } from 'shared/ui/Button/Button';
import { LoginForm } from 'features/auth/ui/LoginForm';

// ❌ Wrong - violating layer hierarchy
// In shared/ui/Button.tsx:
import { User } from 'entities/user'; // shared cannot import entities
```

## TypeScript Configuration

- Strict mode enabled
- No `any` types allowed
- Path aliases configured for all layers
- Generated API types from Swagger

## Testing Structure

- Test files colocated with source: `Component.test.tsx`
- Setup file: `src/test/setup.ts`
- Use Testing Library for component tests
- Use Vitest for unit tests
