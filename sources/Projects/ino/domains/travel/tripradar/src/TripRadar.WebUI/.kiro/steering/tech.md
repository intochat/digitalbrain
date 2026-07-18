# Technology Stack

## Core Technologies

- **React 18.3** - UI framework with TypeScript 5.5
- **Vite 5.4** - Build tool and dev server
- **Tailwind CSS 3.4** - Utility-first CSS framework with dark mode support
- **React Router 6.21** - Client-side routing

## State Management

- **Zustand 4.5** - Client state management
- **TanStack React Query 5.0** - Server state, caching, and data fetching
- **Zod 3.22** - Runtime validation and type safety

## Key Libraries

- **React Hook Form 7.62** with Zod resolvers for form handling
- **Firebase 12.1** - Authentication backend
- **Lucide React** - Icon library
- **date-fns 3.0** - Date manipulation
- **clsx + tailwind-merge** - Conditional class names

## Development Tools

- **ESLint 9.9** with TypeScript strict rules
- **Prettier 3.6** - Code formatting
- **Vitest 1.0** - Testing framework
- **Testing Library** - Component testing
- **Husky + lint-staged** - Pre-commit hooks

## Common Commands

```bash
# Development
npm run dev              # Start dev server (http://localhost:5173)
npm run build            # Production build
npm run preview          # Preview production build

# Code Quality
npm run lint             # Run ESLint
npm run lint:fix         # Fix ESLint issues automatically
npm run format           # Format code with Prettier
npm run format:check     # Check formatting without changes

# Testing
npm run test             # Run tests once
npm run test:watch       # Run tests in watch mode

# API Types
npm run generate-types   # Generate TypeScript types from Swagger API
```

## Path Aliases

Configured in `vite.config.ts` for clean imports:

```typescript
import { Button } from 'shared/ui';
import { useAuth } from 'features/auth';
import { User } from 'entities/user';
```

## API Configuration

- Development proxy: `/api` → `https://api-dev.tripradar.io`
- Type generation from Swagger endpoint
- Generated types in `src/shared/api/generated-types.ts`

## Build Configuration

- Vite with React plugin
- Optimized for production with code splitting
- Vitest for unit tests with jsdom environment
- Dark mode via Tailwind's class strategy
