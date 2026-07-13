# Subsystem audit: mcp-hosts-build

- **Scope**: `src/DigitalBrain.Mcp/` (MCP server + shared runtime transport), `hosts/DigitalBrain.AppHost/`, `hosts/DigitalBrain.ServiceDefaults/`, `deploy/` (Pulumi), `.github/workflows/`, root build/package/config files (`.editorconfig`, `.gitattributes`, `.gitignore`, `.lsp.json`, `.mcp.json`, `AGENTS.md`, `Brain.slnx`, `CLAUDE.md`, `Directory.Build.props`, `Directory.Build.targets`, `Directory.Packages.props`, `LICENSE`, `README.md`, `aspire.config.json`), `.config/`, `.codex/`, `.codegraph/`, `docs/` (adr + plan/log docs). 55 files.
- **Commit**: `72400e3ebbec27e17af4ae6b5b2c4158c2797fa4` (branch `docs/refinement-audit`)
- **Date**: 2026-07-13

## Subsystem overview

`DigitalBrain.Mcp` is the single machine- and UI-facing edge of the OS: an Orleans **client** (no grains hosted) exposing (a) one authenticated MCP tool (`ino_interact`) over streamable HTTP at `/mcp`, (b) the authenticated gRPC/gRPC-Web UI transport (`DigitalBrainV2Ui`: session bootstrap/refresh/logout, surface-feed watch/ack, action submit), and (c) an unauthenticated but tightly-bounded OAuth-start reverse proxy to the kernel. All durable state lives in kernel grains (`IConversationNeuron`, `ISurfaceFeedNeuron`, `ISessionNeuron`); the Mcp project's `ConversationStateClient`/`RuntimeSurfaceFeed`/`RuntimeSessionAuthority` are optimistic-concurrency adapters over them. The AppHost composes kernel (3-replica HA via `DigitalBrain.Aspire`), single-replica MCP, storage waits, and dev Flutter clients. `deploy/` is a minimal Pulumi program provisioning ACA (kernel 2–5 replicas, MCP pinned 1), Storage (MI-only), Azure OpenAI, Log Analytics/App Insights. CI gates PRs on whitespace/credential policy, full `dotnet test`, an Aspire publish-graph validation, and Flutter analyze/test/build; deploy is release-gated and re-runs tests before publishing images and running `pulumi up`.

**Trust-boundary verdict (subsystem question)**: MCP tool calls **cannot** bypass the rail. `ino_interact` demands `brain.interact`, flows through `McpInoCommandHandler.AcceptAsync` → `ConversationStateClient.BeginAsync` → conversation grain — the identical journaled acceptance boundary the UI uses. Approvals are *not decidable* over MCP at all (only via UI `SubmitAction` with a signed surface action binding + capability token + awaiting-approval state check). MCP is accept-only and fail-closed. The weaker spots are upstream of the rail: OIDC tenant claims are fully trusted (SEC-501) and the UI transport implicitly appends `brain.interact` (ARCH-504).

---

## Per-file review

### src/DigitalBrain.Mcp/Program.cs (1–123, reviewed)
Host composition: ServiceDefaults, Orleans client (Redis local / Azure Table cloud selected by env), fail-fast profile + base64 session key, MCP server (`AddMcpServer().WithHttpTransport().WithTools<McpConversationTools>()`, `MapMcp("/mcp")`), per-request auth middleware + `McpRequestGuard` lease on `/mcp`, UI transport, OAuth start proxy, health endpoints. Fail-fast on missing profile/key is correct. MCP SDK hosting pattern matches official guidance (FRAME-503). Issues: dead `ITelemetrySink`/`SchemaRegistry` registrations (CLEAN-502); redundant env-var fallback for the signing key (CLEAN-503). Auth ordering is sound: authenticate → 401, then guard → 429, body-size clamp before guard. **Verdict: retain; delete dead registrations.**

### src/DigitalBrain.Mcp/McpTools.cs (1–65, reviewed)
`McpAuthority` (per-call re-authentication + `DemandGrant`) and the single `ino_interact` tool: authenticated, grant-gated, idempotent via caller `commandId`, returns durable receipt. Disciplined, minimal surface — supports the OS model. Gaps: no read/status tool (ARCH-501); per-call re-auth duplicates middleware work (PERF-502); `RequireFixedMcp` config re-validation per call is vestigial (CLEAN-503). No direct tests of `McpConversationTools`/`McpAuthority` (TEST-500). **Verdict: retain; add read tool or document write-only intent.**

### src/DigitalBrain.Mcp/McpConversationPipeline.cs (1–51, reviewed)
`McpInoCommandHandler.AcceptAsync`: validates command type + bounded single-`prompt` payload (≤4096), delegates to `ConversationStateClient.BeginAsync` with `CancellationToken.None` (deliberate: acceptance survives client disconnects, per ADR 0001), derives phase from the committed operation. Correct acceptance-boundary semantics; `snapshot.Operations.Single(...)` would throw `InvalidOperationException` if the commit were lost — acceptable integrity trip-wire. **Verdict: retain.**

### src/DigitalBrain.Mcp/RuntimeRequestAuthenticator.cs (1–36, reviewed)
MCP request auth: exactly-one bounded `Bearer` header → durable MCP-audience session validation, else OIDC JWT. Fail-closed, bounded header, no token echo. But the durable-session branch is unreachable today — nothing ever issues an MCP-audience session (ARCH-502). Tested (`RuntimeRequestAuthenticatorTests`). **Verdict: retain; resolve dead branch.**

### src/DigitalBrain.Mcp/RuntimeSessionAuthority.cs (1–252, reviewed)
Session lifecycle over `ISessionNeuron`: create, rotate-refresh with replay-triggers-revocation, revoke (idempotent, tolerates concurrent revocation), validate-access (signed token + grain state cross-check of identity/assurance/grants/version). Strong: SHA-256 refresh hashes, fixed-time compares, strict refresh-token grammar, revision-conflict handling, bounded revoke-after-replay retry (throws after 4 attempts — fail-closed). Audience restricted to the two fixed transports. Grants compare uses ordinal sort on both sides — consistent with `CreateAsync` storing sorted grants. **Verdict: retain.**

### src/DigitalBrain.Mcp/RuntimeTransportBoundary.cs (1–124, reviewed)
Edge middleware for `/mcp`, UI gRPC, `/oauth/start`: HTTPS-required (426), body cap (413), fixed-window rate + concurrency semaphore (429), 2-min request timeout (504) except the feed stream, catch-all → 500/abort. Correct semaphore release in `finally`; `WaitAsync(0)` avoids queueing. Issues: streams share the 32-slot semaphore with unary traffic (PERF-501); exception logging strips all context (REL-502); in-memory limiter is single-replica-only (REL-504). Tested. **Verdict: retain; split stream/unary budgets.**

### src/DigitalBrain.Mcp/ConversationStateClient.cs (1–413, reviewed)
Adapter over `IConversationNeuron`: read snapshot, begin operation (bounded ids/prompt, deterministic scoped `OperationId`, outbox projection written atomically with acceptance), decide approval (grant demand via `InoMutationGrants` before approving, replay-receipt idempotency, immutable-decision enforcement, `RuntimeStateIntegrityException` if decision not durably recorded). Optimistic-concurrency retry ×3 then hard fail. Canonical conversation-id grammar enforced (`ino-` + 64 hex). Correct fail-closed identity check in `EnsureInitializedAsync`. Legacy-state mapping (`LegacyState`) marks residual protocol duality. **Verdict: retain; candidate to move into a neutral transport library (ARCH-503).**

