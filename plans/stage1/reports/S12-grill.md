# S1.2-GRILL — adversarial review of the identity seam

## What was reviewed

Whole S1.2 identity seam (judge only; no production edits):

| Slice | Location |
|-------|----------|
| RED (pins) | commit `e77d6ac5` |
| GREEN-1 (workspace grains) | commit `92ef469e` |
| GREEN-2 (Host auth) | uncommitted working tree + `Auth/` |
| Combined | `git diff HEAD~2` + `git diff` |
| Worker reports | `plans/stage1/reports/S12-identity-{red,green1,green2}.md` |

Authority: `plans/RATIFIED-PRODUCT-DEFINITION.md` §1.13, `plans/GROK-ORCHESTRATION-STAGE1.md` S1.2, briefs `S12-identity-*.md` / `S12-grill.md`.

## Attack results

### 1. Ratified conformance (§1.13)

| Check | Result |
|-------|--------|
| Credentials/sessions only at Host | **PASS.** Grep of Core + Workspace abstractions/impl: no password/hash/secret material. Password fields live only under `DigitalBrain.Kernel/Auth/*`. |
| Membership/roles/audit in grains | **PASS.** `WorkspaceNeuron` + `db.workspace.*` verbs; mutation replies stamp `Actor` + `At`; commands carry `ActorContext` into the inbound journal. |
| Invitations | **MAJOR** (deferred). Ratified grains own invitations; GREEN-1/2 never modeled them. Acceptable residual for this slice if membership alone ships MVP, but §1.13 is incomplete. |
| Roles Owner/Admin/Builder/Viewer | **PASS.** `WorkspaceRole` enum + host `/auth/users` parse set match. |
| Last-Owner invariant | **PASS.** Demote/remove refused when `OwnerCount == 1` via `NeuronAuthorizationException` (`WorkspaceNeuron.cs` ~86–92, ~125–129). |
| HTTPS beyond localhost | **MAJOR.** Stance is not enforced in code: `CookieSecurePolicy.SameAsRequest` only (`AuthHostingExtensions.cs:49`); no HTTPS-redirect/HSTS. TLS must be assumed at the edge. |
| Loopback bypass only in Development | **PASS.** `AuthOptions.ResolveAllowLoopbackDev` hard-returns `false` outside Development (`AuthOptions.cs:16–18`); middleware also requires `environment.IsDevelopment()` (`LoopbackDevAuthMiddleware.cs:16`). |

### 2. Security of the auth implementation

| Check | Result |
|-------|--------|
| Framework `PasswordHasher` | **PASS.** `AddIdentityCore` + `UserManager.CreateAsync(user, password)` + login `IPasswordHasher.VerifyHashedPassword` — no homemade digest. |
| Cookie flags | **PASS** with notes: `HttpOnly=true`, `SameSite=Lax`, `SecurePolicy=SameAsRequest`, name `DigitalBrain.Auth`, 401/403 instead of redirects (`AuthHostingExtensions.cs:44–62`). |
| Login non-enumeration | **PASS.** Unknown user and wrong password both `Results.Unauthorized()` (`MapAuth.cs:99–107`). |
| Bootstrap-once | **PASS.** Non-empty account table → `409 Conflict` (`MapAuth.cs:38–41`). Covered by `BootstrapIsRefusedOnceAnOwnerExists`. |
| Secrets not in journals | **PASS.** Durable stamps are `ActorContext{PrincipalId, Username}` only. |
| Table store no plaintext | **PASS.** Entity field is `PasswordHash` (`TableAccountDirectory.cs:165`). |

Additional security notes (not blockers):

- **MAJOR — bootstrap TOCTOU:** concurrent empty-table bootstraps can both pass `IsEmptyAsync` and create accounts; second membership add may fail while orphan account remains.
- **MAJOR — non-atomic account+membership:** `/auth/bootstrap` and `/auth/users` create Identity row then `AddMemberAsync`; failure between leaves an account without membership.
- **MINOR — `SuccessRehashNeeded` ignored** on login (`MapAuth.cs:104–108`).
- **MINOR — loopback treats null `RemoteIpAddress` as loopback** (`LoopbackDevAuthMiddleware.cs:39–41`) — intentional for TestServer; safe only because Development-gated.

### 3. Actor propagation truth

End-to-end (HTTP user path):

