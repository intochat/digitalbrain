# Assistant Project Consolidation

## Goal

Reduce Assistant domain from 5 projects to 2 by merging the grain contracts project into the Silo and deleting empty stubs.

## Current State

| Project | Purpose | Status |
|---------|---------|--------|
| `Assistant.csproj` | Grain interfaces/contracts | Only consumed by Assistant.Silo |
| `Assistant.Silo` | Implementation + web host | Main project |
| `Assistant.MiniApp.Ui` | Blazor WASM frontend | Keep as-is |
| `Assistant.MiniApp.Api` | Stub, no csproj | Delete |
| `Assistant.TelegramMiniApp` | Stub, empty dir | Delete |

## Target State

| Project | Purpose |
|---------|---------|
| `Assistant.Silo` | Contracts + implementation + web host |
| `Assistant.MiniApp.Ui` | Blazor WASM frontend (unchanged) |

## Approach

Merge `Assistant.csproj` contents into `Assistant.Silo` under a `Contracts/` subfolder. Preserve original namespaces.

### File Moves

```
Assistant/IUserAgent.cs              → Assistant.Silo/Contracts/IUserAgent.cs
Assistant/Agents/*.cs                → Assistant.Silo/Contracts/Agents/
Assistant/Telegram/*.cs              → Assistant.Silo/Contracts/Telegram/
Assistant/Voice/*.cs                 → Assistant.Silo/Contracts/Voice/
Assistant/Events/*.cs                → Assistant.Silo/Contracts/Events/
```

### Project Changes

1. Move package references from `Assistant.csproj` to `Assistant.Silo.csproj` (deduplicate)
2. Remove `ProjectReference` to `Assistant.csproj` from `Assistant.Silo.csproj`
3. Update `TripRadar.slnx` to remove deleted projects
4. Delete `src/Assistant/Assistant/` directory
5. Delete `src/Assistant/Assistant.MiniApp.Api/` directory
6. Delete `src/Assistant/Assistant.TelegramMiniApp/` directory

### Validation

- `dotnet build src/Aspire/Aspire.csproj` succeeds
- `dotnet run --project src/Aspire/Aspire.csproj` starts without errors
- No namespace or reference changes needed outside Assistant domain