### src/DigitalBrain.Mcp/RuntimeSurfaceFeed.cs (1–758, reviewed)
Feed adapter: prepare session (init identity, ensure home surface, renew bindings, kick pending outbox dispatch), paged reads with retention-gap reset, delivery/ack cursors, and `AuthorizeActionAsync` — the heart of UI action security: signed binding + HMAC capability token + grant + surface revision + single-use consumption in the grain, with careful idempotent replays for both prompt submissions and approval decisions (prior-prompt equality, decision immutability, `DemandAwaitingApprovalAsync` state check). Canonical JSON for content-addressed idempotency keys. Concerns: 250 ms polling per client in `WaitForChangeAsync` and read+write per delivered item (PERF-500); the double `ConsumeActionAsync` conflict-retry block is duplicated code (~40 lines) mapping four exception types twice — simplify. The file is 758 lines doing authorization + paging + identity + JSON canonicalization; split candidate. Tested (`RuntimeSurfaceFeedTests`). **Verdict: split (authorization vs feed paging); keep behavior.**

### src/DigitalBrain.Mcp/UiGrpcService.cs (1–561, reviewed)
gRPC UI transport. Session RPCs demand exact `x-v2-audience`; bootstrap prefers OIDC, falls back to dev secret (disabled in prod); metadata parse rejects binary/duplicate headers (fail-closed empty dict). `WatchSurfaceFeed` re-validates the session every 5 s and caps the stream at access-token expiry. `SubmitAction` bounds input (64 KB, depth 64), blocks credential-shaped keys, and for approvals cross-checks decision↔operation before `DecideApprovalAsync` with `CancellationToken.None` (durable decision can't be client-cancelled — correct). Issues: implicit `brain.interact` grant append (ARCH-504); `RecordDeliveredAsync` before client ack per item is delivery bookkeeping, not ack — correct but costly (PERF-500). Tested. **Verdict: retain; make the grant escalation explicit policy.**

### src/DigitalBrain.Mcp/UiExternalIdentity.cs (1–214, reviewed)
OIDC options + authenticator: strict issuer/audience validation, HTTPS metadata (loopback dev exception), distinct claim names, bounded grant allowlist, unique-claim extraction, all-or-nothing grant intersection (any non-allowlisted asserted grant rejects the token — strict, good). Catch-all → Rejected (fail-closed) preserving cancellation. Tenant/workspace claims are trusted verbatim from the IdP (SEC-501). No `ValidAlgorithms` restriction (default asymmetric set — acceptable). Tested. **Verdict: retain; add tenant binding when multi-tenant is real.**

### src/DigitalBrain.Mcp/UiHostingExtensions.cs (1–113, reviewed)
gRPC (128 KB recv / 2 MB send, detailed errors off), CORS (prod defaults to a sinkhole origin if unset — fail-closed), forwarded headers (ACA trust clears proxy lists — SEC-502), OIDC JwtBearer registration, delivery options, health check. `UiTransportHealthCheck` always returns Healthy and its comment overclaims validation (REL-500). **Verdict: retain; fix health check.**

### src/DigitalBrain.Mcp/AuthorizationFlowStartProxy.cs (1–82, reviewed)
OAuth-start reverse proxy: parses/validates the flow path via `OAuthCallbackPaths`, forwards only to the configured internal origin (HTTPS-only in prod), 15 s deadline, accepts only redirect responses whose absolute Location passes the per-provider authorization-URL allowlist (open-redirect defense), no-store/no-referrer/CSP headers. Unauthenticated by necessity (browser navigation) — SEC-503. Tested. **Verdict: retain.**

### src/DigitalBrain.Mcp/BoundedOrleansClientConnectionRetryFilter.cs (1–26, reviewed)
60 × 2 s bounded gateway retry, then fail. Instance field `_attempts` with `Interlocked` — correct for the single registered filter. Cold-start coupling noted (REL-503). **Verdict: retain.**

### src/DigitalBrain.Mcp/InoTelemetry.cs (1–8, reviewed)
Single `ActivitySource("DigitalBrain.Mcp")`. Note `UiGrpcService` news up its own identical source (line 108) — duplicate; use one. **Verdict: retain; dedupe.**

### src/DigitalBrain.Mcp/DigitalBrain.Mcp.csproj (1–51, reviewed)
net11.0, ASP.NET **11.0-preview** container base image, SDK container publish, proto compiled `GrpcServices="Both"` (client stubs used by E2E tests). Preview base in prod → PROD-500. **Verdict: retain; move to a supported base when available.**

### src/DigitalBrain.Mcp/Protos/ui.proto (1–87, reviewed)
Clean minimal contract; surfaces are opaque JSON strings (server-driven UI); enum zero-value = PRINCIPAL is the only supported audience (validated server-side). **Verdict: retain.**

### src/DigitalBrain.Mcp/Properties/AssemblyInfo.cs (1–3), Properties/launchSettings.json (1–11) — reviewed
`InternalsVisibleTo("DigitalBrain.Tests")`; local launch profile whose fixed ports are deliberately bypassed by the AppHost (`launchProfileName: null`). Fine.

### hosts/DigitalBrain.AppHost/AppHost.cs (1–186, reviewed)
Composition root. Profile fail-fast in publish; persisted generated secrets in run mode (44-char base64-alphabet keys ≈ 33 bytes); paired-OIDC fail-fast for the dev web UI; MCP resource single-replica with health check on `/health` (https), `WaitFor` on three blob stores + kernel; Flutter wired only to MCP's authenticated boundary; bootstrap secret dev-only. Comment block correctly warns about last-write-wins env vars. Aspire API usage (`WithHttpHealthCheck`, `WaitFor`, `AddParameter(secret, persist)`, `AsHttp2Service`) is consistent with documented semantics; Aspire 13.4.6-specific docs could not be pulled (FRAME-503 gap). Vestigial always-true TFM condition in the csproj (FRAME-504). Commented-out provider blocks (OpenAI/Anthropic/GitHub) are acceptable switchboard documentation. **Verdict: retain.**

### hosts/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj (1–23), Properties/launchSettings.json (1–29), appsettings.json / appsettings.Development.json (1–11 each) — reviewed
Aspire.AppHost.Sdk 13.4.6; standard dashboard ports; identical logging config in both appsettings (Development copy is redundant — micro-CLEAN). FRAME-504 condition. **Verdict: retain.**

### hosts/DigitalBrain.ServiceDefaults/Extensions.cs (1–166, reviewed)
Standard Aspire service defaults + Orleans/INO meters and activity sources; OTLP and Azure Monitor exporters (both can be active if both env vars set — double-export possibility, Note); health-response writer whitelists safe keys only (good); honest comment about publicly reachable `/health` on shared ingress. Tracing filter excludes health/alive/oauth paths — OAuth requests invisible in traces (deliberate secret-hygiene trade-off; worth a metric instead). **Verdict: retain.**

### hosts/DigitalBrain.ServiceDefaults/DigitalBrain.ServiceDefaults.csproj (1–23, reviewed)
OTEL 1.16/1.15.x pins via central versions. **Verdict: retain.**

### deploy/Program.cs (1–561, reviewed)
Single-file Pulumi program. Strong points: fail-fast required secrets/settings; HTTPS-origin validation; exact-audience and exact-OAuth-callback demands; storage with shared-key access disabled + MI role assignments (kernel RW, MCP table-**reader** only — correct least privilege for an Orleans client); single-revision MCP with honest no-autoscale comment; legacy-URN aliases to avoid resource replacement. Weak points: PROD-501 (SPOF edge), PROD-502 (stack "dev" = prod; duplicated literals with deploy.yml), OpenAI key-based auth still wired (acknowledged as staged migration in comment), `NetworkRuleSet.DefaultAction = Allow` on storage (public network reachable, MI-authenticated — acceptable now, tighten later). **Verdict: retain; simplify naming and finish MI-only OpenAI.**

### deploy/DigitalBrain.Deploy.csproj (1–23), Pulumi.yaml (1–5), Pulumi.dev.yaml (1–12), deploy/.gitignore (1–2) — reviewed
Standalone (non-central) versioning is a reasonable isolation choice; `OpenTelemetry.Api`/`Exporter` PackageReferences appear unused by `Program.cs` — delete candidates (CLEAN-505 adjunct). Pulumi.dev.yaml holds only non-secrets + encryption salt and documents what must never be committed — good. Stale `imageTag: v4` (CLEAN-505). **Verdict: retain.**

### .github/workflows/ci.yml (1–168, reviewed)
CI **does** gate on tests: full `dotnet test` (no filter), plus whitespace policy, bespoke credential-content and credential-filename scans, deploy-project build, Aspire publish-graph validation (`--list-steps` with Production profile), and a full Flutter analyze/test/build job. `dotnet-quality: preview` (PROD-500). Missing CodeQL/dependency scanning (SEC-504). Note: `dotnet build` triggers the `InitCodeGraph` npx target on runners (SEC-500). **Verdict: retain; add supply-chain scanning.**

### .github/workflows/deploy.yml (1–382, reviewed)
Release-gated; re-runs .NET + Flutter tests before publishing; Azure OIDC login (no long-lived cloud creds); secrets passed to Pulumi via env; SWA token masked; MCP/domain/SWA smoke tests; a genuinely good bundle-hygiene test asserting the Flutter JS contains only the custom MCP endpoint and no kernel/generated endpoints. Weaknesses: imperative az-CLI hostname orchestration (~200 lines) duplicating IaC authority; hard-coded resource names duplicated with Pulumi (PROD-502); Docker Hub personal account supply chain (PROD-503); smoke test only proves the always-healthy `/health` (REL-500). **Verdict: simplify (move hostname management into Pulumi).**

### Root files
- **.editorconfig (1–250, reviewed)**: thorough; Security/Reliability/Usage/CodeQuality analyzers at warning, four CA rules escalated to error, naming rules, generated-code exclusions. `*.slnx` marked `generated_code` while the comment says manually curated (CLEAN-504). No `TreatWarningsAsErrors` anywhere (analyzer warnings don't fail CI builds) — Note.
- **.gitattributes (1–14, reviewed)**: CRLF-normalized .NET files, LF for shell. Consistent with .editorconfig. Fine.
- **.gitignore (1–460, reviewed)**: stock VS template + repo-specific additions; lines 458–459 ignore the two build sentinels — but one of them is *tracked*, nullifying the rule (REL-501).
- **.lsp.json (1–20, reviewed)**: csharp-ls via `dotnet tool run` (depends on tool restore actually running — coupled to REL-501) + Dart LSP. Fine.
- **.mcp.json (1–24, reviewed)**: five agent-side MCP servers, all stdio commands wrapped in `gcf-proxy`, npm packages unpinned/`@latest` (SEC-500). These are development-agent tools, not product runtime — trust level is "developer machine".
- **AGENTS.md (1–4, reviewed)**: pointer to CLAUDE.md, exactly as planned (P1.12 done). Fine.
- **Brain.slnx (1–42, reviewed)**: curated solution; includes deploy project (skipped by default via `SkipDeployBuild`); trailing blank lines (CLEAN-504). Fine.
- **CLAUDE.md (1–116, reviewed)**: the WoW doc; mostly living, but the MCP standalone-run speed hack is dead (`DIGITALBRAIN_MCP_TRANSPORT` read nowhere; standalone run fails without profile/key/cluster; no digitalbrain entry in .mcp.json) — CLEAN-501.
- **Directory.Build.props (1–18, reviewed)**: opt-in skip flags + `EnforceCodeStyleInBuild`. Sound fast-loop design. No central nullable/warnaserror (each csproj sets Nullable) — Note.
- **Directory.Build.targets (1–27, reviewed)**: tool-restore + CodeGraph-init sentinel targets. The CodeGraph target executes an unpinned third-party npm package silently on build (SEC-500) and its sentinel design is broken by the tracked sentinel (REL-501).
- **Directory.Packages.props (1–124, reviewed)**: central pins with dated provenance comments — good practice. Findings: Orleans stable/preview skew (FRAME-500), journaling alpha in prod (PROD-500), four overlapping AI SDKs + stale "Marketplace economics" comment (FRAME-501), unmaintained DeveloperForce.Force (FRAME-502).
- **LICENSE (1–21, reviewed)**: MIT, 2026 Digital Brain Tech. Fine.
- **README.md (1–86, reviewed)**: current enough on commands/tests; still leads with the pack/marketplace self-evolution narrative that `docs/architecture-assessment-and-plan.md` itself calls fiction, and the promised one-line user promise (P4.5) was never written — Note under CLEAN-500.
- **aspire.config.json (1–5, reviewed)**: apphost pointer. Fine.
- **.codegraph/.gitignore (1–5, reviewed)**: ignore-all-but-self. Fine.
- **.codex/config.toml (1–52, reviewed)**: Codex-CLI mirror of .mcp.json, same unpinned commands (SEC-500). Duplicated server list will drift — Note.
- **.config/dotnet-tools.json (1–11, reviewed)**: csharp-ls 0.16.0 pinned. Fine.
- **.config/.tools-restored (empty, reviewed)**: tracked empty sentinel — the defect itself (REL-501).

### docs/
- **docs/adr/0001-durable-ino-operations.md (1–187, reviewed)**: the one genuinely durable document — authority table, deleted-paths record, sequence diagrams, invariants (lease fencing, no blind retry, OutcomeUnknown, OAuth suspend/resume, outbox identity), regression-coverage map. Matches the code read in this audit (acceptance with `CancellationToken.None`, outbox-projection-at-acceptance, actor-bound idempotent approvals). **Verdict: retain — keep current.**
- **docs/architecture-assessment-and-plan.md (1–150, reviewed)**: point-in-time assessment that says of itself "execute, then delete or trim to a short retro". Several findings since fixed (AGENTS.md, dead packages, ghost dirs, `ClosedInoToolGateway` — Kernel now has `DigitalBrain__Tools__Enabled=true` and typed effects per execution-log), several still open (M1 csproj reference — verified still present; P3.1 transport extraction not done). Now partially misleading. **Verdict: trim to retro or delete (CLEAN-500).**
- **docs/execution-plan.md (1–203, reviewed)**: one-shot agent playbook against a 2026-07-12 baseline; branch/LOC/task state stale. **Verdict: delete after extracting still-open tasks (CLEAN-500).**
- **docs/execution-log.md (1–62, reviewed)**: append-only run log; historically valuable evidence (incl. Context7 quota blocks, blocked external acceptance), but not a living doc. **Verdict: archive/trim (CLEAN-500).**
- **docs/grok-prompt.md (1–19, reviewed)**: copy-paste prompt for a third-party CLI agent; pure session artifact. **Verdict: delete (CLEAN-500).**

---

## Findings

### ARCH-500: Kernel still references the MCP Exe project with zero remaining code usage
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Kernel/DigitalBrain.Kernel.csproj:60` — `<ProjectReference Include="..\DigitalBrain.Mcp\DigitalBrain.Mcp.csproj" />`; grep of `src/DigitalBrain.Kernel/**/*.cs` finds no `DigitalBrain.Mcp` usage (FACT).
- **Current behavior**: The silo build pulls the entire MCP host (JwtBearer, gRPC UI, MCP SDK, Orleans-client config) into its dependency graph for nothing.
- **Why it matters**: (INFERENCE) Inverted layering documented as M1 in the repo's own assessment; the code usage was removed but the reference survived — dead coupling that bloats the silo image and lets edge concerns leak back into the kernel unnoticed.
- **OS/product consequence**: Kernel/edge trust boundary blurred at build level.
- **Recommendation**: (PROPOSAL) Delete the ProjectReference; add the planned architecture test "Kernel references no OutputType=Exe project" (execution-plan P6.4a).
- **Deletion/simplification opportunity**: yes — one line plus a guard test.
- **Dependencies**: ARCH-503 (transport-library extraction).
- **Tests/measurements required**: build green after removal; architecture test.
- **Effort**: S
- **Migration/rollback concern**: none.

### ARCH-501: MCP surface is write-only — no way for a machine client to observe an operation outcome
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Mcp/McpTools.cs:32-65` — the only tool is `ino_interact`, returning `{commandId, operationId, phase, version}` (FACT).
- **Current behavior**: An MCP client can durably submit a prompt but has no tool to read the conversation snapshot, poll the operation phase, or receive the assistant result; `ConversationStateClient.ReadAsync` exists and is unused by any MCP tool.
- **Why it matters**: (INFERENCE) The machine-facing entry point cannot complete a request/response loop; every MCP integration dead-ends at "Accepted". Deliberate minimalism is documented ("generic command/admin tools would bypass the rail"), but a *read* tool bypasses nothing.
- **OS/product consequence**: MCP as an OS entry point is demo-grade; agents cannot act on results.
- **Recommendation**: (PROPOSAL) Add a read-only `ino_status`/`ino_read` tool gated on `brain.read`, reusing `ConversationStateClient.ReadAsync`.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: TEST-500.
- **Tests/measurements required**: tool test: interact → poll → observe terminal phase.
- **Effort**: S
- **Migration/rollback concern**: none.

### ARCH-502: Durable MCP-audience sessions are validated but never issued
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Mcp/RuntimeRequestAuthenticator.cs:25-29` validates `SessionAudiences.Mcp` access tokens; repo-wide grep shows no `RuntimeSessionAuthority.CreateAsync`/`RefreshAsync` caller with the MCP audience (only `SessionAudiences.Ui` in `UiGrpcService`) (FACT).
- **Current behavior**: The durable-session branch of MCP authentication is unreachable; every real MCP request authenticates via the OIDC JWT fallback (configured under `DigitalBrain:Runtime:Ui:Oidc`, i.e. the *UI* section).
- **Why it matters**: (INFERENCE) Dead auth path misstates the security model (readers assume MCP has first-class sessions with rotation/revocation — it does not), and MCP auth silently depends on UI-named OIDC configuration.
- **OS/product consequence**: Auth boundary story for the machine entry point is misleading; revocation semantics OIDC-only.
- **Recommendation**: (PROPOSAL) Either add an MCP session-issuance flow (token exchange endpoint) or delete the durable-session branch and rename the OIDC config section to transport-neutral.
- **Deletion/simplification opportunity**: yes — a branch and its docs, or clarity gained.
- **Dependencies**: SEC-501.
- **Tests/measurements required**: authenticator tests updated to match the chosen model.
- **Effort**: S–M
- **Migration/rollback concern**: config key rename needs deploy update (`deploy/Program.cs:403-405`).

### ARCH-503: Shared runtime transport lives inside the MCP Exe project
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Mcp/` contains `RuntimeSessionAuthority.cs`, `RuntimeSurfaceFeed.cs`, `ConversationStateClient.cs`, `UiGrpcService.cs`, `ui.proto` — general runtime-transport components, not MCP-protocol code (FACT). Planned extraction (`docs/execution-plan.md` P3.1 `DigitalBrain.Runtime.Transport`) not executed (FACT).
- **Current behavior**: The Exe project doubles as the de-facto transport library, which is why ARCH-500's reference existed.
- **Why it matters**: (INFERENCE) Library-in-Exe invites re-coupling and makes the MCP host non-thin.
- **OS/product consequence**: Edge layer boundaries by convention only.
- **Recommendation**: (PROPOSAL) Execute P3.1: extract a `DigitalBrain.Runtime.Transport` classlib.
- **Deletion/simplification opportunity**: yes — Mcp shrinks to Program + guard + tools.
- **Dependencies**: ARCH-500.
- **Tests/measurements required**: existing Runtime tests keep passing post-move.
- **Effort**: M
- **Migration/rollback concern**: none (pure move).

### ARCH-504: UI transport silently escalates `ui.action` sessions with `brain.interact`
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Mcp/UiGrpcService.cs:380` — `var internalGrants = authenticated.Grants.Append("brain.interact").ToHashSet(...)` before building the send `CommandEnvelope` (FACT).
- **Current behavior**: Any authenticated UI session holding `ui.action` (plus a valid signed binding + capability token) submits INO interactions even if the session/IdP never granted `brain.interact`; the append happens after `AuthorizeActionAsync`, so the surface binding's `RequiredGrant` is the effective gate.
- **Why it matters**: (INFERENCE) Two grant vocabularies now answer "may this principal talk to INO": MCP demands `brain.interact` explicitly, UI conjures it. An operator revoking `brain.interact` at the IdP does not stop UI-driven interactions — surprising duplicated authority.
- **OS/product consequence**: Grant model (least-privilege, revocable) is not uniform across the two entry points.
- **Recommendation**: (PROPOSAL) Either make the send binding's `RequiredGrant` be `brain.interact` and drop the append, or document `ui.action ⊇ brain.interact` as an explicit policy constant in Core.
- **Deletion/simplification opportunity**: yes — one implicit rule becomes explicit.
- **Dependencies**: SEC-501.
- **Tests/measurements required**: test proving a session without the relevant grant cannot submit a send action.
- **Effort**: S
- **Migration/rollback concern**: prod OIDC AllowedGrants already includes `brain.interact` (`deploy/Program.cs:405`), so tightening is non-breaking there.

### PROD-500: Production runs on a fully preview/alpha toolchain
- **Severity**: High
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Mcp/DigitalBrain.Mcp.csproj:5,12` — `net11.0`, `ContainerBaseImage mcr.microsoft.com/dotnet/aspnet:11.0-preview`; `.github/workflows/ci.yml:99` and `deploy.yml:33` — `dotnet-quality: preview`; `Directory.Packages.props:31-40` — Orleans `10.2.1-preview.1`, Journaling `10.2.1-preview.1.alpha.1` (FACT).
- **Current behavior**: The deployed kernel and MCP images (the systems holding user OAuth tokens and executing approved external writes) run a preview .NET runtime, preview Orleans, and an alpha journaling provider.
- **Why it matters**: (INFERENCE) No servicing/security-patch guarantee for the base image; alpha journaling is the durability substrate for the "sacred" journal — a storage-format or correctness change upstream endangers replay/rollback, the core product promise.
- **OS/product consequence**: The durability/replay OS primitive rests on unsupported bits.
- **Recommendation**: (PROPOSAL) Record this as an explicit, dated risk acceptance (the journaling features force it); pin a migration checkpoint for when .NET 11 GA / Orleans 10.2.1 final ship; add an upgrade test that replays existing journals.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: FRAME-500.
- **Tests/measurements required**: journal-replay compatibility test across package bumps.
- **Effort**: M (process + test)
- **Migration/rollback concern**: journaling storage-format drift is the concern itself.

### PROD-501: MCP edge is a pinned single replica, single revision
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `deploy/Program.cs:341,420` — `ActiveRevisionsMode = "Single"`, `MinReplicas = 1, MaxReplicas = 1` with comment "Runtime actions and authorization leases are deliberately single-owner"; `hosts/DigitalBrain.AppHost/AppHost.cs:105` mirrors `.WithReplicas(1)` (FACT).
- **Current behavior**: Every deploy, crash, or node move takes down the only user/machine-facing edge; kernel HA (2–5 replicas) is moot while its sole front door is a SPOF.
- **Why it matters**: (INFERENCE) MCP holds no durable state (all in grains) — the single-owner claim applies to in-memory MCP protocol sessions and the in-process rate limiter, both solvable; the comment gates scaling on a "verified multi-replica coordination protocol" that no roadmap item tracks.
- **OS/product consequence**: Availability of every user journey.
- **Recommendation**: (PROPOSAL) Inventory actual per-replica state (MCP streamable-HTTP sessions, `RuntimeTransportBoundary` counters); make MCP sessions sticky or stateless-mode; then allow ≥2 replicas.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: REL-504, PERF-501.
- **Tests/measurements required**: two-replica soak with feed + tool traffic.
- **Effort**: M–L
- **Migration/rollback concern**: revision-mode change is reversible.

### PROD-502: Deploy authority split between Pulumi and bash with duplicated literals; prod stack named "dev"
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `.github/workflows/deploy.yml:145,151,159-165,210-211` hard-code `stack-name: dev`, `digitalbrain-rg`, `digitalbrain-mcp`, `digitalbrain-cae-prod`, `digitalbrain-web-prod`; same names as constants in `deploy/Program.cs:24-33`; ~200 lines of az-CLI hostname orchestration in the workflow (FACT).
- **Current behavior**: Custom domains and the SWA are managed imperatively outside Pulumi; renaming anything requires synchronized edits in two languages.
- **Why it matters**: (INFERENCE) Drift and partial-apply risk on the deploy path; "dev" stack for prod invites operator error.
- **OS/product consequence**: Deploy reproducibility.
- **Recommendation**: (PROPOSAL) Move hostname/SWA management into the Pulumi program; rename or alias the stack to `prod`.
- **Deletion/simplification opportunity**: yes — large workflow shrink.
- **Dependencies**: none.
- **Tests/measurements required**: `pulumi preview` no-op after migration.
- **Effort**: M
- **Migration/rollback concern**: Pulumi stack rename needs state migration; aliases mitigate.

### PROD-503: Production images pulled from a private personal Docker Hub account
- **Severity**: Note
- **Confidence**: High
- **Evidence**: `deploy/Pulumi.dev.yaml:3` `dockerHubUsername: vhorbachov`; `deploy/Program.cs:36-38,223,329` PAT-based registry credentials (FACT).
- **Current behavior**: Supply chain for both runtime images hangs on one personal account/PAT.
- **Why it matters**: (INFERENCE) Account compromise or rate limiting takes the deploy path down; no image signing/digest pinning (`latest` fallback exists at `Program.cs:62`).
- **OS/product consequence**: Deployment integrity.
- **Recommendation**: (PROPOSAL) Move to ACR with MI pull (no PAT), pin digests, drop the `latest` fallback.
- **Deletion/simplification opportunity**: yes — removes the DockerHub secret plumbing.
- **Dependencies**: none.
- **Tests/measurements required**: deploy smoke.
- **Effort**: M
- **Migration/rollback concern**: registry cutover is standard.

### SEC-500: Build- and agent-time execution of unpinned third-party npm packages
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `Directory.Build.targets:25` — `npx --yes @colbymchenry/codegraph init` runs before Build (silent, `ContinueOnError`) whenever the sentinel is absent, including on CI runners; `.mcp.json:4-22` and `.codex/config.toml:18-35` run `gcf-proxy`, `@colbymchenry/codegraph`, `@upstash/context7-mcp@latest` — all unpinned (FACT).
- **Current behavior**: A `git clean -fdx` + build on any machine (or a CI cache miss) downloads and executes the latest published version of a low-profile npm package with the developer's/runner's credentials in scope.
- **Why it matters**: (INFERENCE) Classic npm supply-chain vector; the build target is silent by design, so a malicious update would run invisibly. CI holds repo-scoped tokens; dev machines hold cloud/user secrets.
- **OS/product consequence**: Developer/CI trust boundary — upstream of everything the rail protects.
- **Recommendation**: (PROPOSAL) Pin exact versions (`@colbymchenry/codegraph@x.y.z`, `@upstash/context7-mcp@x.y.z`); skip the target when `CI=true`; consider vendoring or checksum-locking.
- **Deletion/simplification opportunity**: yes — the target can be dev-only.
- **Dependencies**: REL-501 (sentinel logic).
- **Tests/measurements required**: CI run showing target skipped; pinned versions resolve.
- **Effort**: S
- **Migration/rollback concern**: none.

### SEC-501: Tenant/workspace isolation delegated entirely to the external IdP's claims
- **Severity**: Medium
- **Confidence**: Medium
- **Evidence**: `src/DigitalBrain.Mcp/UiExternalIdentity.cs:95-126` — `TryMapPrincipal` builds `RequestContext` directly from `tenant_id`/`workspace_id`/`sub` claims with only length/control-char checks; grants intersected against a static allowlist; prod allowlist includes `brain.interact,ui.action,gmail.read,salesforce.read` (`deploy/Program.cs:405`) (FACT).
- **Current behavior**: Any validly-signed token from the configured issuer/audience asserts an arbitrary tenant and workspace; no server-side registry binds subjects to tenants.
- **Why it matters**: (INFERENCE) Cross-tenant isolation ("tenant-isolated, least-privilege" per the OS model) holds only if the IdP is configured to never let a subject influence those claims. A single IdP misconfiguration (custom-claim self-service, multi-app audience reuse) yields silent cross-tenant read/write of conversations and connector credentials.
- **OS/product consequence**: Tenant isolation primitive rests outside the system's control.
- **Recommendation**: (PROPOSAL) For the current single-tenant reality, pin expected tenant/workspace values in config and reject others; for multi-tenant, add a subject→tenant binding registry (grain) checked at session creation.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: ARCH-502, ARCH-504.
- **Tests/measurements required**: token with unexpected tenant claim rejected.
- **Effort**: S (pin) / L (registry)
- **Migration/rollback concern**: config addition only for the pin.

### SEC-502: ACA forwarded-headers trust clears all proxy allowlists
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Mcp/UiHostingExtensions.cs:30-41` — with `TrustAzureContainerAppsIngress=true`, `KnownIPNetworks.Clear()` + `KnownProxies.Clear()`; `ForwardLimit = 1`; comment cites Microsoft's Container Apps guidance (FACT).
- **Current behavior**: `X-Forwarded-Proto/For` accepted from any direct peer; the HTTPS check in `RuntimeTransportBoundary` then trusts the forwarded scheme.
- **Why it matters**: (INFERENCE) Safe only while ACA ingress is the sole network path to the container (true for external traffic; in-VNet peers within the environment could spoof). Documented pattern; residual risk accepted.
- **OS/product consequence**: HTTPS-only enforcement fidelity at the edge.
- **Recommendation**: (PROPOSAL) Keep, but note in deploy docs that no additional workloads may share the ACA environment without revisiting.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: none.
- **Tests/measurements required**: none beyond existing.
- **Effort**: S
- **Migration/rollback concern**: none.

### SEC-503: `/oauth/start/{provider}` is unauthenticated (accepted, bounded)
- **Severity**: Note
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Mcp/Program.cs:98-103` maps the route with no auth middleware; `AuthorizationFlowStartProxy.cs:29-72` bounds it via path parse, fixed internal origin, 15 s deadline, redirect-only + provider-authorization-URL allowlist, no-store/CSP headers (FACT).
- **Current behavior**: Browser-navigable OAuth start; rate-limited by `RuntimeTransportBoundary` (path included at line 120).
- **Why it matters**: (INFERENCE) Necessary for browser flows (no bearer on navigation); the flow reference (`f=`) must carry its own integrity (kernel-side) — outside this subsystem's files.
- **OS/product consequence**: Auth-on-demand journey.
- **Recommendation**: (PROPOSAL) None beyond keeping the allowlist test coverage.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: kernel OAuth subsystem audit.
- **Tests/measurements required**: existing `AuthorizationFlowStartProxyTests`.
- **Effort**: —
- **Migration/rollback concern**: none.

### SEC-504: No dependency/SAST scanning in CI
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `.github/workflows/` contains only `ci.yml` and `deploy.yml`; no CodeQL, no Dependabot config, no `dotnet list package --vulnerable` step; only bespoke credential-regex scanning (`ci.yml:47-89`) (FACT).
- **Current behavior**: Vulnerable-package and code-scanning gaps; the credential scan is good but narrow.
- **Why it matters**: (INFERENCE) With alpha/preview and unmaintained packages pinned (FRAME-500/502), no automated signal exists when CVEs land.
- **OS/product consequence**: Supply-chain hygiene for a credential-holding system.
- **Recommendation**: (PROPOSAL) Add Dependabot (nuget, github-actions, pub, npm) + a `--vulnerable --include-transitive` CI step; consider CodeQL C#.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: SEC-500.
- **Tests/measurements required**: CI run with the new gates.
- **Effort**: S
- **Migration/rollback concern**: none.

### PERF-500: Feed delivery is poll-per-client with grain-write-per-item
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Mcp/RuntimeSurfaceFeed.cs:353-365` — `WaitForChangeAsync` loops `ReadAsync` + 250 ms delay per connected stream; `UiGrpcService.cs:244-251` — per delivered item: `RevalidateAsync` (grain read) + `RecordDeliveredAsync` (grain read + conditional write); revalidation additionally every 5 s (FACT).
- **Current behavior**: Each connected client imposes ~4 grain reads/sec idle plus 3–4 grain calls per delivered event on the same single feed grain per principal.
- **Why it matters**: (INFERENCE) Fine for one user; O(clients × 4/sec) hot-grain load and battery/network churn as soon as more sessions exist; latency floor 250 ms.
- **OS/product consequence**: Feed scalability, the primary UI journey.
- **Recommendation**: (PROPOSAL) Replace polling with an Orleans observer/stream or in-silo notification; batch delivery records (record page max sequence once per page — the reset branch already does this).
- **Deletion/simplification opportunity**: yes — per-item `RecordDeliveredAsync` in the item loop can batch.
- **Dependencies**: PROD-501 (scaling), PERF-501.
- **Tests/measurements required**: grain-call count per delivered event before/after; idle-load metric.
- **Effort**: M
- **Migration/rollback concern**: delivery-dedupe semantics must be preserved.

### PERF-501: Long-lived feed streams share the 32-slot concurrency budget with all edge traffic
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Mcp/RuntimeTransportBoundary.cs:30-32,56` — one `SemaphoreSlim(32)` for `/mcp`, gRPC, and `/oauth/start`; `IsLongLivedFeed` (line 122) only exempts the stream from the *timeout*, not the semaphore (FACT).
- **Current behavior**: 32 concurrent `WatchSurfaceFeed` streams (each held up to 15 min) exhaust the boundary; subsequent MCP tool calls, session refreshes, and OAuth starts get 429.
- **Why it matters**: (INFERENCE) A handful of open browser tabs can starve the single-replica edge; effectively a self-DoS ceiling far below the rate limit's 600/min intent.
- **OS/product consequence**: Availability of every entry point under modest fan-out.
- **Recommendation**: (PROPOSAL) Separate budgets: small dedicated pool for streams, larger for unary; or exempt streams from the semaphore and cap them via a stream-specific counter.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: PROD-501.
- **Tests/measurements required**: unit test: 32 held streams + unary request → not 429 after fix.
- **Effort**: S
- **Migration/rollback concern**: none.

### PERF-502: Double full authentication per MCP tool call
- **Severity**: Note
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Mcp/Program.cs:70-96` authenticates `/mcp` in middleware; `McpTools.cs:15-23` — `McpAuthority.RequireContextAsync` re-runs `AuthenticateMcpAsync` (incl. session-grain read) per tool invocation (FACT).
- **Current behavior**: Two token validations + up to two grain reads per tool call.
- **Why it matters**: (INFERENCE) Defense-in-depth is fine, but the second pass could reuse the middleware's principal via `HttpContext.Items`; on the SDK's streamable-HTTP transport, tool calls may also arrive on a long-lived session where re-validation is actually the valuable one — keep one, not both blind.
- **OS/product consequence**: Latency/load only.
- **Recommendation**: (PROPOSAL) Cache the authenticated context per request in `HttpContext.Items`; keep grain-backed re-validation on a timer for long-lived MCP sessions.
- **Deletion/simplification opportunity**: yes.
- **Dependencies**: none.
- **Tests/measurements required**: authenticator call count per request.
- **Effort**: S
- **Migration/rollback concern**: none.

### REL-500: MCP readiness never reflects Orleans connectivity; health-check comment is false
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Mcp/UiHostingExtensions.cs:99-113` — `UiTransportHealthCheck` discards three injected singletons and returns `Healthy` unconditionally; comment claims "Constructor resolution eagerly opens/validates the durable feed" (constructors do no I/O) (FACT). ACA readiness probes `/health` (`deploy/Program.cs:383-389`); deploy smoke tests curl `/health` (`deploy.yml:310-334`) (FACT).
- **Current behavior**: MCP reports Ready even when the Orleans gateway is unreachable (post-startup cluster loss, kernel outage); traffic keeps routing and every tool/UI call fails.
- **Why it matters**: (INFERENCE) Deploys "pass" while the edge is non-functional; misleading comment invites false confidence.
- **OS/product consequence**: Recoverability signal for the whole edge.
- **Recommendation**: (PROPOSAL) Add a health check that pings a lightweight grain (or checks `IClusterClient` connectivity) with a short timeout, tagged `ready` only; fix the comment.
- **Deletion/simplification opportunity**: yes — the current check adds nothing; replace rather than keep both.
- **Dependencies**: REL-503.
- **Tests/measurements required**: health flips Unhealthy when cluster is stopped in a TestKit run.
- **Effort**: S
- **Migration/rollback concern**: readiness flapping during kernel deploys — use ACA probe thresholds.

### REL-501: Tracked `.config/.tools-restored` sentinel defeats tool restore and its own clean-up story
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `git ls-files .config/.tools-restored` → tracked (committed in `faebf08`) (FACT); `.gitignore:459` ignores it (no effect on tracked files) and `Directory.Build.targets:3,10-14` skips `dotnet tool restore` whenever the file exists, with a comment claiming it is "deleted by `git clean -fdx`" — tracked files are not removed by clean (FACT).
- **Current behavior**: On every fresh clone the sentinel already exists, so `dotnet tool restore` never runs; `.lsp.json`'s `dotnet tool run csharp-ls` then fails until someone restores manually. The sentinel also can never be regenerated "after clean" because clean won't delete it.
- **Why it matters**: (INFERENCE) The developer-tooling bootstrap silently broke; the mechanism's own documentation is wrong.
- **OS/product consequence**: Dev loop only, but the WoW doc leans on this tooling.
- **Recommendation**: (PROPOSAL) `git rm --cached .config/.tools-restored`; keep the ignore rule.
- **Deletion/simplification opportunity**: yes — one tracked file.
- **Dependencies**: SEC-500 (same target file).
- **Tests/measurements required**: fresh clone → build → `dotnet tool run csharp-ls --version` works.
- **Effort**: S
- **Migration/rollback concern**: none.

### REL-502: Edge exception handler logs only the exception type name
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Mcp/RuntimeTransportBoundary.cs:82-87` — `logger.LogError("Runtime transport request failed with {ExceptionType}.", exception.GetType().Name)`; the exception object is not passed (FACT).
- **Current behavior**: Any unhandled edge failure surfaces as e.g. "failed with InvalidOperationException" — no message, no stack, no path.
- **Why it matters**: (INFERENCE) Secret-hygiene motivation is sound (messages can carry tokens), but type-only makes production incidents nearly undiagnosable; a scrubbed message or exception fingerprint would preserve both goals.
- **OS/product consequence**: Recoverability/diagnosability of the edge.
- **Recommendation**: (PROPOSAL) Log the exception object to a sink with scrubbing, or at minimum include `exception.Message` length-bounded + activity id.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: none.
- **Tests/measurements required**: log assertion in boundary tests.
- **Effort**: S
- **Migration/rollback concern**: none.

### REL-503: Cold-start coupling — MCP process dies if the kernel gateway is absent > 2 minutes
- **Severity**: Note
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Mcp/BoundedOrleansClientConnectionRetryFilter.cs:8-9` — 60 attempts × 2 s; `hosts/DigitalBrain.AppHost/AppHost.cs:109` `WaitFor(kernel)` covers local runs only; ACA has no start ordering (FACT).
- **Current behavior**: In cloud, MCP may crash-loop during kernel outages/deploys until ACA restarts land after the kernel returns.
- **Why it matters**: (INFERENCE) Acceptable (fail-fast + supervisor), but combined with REL-500 the outage is invisible to health-based alerting.
- **OS/product consequence**: Boot/recovery time of the edge.
- **Recommendation**: (PROPOSAL) Keep bounded retry; fix REL-500 so the state is observable.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: REL-500.
- **Tests/measurements required**: none.
- **Effort**: —
- **Migration/rollback concern**: none.

### REL-504: In-memory fixed-window rate limiter is per-replica and reset-on-restart
- **Severity**: Note
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Mcp/RuntimeTransportBoundary.cs:33-35,101-115` (FACT).
- **Current behavior**: Correct at exactly one replica; silently multiplies limits if replicas increase (PROD-501's fix would interact).
- **Why it matters**: (INFERENCE) A latent assumption that must be revisited together with any scale-out.
- **Recommendation**: (PROPOSAL) Document the coupling; move to distributed limiting only when scaling.
- **Deletion/simplification opportunity**: no. **Dependencies**: PROD-501. **Tests/measurements required**: none now. **Effort**: — **Migration/rollback concern**: none.

### FRAME-500: Orleans stable/preview version skew across one runtime family
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `Directory.Packages.props:27-41` — Client/Clustering.AzureStorage/Clustering.Redis/Persistence.AzureStorage/Reminders.AzureStorage/TestingHost at `10.2.0`; Core/Core.Abstractions/Serialization/Server/Reminders/Streaming/Persistence.Memory at `10.2.1-preview.1`; Journaling at `10.2.1-preview.1.alpha.1` (FACT).
- **Current behavior**: One process mixes stable 10.2.0 provider assemblies with preview 10.2.1 core/serialization.
- **Why it matters**: (INFERENCE) Orleans serialization/membership protocols are normally compatible within a minor line, but preview core + stable providers is an untested-by-upstream combination; a wire/serializer change in the preview would surface as cluster-join or state-decode failures.
- **OS/product consequence**: Cluster membership and grain-state durability.
- **Recommendation**: (PROPOSAL) Align every Orleans pin to the same version the journaling alpha was built against; add a comment stating the tested matrix.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: PROD-500.
- **Tests/measurements required**: full E2E suite (already exercises real silo + client) after alignment.
- **Effort**: S
- **Migration/rollback concern**: storage-format sensitivity — test replay.

### FRAME-501: Four overlapping AI SDK stacks pinned; stale rationale comments
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `Directory.Packages.props:48-54,63,74` — `Microsoft.Extensions.AI(+.OpenAI)`, `Anthropic`, `Microsoft.Agents.AI`, `Azure.AI.OpenAI 2.1.0`, `OpenAI 2.12.0`; the `OpenAI` pin still carries the comment "Marketplace economics" although marketplace/Stripe were deleted (execution-log P1.8/P1.9) (FACT).
- **Current behavior**: Multiple provider SDKs coexist behind the `IChatClient` abstraction; comments no longer describe reality.
- **Why it matters**: (INFERENCE) Each SDK is an update/audit surface; the misleading comment hides which pins are load-bearing (`Azure.AI.OpenAI` is used by the deployed `azureopenai` provider; `OpenAI` may only back `Microsoft.Extensions.AI.OpenAI`).
- **OS/product consequence**: Dependency hygiene.
- **Recommendation**: (PROPOSAL) Re-verify consumers per pin (the planned P6.4d "every PackageVersion referenced" architecture test); correct comments.
- **Deletion/simplification opportunity**: possibly — any pin with no csproj reference.
- **Dependencies**: SEC-504.
- **Tests/measurements required**: the P6.4d gate.
- **Effort**: S
- **Migration/rollback concern**: none.

### FRAME-502: DeveloperForce.Force 2.1.0 — effectively unmaintained client on the external-write path
- **Severity**: Medium
- **Confidence**: Medium
- **Evidence**: `Directory.Packages.props:115-117` pins `DeveloperForce.Force 2.1.0` (last upstream releases ~2016-era project) plus `Newtonsoft.Json 13.0.4` explicitly to cover its transitive dependency (FACT; maintenance status is INFERENCE from package history — Context7 verification unavailable, quota).
- **Current behavior**: Salesforce reads/writes (approval-gated external mutations) flow through an unowned REST wrapper.
- **Why it matters**: (INFERENCE) No upstream fixes for API-version or security issues will ever arrive; the repo already pins its own `ApiVersion v61.0` around it (`deploy/Program.cs:314`).
- **OS/product consequence**: Verified-external-effect promise depends on a dead library's correctness.
- **Recommendation**: (PROPOSAL) Plan replacement with direct `HttpClient` + typed REST calls (the surface used is small) or a maintained client.
- **Deletion/simplification opportunity**: yes long-term (also drops Newtonsoft).
- **Dependencies**: Salesforce integration subsystem audit.
- **Tests/measurements required**: existing Salesforce suite against the replacement.
- **Effort**: M–L
- **Migration/rollback concern**: behavior-compatible REST calls; low.

### FRAME-503: Framework-usage verification status and documentation gaps
- **Severity**: Note
- **Confidence**: High
- **Evidence**: MCP SDK hosting (`AddMcpServer().WithHttpTransport().WithTools<T>()`, `MapMcp("/mcp")`, separate `/health` because MCP endpoints are unsuitable as probes) matches Microsoft Learn guidance (learn.microsoft.com ACA MCP tutorial; fetched 2026-07-13). Aspire `WithHttpHealthCheck` (https-endpoint default), `WaitFor`, ServiceDefaults OTEL shape match documented semantics (FACT). **Gap**: Context7 monthly quota exhausted during this audit (same blocker recorded in `docs/execution-log.md`), so version-exact docs for `ModelContextProtocol 1.4.0` and `Aspire 13.4.6` could not be pulled; stateful-vs-stateless HTTP-transport session defaults for 1.4.0 are unverified — the single-replica pin makes either default safe (FACT of config, INFERENCE of safety).
- **Recommendation**: (PROPOSAL) Re-verify `WithHttpTransport` session mode against 1.4.0 release notes before ever raising MCP replicas.
- **Effort**: S. Other fields: n/a.

### FRAME-504: Vestigial always-true TFM condition in AppHost csproj
- **Severity**: Note
- **Confidence**: High
- **Evidence**: `hosts/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj:19` — `<ItemGroup Condition="'$(TargetFramework)' == 'net11.0'">` around the Mcp reference; the project single-targets net11.0 (FACT).
- **Recommendation**: (PROPOSAL) Drop the condition. **Effort**: S. Other fields: n/a.

### CLEAN-500: docs/ dominated by stale one-shot agent artifacts contradicting the repo's own doc policy
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `docs/execution-plan.md` (baseline 2026-07-12, branch `shape-v3`, tasks partially superseded), `docs/execution-log.md` (run log, references unmerged PR #14), `docs/grok-prompt.md` (copy-paste prompt for another CLI agent), `docs/architecture-assessment-and-plan.md:3` — "This doc replaces itself: execute, then delete or trim to a short retro" (FACT). `CLAUDE.md` states "Only living docs: README + CLAUDE.md; 99% of historical plans/specs are noise — kill them" (FACT). Several assessment findings are now wrong (AGENTS.md fixed, dead packages removed, `ClosedInoToolGateway` superseded by `DigitalBrain__Tools__Enabled=true` in `AppHost.cs:78`), others still open (M1 reference — ARCH-500) (FACT).
- **Current behavior**: A newcomer (or agent) reading docs/ gets a mixture of fixed, open, and abandoned claims with no freshness markers.
- **Why it matters**: (INFERENCE) Misleading docs cost decisions; the repo's WoW explicitly demands their deletion.
- **OS/product consequence**: WoW integrity; agent-driven development quality.
- **Recommendation**: (PROPOSAL) Keep ADR 0001 (accurate, verified against code). Trim the assessment to a dated retro listing only still-open items (ARCH-500/503, memory layer, product promise); delete execution-plan and grok-prompt; archive execution-log outside docs/ or compress to the final state summary.
- **Deletion/simplification opportunity**: yes — ~430 lines of stale planning.
- **Dependencies**: none.
- **Tests/measurements required**: none.
- **Effort**: S
- **Migration/rollback concern**: history stays in git.

### CLEAN-501: CLAUDE.md's MCP standalone-run instruction is dead
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `CLAUDE.md:93` — `DIGITALBRAIN_MCP_TRANSPORT=http ASPNETCORE_URLS=... dotnet run --project src/DigitalBrain.Mcp ... (then connect via url in .mcp.json)`; repo-wide grep shows `DIGITALBRAIN_MCP_TRANSPORT` appears only in CLAUDE.md; `Program.cs` fail-fasts without `DigitalBrain:Profile` + `SessionSigningKey` + a reachable cluster; `.mcp.json` has no digitalbrain entry (FACT).
- **Current behavior**: Following the canonical WoW doc's speed hack produces an immediate startup exception.
- **Why it matters**: (INFERENCE) The single source of truth teaches a broken loop; agents will burn cycles on it.
- **OS/product consequence**: Dev-loop trust in the WoW.
- **Recommendation**: (PROPOSAL) Replace with the real invocation (profile + key env vars + running kernel, or "use aspire run + aspire MCP tools") or delete the hack.
- **Deletion/simplification opportunity**: yes.
- **Dependencies**: CLEAN-500.
- **Tests/measurements required**: manual: documented command starts the server.
- **Effort**: S
- **Migration/rollback concern**: none.

### CLEAN-502: Dead DI registrations in the MCP host
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Mcp/Program.cs:34,37-39` registers `ITelemetrySink`/`TelemetryBuffer` and `SchemaRegistry`; repo-wide grep finds zero constructor/consumer references (only the duplicate registration in `Kernel/Hosting/DigitalBrainOrleansExtensions.cs:215-216` and one direct-construction contract test) (FACT).
- **Current behavior**: Two service graphs carry unused singletons; `SchemaRegistry`'s schema descriptors imply governance that nothing enforces.
- **Why it matters**: (INFERENCE) Speculative code contradicting delete-first; suggests an envelope-validation feature that silently never shipped.
- **OS/product consequence**: none functional; clarity.
- **Recommendation**: (PROPOSAL) Delete both registrations (and the Kernel twins, flagged to that subsystem) or wire the registry into envelope validation deliberately.
- **Deletion/simplification opportunity**: yes.
- **Dependencies**: kernel subsystem.
- **Tests/measurements required**: build/tests green.
- **Effort**: S
- **Migration/rollback concern**: none.

### CLEAN-503: Redundant per-call/config re-validation in the MCP auth path
- **Severity**: Note
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Mcp/Program.cs:49` — env-var fallback for a key the configuration provider already surfaces; `McpTools.cs:17` — `SessionAudiences.RequireFixedMcp(configuration[...])` re-validated on every tool call though validated at startup (`Program.cs:36`) (FACT).
- **Recommendation**: (PROPOSAL) Drop the env fallback; inject the startup-validated audience value. **Effort**: S. Other fields: n/a.

### CLEAN-504: Cosmetic config contradictions
- **Severity**: Note
- **Confidence**: High
- **Evidence**: `.editorconfig:25-28` marks `*.slnx` `generated_code = true` while the comment above says it "is manually curated"; `Brain.slnx:39-43` trailing blank lines; `appsettings.json` and `appsettings.Development.json` in AppHost are byte-identical (FACT).
- **Recommendation**: (PROPOSAL) Batch cleanup. **Effort**: S. Other fields: n/a.

### CLEAN-505: Stale deploy metadata and legacy naming
- **Severity**: Note
- **Confidence**: High
- **Evidence**: `deploy/Pulumi.dev.yaml:4` `imageTag: v4` (superseded by workflow-driven tags); kernel container app named `digitalbrain-jobs` with container `jobs` (`deploy/Program.cs:225-269`) — a name predating the kernel role; `deploy/DigitalBrain.Deploy.csproj:17-18` OpenTelemetry packages with no usage in `Program.cs` (FACT).
- **Recommendation**: (PROPOSAL) Remove the stale tag and unused packages; renaming the ACA app is a replacement-level change — document instead. **Effort**: S. Other fields: n/a.

### TEST-500: No direct tests for the MCP tool surface and host pipeline composition
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: Tests exist for `RuntimeTransportBoundary`, `RuntimeRequestAuthenticator`, `UiExternalIdentity`, `AuthorizationFlowStartProxy`, `RuntimeSurfaceFeed`, `UiGrpcService` (tests/DigitalBrain.Tests/Runtime/*) (FACT). Zero test references to `McpConversationTools`, `McpAuthority`, or `ino_interact`; nothing exercises the `Program.cs:70-96` `/mcp` middleware ordering (auth → body clamp → guard) end-to-end (FACT; planned as P6.3, not delivered).
- **Current behavior**: The single machine-facing tool and the exact middleware composition that enforces its authentication are verified only indirectly.
- **Why it matters**: (INFERENCE) A future edit reordering `MapMcp` before the auth middleware, or loosening `DemandGrant`, would pass the entire suite.
- **OS/product consequence**: Trust boundary of the machine entry point unguarded by tests.
- **Recommendation**: (PROPOSAL) Add: (a) unit tests for `McpConversationTools` grant/idempotency behavior; (b) a WebApplicationFactory-style test asserting `/mcp` without a token → 401, with token minus grant → tool-level denial, and 429 under guard saturation.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: ARCH-501 (a read tool would make the E2E assertable).
- **Tests/measurements required**: the tests are the measurement.
- **Effort**: M
- **Migration/rollback concern**: none.

---

## Answers to subsystem-specific questions

1. **MCP tools / authentication / rail bypass**: One tool, `ino_interact` (`McpTools.cs`). Authenticated: middleware (`Program.cs:70-96`) + per-call `McpAuthority` demand `Bearer` auth (durable MCP session — currently unissuable, ARCH-502 — or OIDC JWT) and the `brain.interact` grant; unauthenticated `/mcp` → 401; guard adds origin/audience/body/rate/concurrency limits. **No rail bypass**: the tool reaches the same conversation-grain acceptance boundary as the UI, and approvals cannot be granted via MCP at all — approval decisions require a signed UI surface binding + HMAC capability token + awaiting-approval grain state (`RuntimeSurfaceFeed.AuthorizeActionAsync`). SDK usage verified against Microsoft Learn (FRAME-503); ModelContextProtocol 1.4.0-exact docs unavailable (Context7 quota — recorded gap).
2. **AppHost/ServiceDefaults topology**: Kernel (HA via `DigitalBrain.Aspire`, storage/journal wiring) → MCP (single replica, `WaitFor` kernel + 3 blob stores, `/health` https health check, h2) → Flutter (dev-only, MCP-endpoint-only, bootstrap-secret desktop / OIDC web). OTEL: OTLP + optional Azure Monitor, Orleans + INO sources/meters, health/OAuth trace filtering. Dependencies are correct; cold-start is bounded-retry-then-crash (REL-503) and readiness is fake (REL-500).
3. **CI/CD**: CI gates on full `dotnet test`, Flutter analyze/test/build, whitespace, bespoke credential scans, and an Aspire publish-graph validation. Deploy is release-gated, re-tests, uses Azure OIDC (no stored cloud keys), masks the SWA token, and smoke-tests endpoints + Flutter bundle hygiene. Gaps: preview toolchain (PROD-500), no dependency/SAST scanning (SEC-504), imperative az-CLI/IaC split (PROD-502), personal Docker Hub registry (PROD-503). Deploy manifests target ACA westeurope with Container-App secrets (no Key Vault) and least-privilege MI storage roles; MCP gets table-reader only — correct.
4. **Directory.Packages.props**: Orleans stable/preview skew (FRAME-500); journaling alpha in prod (PROD-500); overlapping AI SDKs + stale comments (FRAME-501); DeveloperForce.Force effectively EOL (FRAME-502). Central pinning itself with dated provenance comments is good practice.
5. **Directory.Build.props/targets**: opt-in skip flags sane; `EnforceCodeStyleInBuild` on but no warnaserror; codegraph auto-init target executes unpinned npm code silently on build (SEC-500) and its sentinel mechanism is broken by a tracked sentinel file (REL-501).
6. **`.mcp.json`**: five dev-agent MCP servers (codegraph, aspire, context7, dart via `gcf-proxy` stdio; microsoft-learn HTTP). Developer-machine trust; unpinned npm packages (SEC-500). `.codex/config.toml` duplicates it for another agent CLI (drift risk).
7. **docs/**: The named `architecture.md` / `architecture-v2-implementation-plan.md` do **not exist on this commit** (they exist only in later master commits). What exists: ADR 0001 (accurate, keep) plus four stale execution/assessment artifacts that the repo's own policy says to delete (CLEAN-500).