1. Cookie claims include `db.principal-id` + name (`DigitalBrainClaimsPrincipalFactory.cs:17`).
2. `HttpActor.Require` builds `ActorContext` (`HttpActor.cs:8–33`).
3. `MapOwnerCommands` stamps `SendMessage(..., actor)` (`MapOwnerCommands.cs:125`).
4. `Chat.RememberOwnerTurnAsync` writes durable `OwnerCommand(CommandId, Text, Actor)` into `IDurableList` **and** emits `UserMessaged(..., Actor)` (`Chat.cs:222–233`, `OwnerCommand` at 247–251).

This is **not** RequestContext-only. Actor is serialized into durable grain state and journaled as a fact. Ratified “durable commands persist the actor stamp” is met on the Host chat path.

| Detail | Result |
|--------|--------|
| `Responded.Author` | **Not the human actor.** Author remains the responder/agent name (J1). Human actor lives on `OwnerCommand` / `UserMessaged`. Brief narrative “→ Responded.Author” is imprecise; design is consistent with J1. |
| Optional `Actor?` | **MAJOR.** Host always supplies; grain does not refuse null (`SendMessage`/`OwnerCommand` optional). Non-HTTP callers (e.g. MCP `ChatTools.Send` without actor) still produce unstamped durable commands. |

### 4. P0-4 really dead? (chat/transcript isolation)

| Path | Isolation |
|------|-----------|
| `POST /owner/commands` chat send/button | **PASS.** `PrincipalChat.InstanceName(actor.PrincipalId, request.ChatName)` — client conversation name cannot select another principal’s grain (`MapOwnerCommands.cs:49,68`; `PrincipalChat.cs:9–21`). Prefix is always the authenticated principal’s `N` form. |
| `GET /chats/{chatName}/events` | **PASS.** Same `PrincipalChat` + `HttpActor.Require` (`MapChatStreams.cs:28–29`). |
| Client `OwnerCommandRequest` | **PASS.** No Actor/Principal fields; composition pin asserts that. |
| Cross-principal transcript proof | **PASS (grain).** `TwoPrincipalsSendingToTheSameConversationNameGetIsolatedTranscripts`. |

Residual name-trusted resources (authenticated, not principal-scoped):

| Path | Severity |
|------|----------|
| `GET /surfaces/{surfaceName}/events` | **MAJOR.** Client name → `ISurface` under installation owner (`MapShellStreams.cs:14–37`; no `PrincipalChat` / `HttpActor`). Any member can watch any surface name. |
| `surface.open` via commands | **MAJOR.** Same client `SurfaceName` (`MapOwnerCommands.cs:77–90`). |
| Topology / graph / authorizations SSE | Authenticated installation-wide — acceptable as workspace-shared admin surface for MVP; not chat transcripts. |

**P0-4 as characterized (shared chat transcript via client `chatName`) is dead on the product HTTP chat path.** Surface name trust is a leftover multi-user privacy hole, not a re-open of the original chat pin.

Out of scope but noted: MCP `ChatTools` still routes raw `chatName` under the brain owner without `PrincipalChat` and without actor stamp — residual client-trusted identity outside this seam’s Host maps.

### 5. Kernel traps

| Trap | Result |
|------|--------|
| 3 FrameworkInterfaces | **PASS.** `IWorkspace` is synapse/`IHandle`-only; no new free grain methods need listing. |
| 4 Settled refusals | **PASS.** Workspace authz uses `NeuronAuthorizationException`. |
| 8 Broadcast catalog | **PASS.** New `IHandle<>` on workspace membership are intentional contract verbs (same pattern as graph). No accidental demo handlers. |
| 2 Zero-receiver emissions | **PASS.** Workspace mutators use `ReplyAsync` on RequestSynapses; chat still emits on established paths. |

### 6. Pins

RED pins (with `// PIN-DEFECT(P0-3|P0-4)`) were **flipped**, not deleted:

| RED pin | GREEN-2 assertion |
|---------|-------------------|
| `KernelHttpSurfaceIsTheUnauthenticatedInventory…` | `KernelHttpSurfaceRequiresAuthenticationExceptAuthOauthAndHealth` |
| `SessionAndChatIdentitiesResolveUnderTheClientOwner` | `…WithPrincipalScopedChatNames` |
| `OwnerCommandRequestAcceptsClientChatNameAndCarriesNoActor` | `…ButNeverClientActor` + `PrincipalChat` |
| `TwoClientsSendingToTheSameChatNameShareOneTranscript` | `TwoPrincipals…GetIsolatedTranscripts` |
| `DurableChatCommands…CarryNoActorIdentity` | `…CarryActorStampOnUserPath` |
| Singleton `"dev"` composition | Still asserted as installation fact (no longer PIN-DEFECT) — honest residual of single-`IDigitalBrain` host |

