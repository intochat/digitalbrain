# S1.2-GREEN-2 — ASP.NET Identity at the Host boundary   (role: GREEN, part 2 of 2)

Report path: `plans/stage1/reports/S12-identity-green2.md`

## Ratified constraints (binding)
- ASP.NET Core Identity at the Host boundary owns credentials/sessions. Username/password.
  Installation-local accounts — no Entra, no external IdP, no passkeys.
- Orleans grains own workspace membership/roles (built in part 1 — use them, don't duplicate).
- No password/credential material inside grains. Password hashing only in the Host.
- HTTPS is mandatory beyond localhost; local loopback dev may skip login.
- Every durable command carries the actor stamp (`ActorContext` from part 1).
- Client-supplied identity is banned: the server derives chat/transcript identity from the
  authenticated principal (kills P0-4).

## Objective
1. **Auth wiring in `DigitalBrain.Kernel`** (the HTTP host): cookie-based authentication using
   the shared-framework Identity primitives (`AddIdentityCore`/`SignInManager`-level or
   equivalent minimal composition — you need username+password verify, session cookie, logout).
   NO Entity Framework. Implement the user store over **Azure Table Storage** (the stack already
   carries `Aspire.Azure.Data.Tables`; Azurite locally): usernames, password hashes
   (`PasswordHasher<T>`), and the member's `PrincipalId` linking to the part-1 Workspace grain.
2. **Endpoints**: `POST /auth/login`, `POST /auth/logout`, `GET /auth/me`. First-run bootstrap:
   if the user table is empty, `POST /auth/bootstrap` creates the initial Owner account (and
   adds it to the Workspace grain); refuse when an Owner already exists. Admin user creation:
   `POST /auth/users` (Owner/Admin only) — creates account + membership via part-1 verbs.
3. **Require authentication on the entire existing surface** (`/owner/commands`,
   `/chats/{name}/events`, `/surfaces/{name}/events`, `/brain/topology`, `/graph/events`,
   `/authorizations/events`). OAuth callback (`/oauth/callback`) and `/auth/*` and health remain
   anonymous. SSE endpoints authenticate via the same cookie.
4. **Loopback dev bypass**: configuration `DigitalBrain:Auth:AllowLoopbackDev` (default: enabled
   only in Development environment). When active, a loopback request without a cookie runs as
   the bootstrap Owner principal. Never active outside Development.
5. **Actor propagation**: authenticated principal → `ActorContext` → flows into the command
   pipeline; the chat path persists the actor stamp on durable commands and passes the author
   into `Responded.Author` (added by J1). Chat grain identity becomes server-derived per
   principal (e.g. `chat:{owner}/{principalId}/main`) — the client-sent chatName is no longer
   trusted for identity (it may still select AMONG the caller's own conversations by name).
6. **Flip the RED pins**: the `// PIN-DEFECT(P0-3)` and `// PIN-DEFECT(P0-4)` pins from
   S1.2-RED now assert the NEW behavior (auth required; per-principal transcript isolation);
   remove the markers.

## Granted exception to the package rule
You MAY add exactly one package: `Microsoft.AspNetCore.Mvc.Testing` version
`11.0.0-preview.6.26359.118` (test project only) for WebApplicationFactory-based endpoint tests:
401 for anonymous on protected endpoints, login → 200 flow, two users → isolated transcripts,
bootstrap-once semantics, loopback bypass active only in Development. If the Kernel host shape
resists WebApplicationFactory, fall back to handler-seam tests and record why in the report.
No other packages.

## Design discipline
- Study the existing `MapOwnerCommands` / endpoint mapping code and Kernel composition BEFORE
  changing it; extend its style, don't re-architect the host.
- Table-storage user store: small, internal, in the Kernel project (or Security if crypto-shaped
  helpers belong there); no new project.
- Do not break the existing OAuth callback path.
- Keep the diff as small as honest correctness allows. TDD throughout.

## Out of scope
Workspace grains redesign (part 1 owns it), OAuth state rail internals (S1.3), MCP, Flutter,
docker/CI, git.

## Definition of done
Gate passes; RED pins flipped with markers removed; new endpoint tests green; report includes a
short design note (auth flow, store schema, actor flow) + test names.
