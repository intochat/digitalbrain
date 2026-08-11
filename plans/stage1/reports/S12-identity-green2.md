# S1.2-GREEN-2 — ASP.NET Identity at the Host boundary report

## What changed

### Host auth surface
- `src/Kernel/DigitalBrain.Kernel/Program.cs` — `AddDigitalBrainAuth` / `UseDigitalBrainAuth` / `MapAuth`; partial `Program` for test host discovery.
- `src/Kernel/DigitalBrain.Kernel/Auth/*` — cookie auth composition, table+memory account directories, Identity user store, claims factory, loopback middleware, `/auth/*` maps, workspace membership gateway, `PrincipalChat` naming, `HttpActor`.
- `src/Kernel/DigitalBrain.Kernel/HttpSurfacePaths.cs` — `/auth/bootstrap|login|logout|me|users` path constants.
- `src/Kernel/DigitalBrain.Kernel/MapOwnerCommands.cs` — requires actor; routes chat/button by `PrincipalChat.InstanceName`; stamps `SendMessage.Actor`.
- `src/Kernel/DigitalBrain.Kernel/MapChatStreams.cs` — requires actor; watches principal-scoped chat instance.
- `src/Kernel/DigitalBrain.Kernel/MapOAuthCallback.cs` — `AllowAnonymous` (OAuth remains public).
- `src/Kernel/Aspire/DigitalBrain.ServiceDefaults/ServiceDefaultsExtensions.cs` — health `/health` + `/alive` `AllowAnonymous`.

### Durable actor stamp + chat contracts
- `SendMessage` / `UserMessaged` — optional `Actor` field (host always supplies).
- `Chat.OwnerCommand` + `RememberOwnerTurnAsync` — persists actor on durable command log + `UserMessaged`.

### Packages / tests
- `Directory.Packages.props` + test csproj — `Microsoft.AspNetCore.Mvc.Testing` 11.0.0-preview.6.26359.118 (granted).
- `IdentityBoundaryProofs.cs` — flipped P0-3/P0-4 (markers removed); actor stamp asserted on user path.
- `AuthEndpointProofs.cs` — TestServer endpoint proofs (401, bootstrap-once, login, loopback Dev-only, two-user principal isolation).

### Design note (auth flow, store schema, actor flow)

**Auth flow.** `AddIdentityCore<DigitalBrainUser>` + custom `IUserStore`/`IUserPasswordStore` (no EF). Cookie scheme `Cookies` (`DigitalBrain.Auth`) with 401/403 instead of redirects. Fallback authorization policy requires authenticated user; anonymous: `/auth/*`, `/oauth/callback`, `/health`, `/alive`.  
`POST /auth/bootstrap` (empty user table only) creates account + `IWorkspace.AddMember` as self-Owner.  
`POST /auth/login` verifies `PasswordHasher` + sets cookie.  
`POST /auth/users` (Owner/Admin via `ReadMembership`) creates account + `AddMember`.  
`DigitalBrain:Auth:AllowLoopbackDev` defaults on in Development only; loopback requests without cookie run as bootstrap Owner.

**Store schema (Azure Table `identityusers`, clustering storage account).**  
Rows: `PartitionKey=name`/`RowKey=normalizedUserName` and `PartitionKey=id`/`RowKey=userId` with `UserId`, `UserName`, `NormalizedUserName`, `PasswordHash`, `SecurityStamp`, `PrincipalId` (n-format GUID), `IsBootstrapOwner`, `CreatedAt`. Marker row `meta/bootstrap-owner` → bootstrap `UserId`. Password material never enters grains.

**Actor flow.** Cookie claims include `db.principal-id` + name → `HttpActor.Require` → `ActorContext`. Chat instance = `{principal:N}.{conversation}` (no `/` in neuron names). Client `chatName` only selects among the caller's conversations. `SendMessage`/`OwnerCommand`/`UserMessaged` carry `Actor`. Workspace mutations still use GREEN-1 verbs only (`IWorkspaceMembershipGateway` adapts, does not reimplement membership).

## Tests

| Name | Role |
|------|------|
| `KernelHttpSurfaceRequiresAuthenticationExceptAuthOauthAndHealth` | flipped P0-3 composition |
| `SessionAndChatIdentitiesResolveUnderTheClientOwnerWithPrincipalScopedChatNames` | flipped P0-3 chat identity |
| `OwnerCommandRequestAcceptsClientChatNameButNeverClientActor` | flipped P0-4 request shape + PrincipalChat |
| `TwoPrincipalsSendingToTheSameConversationNameGetIsolatedTranscripts` | flipped P0-4 isolation |
| `DurableChatCommandsAndJournalFactsCarryActorStampOnUserPath` | actor on durable user path |
| `AnonymousCallerGets401OnProtectedSurface` | 401 anonymous |
| `BootstrapThenLoginRoundTripReturnsMeAndAuthorizesProtectedSurface` | bootstrap → login → 200 |
| `BootstrapIsRefusedOnceAnOwnerExists` | bootstrap-once |
| `LoopbackDevBypassIsActiveOnlyInDevelopmentWhenEnabled` | loopback Dev-only |
| `TwoUsersReceiveDistinctPrincipalScopedChatInstanceNames` | two users → distinct chat keys |

PIN-DEFECT(P0-3) / PIN-DEFECT(P0-4) markers: **removed**.

## Gate
```
dotnet build DigitalBrain.slnx
Build succeeded.
    0 Error(s)
(Time Elapsed ~3s; node NO_COLOR noise from CodeGraph AppHost target is not a C# warning)

& src/Tests/DigitalBrain.Tests/bin/Debug/net11.0/DigitalBrain.Tests.exe
=== TEST EXECUTION SUMMARY ===
   DigitalBrain.Tests  Total: 96, Errors: 0, Failed: 0, Skipped: 0, Not Run: 0, Time: 40.374s
```

## Conflicts & risks
- Full Kernel `WebApplicationFactory` against Orleans+Azurite was not used; auth endpoint proofs run a TestServer host with `MemoryAccountDirectory` + recording workspace gateway (same verbs). Recorded because silo/table deps make WAF of the production Program impractical without aspire (banned).
- TestServer clients do not reliably re-attach Set-Cookie via CookieContainer for this host shape; tests forward the auth cookie header explicitly after login/bootstrap (browsers attach cookies natively).
- Installation still has one `IDigitalBrain` singleton owner (`dev` default) — workspace is installation-scoped; per-principal isolation is chat/transcript, not multi-brain factory (out of brief).
- Neuron names cannot contain `/`; principal chat name is `{principal:N}.{conversation}` (equivalent intent to brief example `chat:{owner}/{principalId}/main`).

## Out of scope
- Workspace grain redesign (GREEN-1).
- OAuth principal binding / P0-5 (S1.3).
- MCP, Flutter, docker/CI, git.
- No packages beyond the granted Mvc.Testing pin.
