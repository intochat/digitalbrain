# S1.2-RED — identity boundary characterization report

## What changed
- `src/Tests/DigitalBrain.Tests/IdentityBoundaryProofs.cs` — new characterization pins for P0-3 (singleton `"dev"` owner + unauthenticated HTTP surface), P0-4 (client-trusted `chatName` shared transcript), and actor absence on durable chat command/fact shapes.
- `src/Tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj` — project reference to `DigitalBrain.Kernel` so tests can inventory route path constants and `OwnerCommandRequest`.
- `src/Kernel/DigitalBrain.Kernel/DigitalBrain.Kernel.csproj` — `InternalsVisibleTo DigitalBrain.Tests` (test seam only; no behavior change).
- `src/Modules/UI/DigitalBrain.Modules.UI/DigitalBrain.Modules.UI.csproj` — `InternalsVisibleTo DigitalBrain.Tests` so the durable `Chat.OwnerCommand` shape can be pinned (test seam only).

## Tests
Added (all green characterization pins):

| Pin | Test | Marker |
|-----|------|--------|
| P0-3 singleton owner | `IdentityBoundaryCompositionProofs.HostComposesExactlyOneDigitalBrainSingletonDefaultingToDev` | `// PIN-DEFECT(P0-3)` |
| P0-3 session/chat under owner | `IdentityBoundaryChatProofs.SessionAndChatIdentitiesResolveUnderTheClientOwner` | `// PIN-DEFECT(P0-3)` |
| P0-3 unauthenticated HTTP inventory | `IdentityBoundaryCompositionProofs.KernelHttpSurfaceIsTheUnauthenticatedInventoryWithoutAuthMiddleware` | `// PIN-DEFECT(P0-3)` |
| P0-4 client chatName on command request | `IdentityBoundaryCompositionProofs.OwnerCommandRequestAcceptsClientChatNameAndCarriesNoActor` | `// PIN-DEFECT(P0-4)` |
| P0-4 shared transcript | `IdentityBoundaryChatProofs.TwoClientsSendingToTheSameChatNameShareOneTranscript` | `// PIN-DEFECT(P0-4)` |
| Actor absence | `IdentityBoundaryCompositionProofs.DurableChatCommandsAndJournalFactsCarryNoActorIdentity` | (baseline for seam; no P0 id) |

### HTTP surface inventory (today — unauthenticated)
| Method (via Program.cs maps) | Path constant | Value |
|------------------------------|---------------|-------|
| `MapOwnerCommands` | `OwnerCommandsPath` | `/owner/commands` |
| `MapChatStreams` | `ChatEventsPath` | `/chats/{chatName}/events` |
| `MapSurfaceStreams` | `SurfaceEventsPath` | `/surfaces/{surfaceName}/events` |
| `MapBrainTopology` | `BrainTopologyPath` | `/brain/topology` |
| `MapGraphStreams` | `GraphEventsPath` | `/graph/events` |
| `MapAuthorizationStreams` | `AuthorizationEventsPath` | `/authorizations/events` |
| `MapOAuthCallback` | `McpOAuthCallbackPath` | `/oauth/callback` |

Also mapped in Program (not identity-surface): `MapDefaultEndpoints()` (health), `MapOrleansDashboard("/orleans")`.

**Auth:** Program.cs registers none of `AddAuthentication` / `UseAuthentication` / `AddAuthorization` / `UseAuthorization` / `RequireAuthorization`. Repo-wide grep under `src/` finds zero auth middleware registrations. Documented as P0-3.

### Constant locations pinned
- `DigitalBrainClientHostingExtensions.DefaultOwner = "dev"` (`src/Kernel/Aspire/DigitalBrain.Aspire/DigitalBrainClientHostingExtensions.cs`)
- Config key `DigitalBrain:Owner` → empty/whitespace falls back to `"dev"`; `AddDigitalBrainOwner` registers exactly one `IDigitalBrain` **Singleton**
- Chat routing: `brain.GetGrainProxy<IChat>(chatName)` under the composed owner (HTTP path uses client `OwnerCommandRequest.ChatName`)

### Actor absence shapes
No `Actor` / `Principal` / `UserId` / `User` / `CallerId` on:
- `SendMessage` (`CommandId`, `Text`)
- `UserMessaged` (`CommandId`, `Chat`, `Text`)
- `ChatTurn` (`FromUser`, `Text`, `Buttons`, `Charts`, `Timers`)
- `Responded` (`CommandId`, `Chat`, `Text`, `Buttons`, `Charts`, `Timers`, `Author`) — `Author` is agent/chat instance name, not a principal stamp
- `Chat.OwnerCommand` durable log (`CommandId`, `Text`)

Flipped pins: none (RED only).

## Gate
```
dotnet build DigitalBrain.slnx
Build succeeded.
    0 Error(s)
(Time Elapsed ~3s; node NO_COLOR noise from CodeGraph AppHost target is not a C# warning)

& src/Tests/DigitalBrain.Tests/bin/Debug/net11.0/DigitalBrain.Tests.exe
=== TEST EXECUTION SUMMARY ===
   DigitalBrain.Tests  Total: 80, Errors: 0, Failed: 0, Skipped: 0, Not Run: 0, Time: 36.717s
```

## Conflicts & risks
- Full HTTP hosting (WebApplicationFactory) was **not** required: the brief allows handler/route-registration inventory when endpoint hosting is impractical. GREEN-2 is granted `Microsoft.AspNetCore.Mvc.Testing` for live 401/login flows; RED stays inventory + grain-seam.
- Program.cs auth-absence is pinned by reading the source file from the repo root relative to the test base directory. If the test exe is ever run without the source tree present, that one pin fails loudly (desired for characterization).
- `InternalsVisibleTo` is a production-project edit but not a behavior change (allowed RED test seam per GROK/orchestration).
- Shared-transcript pin uses two `IDigitalBrain` clients for the **same owner** + same `chatName` (mirrors two HTTP clients against the host's single `IDigitalBrain` singleton). It does not spin up Kestrel.

## Out of scope
- No auth implementation, no workspace grains, no actor stamp production types.
- No production behavior changes beyond `InternalsVisibleTo`.
- No new NuGet packages, no git.
- OAuth token principal binding (P0-5) left for S1.3.
- Flutter shell still hard-codes `ownerUserId` presentation strings — not touched.