Repo-wide: **zero** remaining `PIN-DEFECT(P0-3)` / `PIN-DEFECT(P0-4)` markers.

### 7. Quality

- Types are mostly SRP: directory / user store / claims / loopback / gateway / maps are cleanly separated; gateway does not reimplement membership.
- Naming matches kernel vocabulary (`ActorContext`, `PrincipalId`, `WorkspaceRole`, `db.workspace.*`).
- **MAJOR — weak HTTP isolation tests:** `AuthEndpointProofs` use a stand-in `MapGet("/owner/commands")`, not real `MapOwnerCommands`/`MapChatStreams`. `TwoUsersReceiveDistinctPrincipalScopedChatInstanceNames` only compares `PrincipalChat.InstanceName` strings after `/auth/users` — it does **not** exercise SSE or command routing. Grain isolation is real; claimed “two-user principal isolation” at the HTTP TestServer layer is overstated.
- **MINOR — `HttpActor.Require` throws `InvalidOperationException`** (500) if claims are broken rather than mapping to 401.
- **MINOR — invalid conversation names** (`/` / whitespace) throw `ArgumentException` from `PrincipalChat` instead of 400.

### 8. Gate (verified this session)

```
dotnet build DigitalBrain.slnx
Build succeeded.
    0 Error(s)
(Time Elapsed ~4.2s; node NO_COLOR noise from AppHost CodeGraph target is not a C# warning)

& src/Tests/DigitalBrain.Tests/bin/Debug/net11.0/DigitalBrain.Tests.exe
=== TEST EXECUTION SUMMARY ===
   DigitalBrain.Tests  Total: 96, Errors: 0, Failed: 0, Skipped: 0, Not Run: 0, Time: 41.253s
EXIT=0
```

## Conflicts & risks (non-blocking)

- Orchestration text still wants a “workspace-scoped brain factory” replacing singleton `"dev"`. GREEN-2 brief DoD does not; installation remains one `IDigitalBrain` + multi-principal chats. Honest for one-installation=one-workspace MVP; call out if multi-brain tenancy was expected at S1.2 exit.
- Invitations, surface ACLs, MCP principal binding, OAuth principal binding (P0-5 / S1.3) remain open.

## Out of scope (noticed, not required)

- MCP `ChatTools` / `ReadChatTranscript` still take bare `chatName`.
- Flutter owner presentation strings.
- OAuth state rail (S1.3).
- Orleans dashboard `/orleans` (fallback auth likely applies; no principal ACL).

## Findings (severity)

1. **MAJOR** — Surfaces remain client-name addressed without principal scope (`MapShellStreams.cs:14–37`, `MapOwnerCommands.cs:77–90`). Authenticated peer can open/watch another user’s surface by name.
2. **MAJOR** — `Actor` is optional on durable chat records; grain does not refuse missing stamp (`SendMessage.cs:10`, `Chat.cs:251`). Host path is correct; other entry points can omit actor.
3. **MAJOR** — No in-process HTTPS-beyond-localhost enforcement (`AuthHostingExtensions.cs:49`).
4. **MAJOR** — Bootstrap/user-create races and non-atomic account+membership can orphan accounts (`MapAuth.cs:38–70`, `188–211`; `TableAccountDirectory.IsEmptyAsync`).
5. **MAJOR** — Invitations not modeled despite §1.13 (deferred product gap).
6. **MAJOR** — HTTP TestServer suite does not prove real command/SSE principal isolation; isolation evidence is grain + helper unit only (`AuthEndpointProofs.cs:176`, `113–143`).
7. **MINOR** — Login does not rehash on `SuccessRehashNeeded`.
8. **MINOR** — Null remote IP treated as loopback under Dev bypass.
9. **MINOR** — Bad conversation names / broken claims yield 500 rather than 400/401.
10. **MINOR** — Installation singleton `"dev"` owner remains (documented residual).

## Verdict rationale

P0-3 (unauthenticated HTTP surface + no caller identity on host path) and P0-4 (client-trusted chat transcript key) are fixed for the Host chat/SSE paths with durable actor stamps, framework password hashing, Development-only loopback, workspace role grain, last-Owner invariant, flipped pins, and a green gate. Remaining issues are multi-user surface isolation, actor enforcement at non-Host edges, operational races, HTTPS stance, and test depth — none re-open the characterized chat identity defects as blockers.

---

VERDICT: APPROVE

Findings: 0 BLOCKER, 6 MAJOR, 4 MINOR (see list above). MAJORs are backlog for follow-on seams (surface ACLs, actor required at grain, HTTPS stance, invitations, stronger HTTP e2e proofs) — not grounds to reject this identity slice.
