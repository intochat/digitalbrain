# S1.2-GREEN-2b — close GRILL MAJORs 1+3 (+3 minors) report

## What changed

### Finding 1 — principal-scoped surfaces
- `src/Kernel/DigitalBrain.Kernel/Auth/PrincipalScoped.cs` — shared `{principal:N}.{local}` naming (replaces former `PrincipalChat.cs` body); thin `PrincipalChat` + `PrincipalSurface` wrappers.
- `src/Kernel/DigitalBrain.Kernel/MapOwnerCommands.cs` — `surface.open` fires `ISurface` at `PrincipalSurface.InstanceName(actor, clientName)` (client name cannot select another principal’s surface).
- `src/Kernel/DigitalBrain.Kernel/MapShellStreams.cs` — `HttpActor.TryGet` + principal-scoped surface instance for SSE watch.

### Finding 3 — HTTPS stance in-process
- `src/Kernel/DigitalBrain.Kernel/Auth/HttpsStanceMiddleware.cs` — non-loopback + non-secure (no https scheme and no `X-Forwarded-Proto: https`) → 403 with one-line reason, before auth.
- `src/Kernel/DigitalBrain.Kernel/Auth/RequestNetwork.cs` — shared loopback + secure-transport helpers.
- `src/Kernel/DigitalBrain.Kernel/Auth/AuthHostingExtensions.cs` — `UseMiddleware<HttpsStanceMiddleware>()` first in `UseDigitalBrainAuth`.

### Finding 7 — rehash on login
- `src/Kernel/DigitalBrain.Kernel/Auth/MapAuth.cs` — on `PasswordVerificationResult.SuccessRehashNeeded`, rehash + `UserManager.UpdateAsync` then sign in.

### Finding 8 — null remote IP is remote
- `LoopbackDevAuthMiddleware` now uses `RequestNetwork.IsLoopback` (null IP → not loopback). Same helper gates HTTPS stance.

### Finding 9 — 400/401 not 500
- `MapOwnerCommands` / `MapChatStreams` / `MapShellStreams` use `HttpActor.TryGet` → 401 on broken claims; catch malformed principal resource names → 400.

### Tests
- `AuthEndpointProofs.cs` — extended for surface isolation via real `MapOwnerCommands`, HTTPS both directions, rehash-on-login, null-IP, 400/401 paths.
- `IdentityBoundaryProofs.cs` — composition pins for `HttpsStanceMiddleware` + `PrincipalSurface` on open/watch maps.

## Tests

| Name | Role |
|------|------|
| `TwoUsersReceiveDistinctPrincipalScopedChatAndSurfaceInstanceNames` | extended: distinct surface keys + real `surface.open` records principal-scoped instance |
| `NonLoopbackPlainHttpIsRefusedWith403` | HTTPS stance refuse |
| `NonLoopbackWithForwardedHttpsIsAllowedPastHttpsStance` | proxy https allowed (then 401 if anonymous) |
| `LoopbackPlainHttpRemainsAllowedByHttpsStance` | loopback plain HTTP unchanged |
| `LoginRehashesPasswordWhenVerificationRequestsRehash` | SuccessRehashNeeded → store new hash |
| `NullRemoteIpIsNotTreatedAsLoopback` | null IP → remote (403 via stance; no dev bypass) |
| `MalformedConversationNameOnChatCommandReturns400` | chat name with `/` → 400 |
| `MalformedSurfaceNameOnSurfaceOpenReturns400` | surface name with whitespace → 400 |
| `BrokenAuthClaimsOnCommandEndpointReturn401Not500` | authenticated but missing PrincipalId → 401 |
| `HttpActorTryGetRejectsBrokenClaimsWithoutThrowing` | unit: TryGet false |
| `PrincipalScopedRejectsSlashAndWhitespace` | unit: name validation + surface format |
| `OwnerCommandRequestAcceptsClientChatNameButNeverClientActor` | extended surface name pin |
| `KernelHttpSurfaceRequiresAuthenticationExceptAuthOauthAndHealth` | composition: HttpsStance + PrincipalSurface |

## Gate
```
dotnet build DigitalBrain.slnx
Build succeeded.
    0 Error(s)
(Time Elapsed ~2.6s; node NO_COLOR noise from AppHost CodeGraph target is not a C# warning)

& src/Tests/DigitalBrain.Tests/bin/Debug/net11.0/DigitalBrain.Tests.exe
=== TEST EXECUTION SUMMARY ===
   DigitalBrain.Tests  Total: 106, Errors: 0, Failed: 0, Skipped: 0, Not Run: 0, Time: 40.340s
EXIT=0
```

## Conflicts & risks
- TestServer leaves `RemoteIpAddress` null; auth endpoint proofs inject loopback by default via `X-Test-Remote-Ip` harness middleware so loopback HTTPS/dev-bypass semantics match Kestrel. Production Kestrel always sets remote IP.
- HTTPS stance trusts first `X-Forwarded-Proto` value without validating the peer is a trusted reverse proxy. Same class of residual as most edge-TLS apps; tighten at reverse-proxy config, not in this slice.
- Surface isolation uses the same neuron-name constraint as chat (`/` and whitespace banned). Client surface names that already contain those characters now get 400.
- Stand-in GET `/owner/commands` still used for simple 401/200 auth probes; surface open isolation and malformed-name tests use real `MapOwnerCommands` + recording `IDigitalBrain`.

## Out of scope
- GRILL findings 2 (Actor optional on grain), 4 (bootstrap races), 5 (invitations), 6 (deeper HTTP e2e SSE proofs) — not in this brief.
- OAuth, MCP, workspace grains, chat pipeline beyond surface scoping.
- No new packages / no Directory.Packages.props changes.
- Git (orchestrator-owned).
