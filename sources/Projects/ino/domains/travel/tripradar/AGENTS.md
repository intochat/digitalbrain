# Repository Guidelines

## Project Structure & Module Organization
`src/` contains the full application stack. `src/Aspire` orchestrates local development. Backend services live under `src/TripRadar.Server.*`, shared defaults under `src/TripRadar.ServiceDefaults`, the Telegram bot in `src/TripRadar.Bot`, deployment/bootstrap code in `src/TripRadar.DeploymentKit` and `src/TripRadar.Infrastructure`, and the frontend in `src/TripRadar.WebUI`.

The frontend follows a feature-sliced layout: `src/TripRadar.WebUI/src/{app,entities,features,pages,shared,widgets}`. Keep reusable UI and API helpers in `shared`, page-level composition in `pages`, and business features in `features`. Tests live in `src/TripRadar.Server.Tests`, `src/TripRadar.Bot.Tests`, and `src/TripRadar.WebUI/src/test`.

## Build, Test, and Development Commands
Run the full stack locally with `dotnet run --project src/Aspire/Aspire.csproj`. This starts the .NET services, frontend, containers, migrations, and Aspire dashboard.

Use `dotnet test` for all .NET tests, or target a suite directly, for example `dotnet test src/TripRadar.Server.Tests`.

For the frontend, work from `src/TripRadar.WebUI`:
- `npm install`
- `npm run dev` to start Vite
- `npm run build` for production assets
- `npm run test` or `npm run test:watch` for Vitest
- `npm run lint`, `npm run lint:fix`, `npm run format`, `npm run format:check`

## Coding Style & Naming Conventions
C# projects target `net11.0` with nullable reference types and implicit usings enabled. Follow `.editorconfig`: 4-space indentation, PascalCase for types and members, interfaces prefixed with `I`, braces required, and block-scoped namespaces.

Frontend code uses TypeScript, ESLint, and Prettier. Prettier enforces 2-space indentation, semicolons, single quotes, and 120-character lines. ESLint forbids default exports in app code, unused variables, `any`, import cycles, and requires ordered imports.

## Testing Guidelines
.NET tests use xUnit v3, FluentAssertions, Moq, and `coverlet.collector`. Name test projects with `.Tests` and keep test classes close to the unit under test. Frontend tests use Vitest and Testing Library; place UI tests under `src/test` or beside the component when that improves locality.

## Commit & Pull Request Guidelines
Recent history uses Conventional Commit prefixes such as `feat:` and `fix:`. Keep commit subjects short, imperative, and scoped to one change.

Pull requests should explain the behavioral change, list affected services or UI areas, link the issue when available, and include screenshots for visible frontend changes. Before opening a PR, run `dotnet test` and the relevant `npm` lint/test commands.

## Security & Configuration Tips
Do not commit secrets. Local startup injects some internal secrets automatically, but external credentials such as Telegram and Stripe keys are provided through the Aspire dashboard or local environment settings.
