# Subsystem audit: kernel-hosting

- **Subsystem**: kernel-hosting (Orleans/Aspire wiring, hosting extensions, config, auth, gRPC gateway, DB, sync, UI surfacing, uploads, tabular data, protos)
- **Scope**: 25 files under `src/DigitalBrain.Kernel/` subfolders Hosting, Config, Auth, Gateway, Db, Sync, Protos, Properties, Generated, Ui, Uploads, TabularData (full list in the ledger fragment)
- **Commit**: `72400e3ebbec27e17af4ae6b5b2c4158c2797fa4` (branch `docs/refinement-audit`)
- **Date**: 2026-07-13

## Subsystem overview

This subsystem is the composition root of the kernel process: `Hosting/DigitalBrainOrleansExtensions.cs` wires the Orleans silo (clustering, grain storage, reminders, encrypted journaling), DI for connectors/self-evolution/chat, CORS, Kestrel ports, and static web serving; `Hosting/DigitalBrainAppEndpoints.cs` + `Hosting/OAuthTransportBoundary.cs` expose the only kernel HTTP endpoints (connector OAuth start/callback). `Config/` owns pack-config persistence (DataProtection-encrypted values over a blob or in-memory backing store) and the OAuth state protector. `Hosting/RuntimeStateHosting.cs` loads the runtime-state KEK/signing key ring (fail-closed in Production/hosted).

The rest of the folders (Auth, Gateway, Ui, Uploads, TabularData, Sync, Db, Protos) are, as of this commit, largely a **legacy stratum from the pre-v2 gateway**: the `DigitalBrainGateway` gRPC contract in `Protos/digitalbrain.proto` is compiled but never mapped by any server (a test asserts it is absent), and its supporting components (IngressNeuron, SignalEgressBus/Subscriber, ChatNeuron, TabularDataParser, ChatUploadClassifier, SyncManifest, SqliteSchemaInspector, UserSessionNeuron/DevAuth) have **zero production callers**. The live UI transport (sessions, JWT, per-call auth, action authorization) lives in `src/DigitalBrain.Mcp` (audited separately). This is the single biggest fact about this subsystem: roughly half of its files are dead weight duplicating authority the v2 rail already owns.

## Per-file review

### src/DigitalBrain.Kernel/Hosting/DigitalBrainOrleansExtensions.cs (reviewed 1-361)
Composition root: `UseDigitalBrainOrleans` (silo), `AddDigitalBrainClients` (DI/clients), `MapDigitalBrainSetup` (pipeline/static web), `ConfigureDigitalBrainKestrel` (ports). Three execution modes: local prototype (localhost clustering, memory storage, prototype journals), Aspire-hosted connection-string mode, managed-identity mode (`DigitalBrain:Storage:AccountName`). Fail-closed on missing connection strings and key material in hosted/Production (`RequireConnectionString`, `RuntimeStateKeyConfiguration.Load(requireConfiguredKeys: true)`). Journal encrypted at rest via `EncryptedSynapseJsonConverter` under the runtime-state key ring — good. Defects: dual Orleans provider-configuration authority (FRAME-200); DataProtection key ring persisted unprotected (SEC-200, in `PackConfigServices` but decided here by what is passed); vestigial gRPC plumbing — `AddGrpc()`, `UseGrpcWeb`, gRPC CORS headers, h2c-only ports for a gateway service that is never mapped (ARCH-200, PROD-200); Google/Salesforce hard-wired into kernel DI including keyed `IConnector` registrations, seeders, and always-healthy connector health checks (ARCH-201, REL-202); duplicate `if (serveWebBundle)` blocks (CLEAN-202). Verdict: **split + simplify** — separate silo wiring from client/connector wiring, delete gateway remnants.

### src/DigitalBrain.Kernel/Hosting/DigitalBrainAppEndpoints.cs (reviewed 1-133)
Maps `/oauth/start/{provider}` and `/oauth/callback/{provider}`. Security posture is good: state round-trips through `IOAuthStateProtector` (fail-closed `TryUnprotect`), redirect URLs are allow-list-validated (`IsAllowedAuthorizationUrl`) before redirecting, responses carry no-store/CSP/no-referrer headers, constant HTML (no injection surface), bounded 2-minute server deadline linked to host shutdown. Defect: the endpoint body branches on `google` vs `salesforce` grain interfaces and client factories — provider knowledge baked into kernel hosting instead of a connector registry (ARCH-201). Callback error strings are generic (no secret leakage). Verdict: **retain, refactor provider dispatch** behind the connector abstraction.

### src/DigitalBrain.Kernel/Hosting/OAuthTransportBoundary.cs (reviewed 1-89)
Middleware clamping the `/oauth` surface: GET-only, zero body, HTTPS-required in Production, 120 req/min fixed window + 16 concurrent, 2-minute timeout, exception responses reduced to status codes with only the exception *type* logged (no secrets). Correct semaphore acquire/release pairing (short-circuit means the semaphore is only released when acquired). Weaknesses: the rate window is global per replica, not per client — one actor can starve all OAuth flows (REL-203); the HTTPS check trusts `X-Forwarded-Proto` which is spoofable when `TrustAzureContainerAppsIngress` clears proxy validation (SEC-204). Verdict: **retain**.

### src/DigitalBrain.Kernel/Hosting/RuntimeStateHosting.cs (reviewed 1-157)
Loads the runtime-state key ring from `DigitalBrain:Runtime:State`. Fail-closed: hosted/Production without key material throws; partial material throws; Production requires exact 32-byte KEKs (HKDF stretch allowed only outside Production); signing key ≥32 bytes and distinct from KEKs (enforced in `RuntimeStateKeyRing`). Zeroization after construction is safe — I verified `RuntimeStateKeyRing` copies input arrays (`src/DigitalBrain.Kernel/Runtime/EncryptedPersistentState.cs:26-27`). `RuntimeStateHealthCheck` never probes storage — it reports static metadata as Healthy (REL-202). Verdict: **retain** (health check should do a real probe or be renamed).

### src/DigitalBrain.Kernel/Hosting/DigitalBrainHostEnvironment.cs (reviewed 1-19)
`IsAspireHosted` = storage account name OR any of three connection strings, checked both via raw `Environment.GetEnvironmentVariable("ConnectionStrings__*")` and via `IConfiguration` — duplicated detection paths (CLEAN-204). Verdict: **simplify** (configuration-only check suffices; env vars flow through config providers).

### src/DigitalBrain.Kernel/Hosting/PrototypeJournals.cs (reviewed 1-34)
In-memory journal + no-op `IJournaledStateManager` for prototype mode; suppresses `ORLEANSEXP005` (alpha journaling APIs, FRAME-201). Registered only when `!requiresDurableStorage`, so it cannot leak into Production. Both types are declared with **no namespace** (global namespace) and `InMemoryJournalForPrototype<T>` is public (CLEAN-201). The no-op state manager silently discards `WriteStateAsync` — acceptable for prototype but a dev/prod behavior divergence worth remembering. Verdict: **retain, fix namespace**.

### src/DigitalBrain.Kernel/Config/PackConfigServices.cs (reviewed 1-49)
Registers DataProtection + `IPackConfigStore`. When blob-backed: `SetApplicationName`, `CreateIfNotExists` on the container (with an accurate comment about `AzureBlobXmlRepository` not ensure-creating), `PersistKeysToAzureBlobStorage(pack-config/dp-keys/keys.xml)` — key ring **is** correctly shared across replicas. But no `ProtectKeysWith*` is configured, so the key ring is stored **unencrypted** in the same container as the ciphertext it protects (SEC-200). The comment explaining Aspire's unkeyed null-sentinel behavior (lines 34-39) is valuable institutional knowledge and contradicts the unkeyed `AddAzureBlobServiceClient("grainstate")` registration left in `DigitalBrainOrleansExtensions.cs:242` (FRAME-200). Fallback to in-memory store only when no blob client is passed, which by construction only happens in non-hosted runs — fail-safe. Verdict: **retain, add key-at-rest protection**.

### src/DigitalBrain.Kernel/Config/PackConfigStore.cs (reviewed 1-64)
Per-value DataProtection with purpose chain `(root, scope, pack, key)` — good isolation; ciphertext never logged. Two coupled defects: `GetAsync` silently skips undecryptable values, and `SetAsync` writes the entire dictionary — so any caller doing read-modify-write after a transient decrypt failure permanently erases the affected value; concurrent writers last-write-win with no ETag (REL-201). Verdict: **retain, harden write path**.

### src/DigitalBrain.Kernel/Config/AzureBlobPackConfigBackingStore.cs (reviewed 1-159)
Opaque HMAC-SHA256-derived blob names (length-prefixed components — no separator-collision ambiguity), identifier key derived from the runtime-state signing key with a distinct purpose string. Legacy `{scope}/{pack}.bin` → opaque-name migration is conservative: copy-if-absent with 409 tolerance, byte-exact verification before trusting either copy, throws `InvalidDataException` on mismatch instead of overwriting. `SaveAsync` ensure-creates the container. Concern (minor): `LoadAsync` migration races with `SaveAsync` are resolved by verify-then-fail, never silent corruption. Verdict: **retain** — one of the best files in the subsystem.

### src/DigitalBrain.Kernel/Config/DataProtectionOAuthStateProtector.cs (reviewed 1-62)
Time-limited protector (10 min, hard cap 1 h), bounded owner (≤256) and state (≤4096) inputs, catches only `CryptographicException`/`JsonException`. The 32-byte random nonce is embedded but **never tracked**, so a protected state token is replayable within its lifetime; single-use enforcement is delegated to grain-side pending-flow state (SEC-205). Verdict: **retain**.

### src/DigitalBrain.Kernel/Config/IPackConfigBackingStore.cs (reviewed 1-9) / InMemoryPackConfigBackingStore.cs (reviewed 1-24)
Clean byte-mover contract; in-memory impl is test/prototype-only and exposes `Peek` for encryption assertions. Note: `LoadAsync`/`Peek` return the stored array by reference (mutable aliasing), harmless at current usage. Verdict: **retain both**.

### src/DigitalBrain.Kernel/Auth/DevAuth.cs (reviewed 1-23)
Hard-coded `admin`/`admin` gated by `IsDevelopment()` with a `DigitalBrain:Auth:DevAutoLogin` config override — the override means a single config flip enables the seeded credential in Production (SEC-203). Duplicates environment detection via raw `DOTNET_ENVIRONMENT` when `IHostEnvironment` is null. Verdict: **delete with the legacy auth stack** (see UserSessionNeuron).

### src/DigitalBrain.Kernel/Auth/UserSessionNeuron.cs (reviewed 1-364)
Journal-sourced login/session grain (`session-main` singleton): PBKDF2-SHA256 100k iterations + `FixedTimeEquals` (good primitives), session = journal scan of `UserSessionCreated`/`UserSessionEnded`. Critical facts: (a) **no production caller exists** — only tests reference `IUserSessionNeuron`; the live session authority is Mcp's v2 session gate (ARCH-202); (b) `AllowFirstUserProvisioning` defaults **true**, so the first `LoginRequest` with any credentials creates an `admin`-role user (SEC-202); (c) dev credentials bypass the password of an *existing* account when enabled; (d) the incoming `LoginRequest` synapse carries the **plaintext password** and the Neuron base journals every delivered synapse into the durable incoming journal (SEC-201); (e) the singleton journal grows unbounded and every session lookup is a full scan (REL-200); (f) `GetSessionByClientIdAsync` keys sessions on a client-supplied `clientId` defaulting to `"flutter"` for everyone — cross-user session confusion if ever used (noted under ARCH-202); (g) vacuous `OnActivateAsync` override (CLEAN-202). Verdict: **delete or fold into the v2 session rail**; do not leave a second, weaker login authority registered in the silo.

### src/DigitalBrain.Kernel/Gateway/IngressNeuron.cs (reviewed 1-11)
One-method grain that broadcasts an arbitrary `Signal` into the cluster timeline. No production caller (only the never-mapped gateway would have used it). If ever exposed, it is an unauthenticated arbitrary-signal injection primitive with no tenant scoping. Verdict: **delete** (CLEAN-200).

### src/DigitalBrain.Kernel/Protos/digitalbrain.proto (reviewed 1-93)
Hand-written proto for `DigitalBrainGateway` (Send/Health/Ask/Fire/Timeline/Transcribe/WatchHomeFeed/WatchSynapses/GetPackConfig). Compiled with `GrpcServices="Both"` (`DigitalBrain.Kernel.csproj:73`; generated C# lands in `obj/`, not checked in — handled correctly as build output) but **no server maps it**, and `tests/DigitalBrain.Tests/Runtime/KernelCompositionTests.cs:106` asserts it is absent from the endpoint graph. The Flutter app meanwhile ships checked-in generated stubs (`app/lib/grpc/digitalbrain.pbgrpc.dart`) from a *third*, no-longer-present proto revision (`AiHealth`, `SubmitPrompt`, `PushFlutterPerf`, `GetRfwLayout`…) — three-way contract drift (ARCH-200). The `GetPackConfig` rpc is contractually a decrypted-secret feed ("Secrets are returned here") with no auth field — a dangerous contract to leave lying around; `PackConfigReply` also starts field numbering at 2. Verdict: **delete** the proto, the csproj `<Protobuf>` item, and the stale Dart stubs.

### src/DigitalBrain.Kernel/Generated/GeneratedPackRuntime.cs (reviewed 1-53)
Despite the folder name this is **hand-written** (design comments, no generator header) — it owns the embodied-pack lifecycle for GeneratedNeuron. Misleading location invites both accidental exclusion from review and accidental deletion by "clean generated" tooling (CLEAN-203). `Ensure(journal, primaryKey)` ignores its `journal` parameter and only logs — dead parameter/near-dead method (CLEAN-202). Embodiment failures fall back to LLM with warnings, never crash the grain. Verdict: **move out of `Generated/`, prune `Ensure`**.

### src/DigitalBrain.Kernel/Db/SqliteSchemaInspector.cs (reviewed 1-382)
Careful read-only SQLite schema reflection: `Mode=ReadOnly`, `PRAGMA query_only=ON`, pooling off, 5 s command timeout, identifier quoting with `""` escaping, hard caps on objects/columns/FKs/indexes, logs only the file name (`SafeSourceLabel`). Registered as a DI singleton (`DigitalBrainOrleansExtensions.cs:201`) but **nothing resolves it** outside tests — a well-built engine with no car (CLEAN-200). Verdict: **delete or park until a real DB-schema surface exists**; the code quality itself is retain-grade.

### src/DigitalBrain.Kernel/Sync/SyncManifest.cs (reviewed 1-5)
Two records with **zero references anywhere** in the repository. Verdict: **delete** (CLEAN-200).

### src/DigitalBrain.Kernel/TabularData/TabularDataParser.cs (reviewed 1-82)
Pure xlsx → (headers, rows, stats). No production caller (tests only). Defects: headers come from `CellsUsed()` (skips blank cells) while data cells are read positionally 1..headerCount — a blank header cell in the middle misaligns every column to its right (REL-204); stats iterate the **entire** sheet with no row/cell cap, so a decompression-bomb xlsx inflates memory unbounded (`MaxUiRows` caps only the UI rows) (PERF-200). Verdict: **delete with the dead upload path**, or fix both if the chat-upload feature is revived.

### src/DigitalBrain.Kernel/Ui/ChatNeuron.cs (reviewed 1-31)
Emits an `RfwCard` per `VisualizeDataRequest`, conversation = unbounded journal scan. No production caller (`GetGrain<IChatNeuron>` appears nowhere in `src/`). Verdict: **delete** (CLEAN-200); the v2 conversation rail supersedes it.

### src/DigitalBrain.Kernel/Ui/SignalEgressBus.cs (reviewed 1-62)
Well-designed in isolation: bounded per-subscriber channels (1024, DropOldest — correct backpressure for best-effort egress), correct dispose-and-complete. But it is only registered in `tests/DigitalBrain.TestKit`, never in kernel DI, and `Subscribe` has no caller — the `WatchSynapses` rpc it was built for was never mapped. Verdict: **delete** (CLEAN-200).

### src/DigitalBrain.Kernel/Ui/SignalEgressStreamSubscriber.cs (reviewed 1-60)
Silo-lifecycle stream pump feeding the bus; correctly subscribes at `Active` stage and degrades (logs, no crash) on subscribe failure. Its registration extension `AddSignalEgressStreamSubscriber` is **never called**. Verdict: **delete** (CLEAN-200).

### src/DigitalBrain.Kernel/Uploads/ChatUploadClassifier.cs (reviewed 1-23)
Extension-only classification (`.xlsx` → TabularWorkbook); no content sniffing, no size limit — but no production caller either (tests only). Verdict: **delete with the upload path** (CLEAN-200).

### src/DigitalBrain.Kernel/Properties/launchSettings.json (reviewed 1-12)
Single project profile setting `DOTNET_ENVIRONMENT=Development`; ports come from `ConfigureDigitalBrainKestrel`. No secrets. Verdict: **retain**.

## Answers to subsystem questions

**Orleans silo + Aspire wiring.** `UseDigitalBrainOrleans` uses `IHostApplicationBuilder.UseOrleans(delegate)`. The AppHost (`src/DigitalBrain.Aspire/DigitalBrainBuilderExtensions.cs:89-95`) declares `AddOrleans("kernel").WithClustering(...).WithGrainStorage(...x4).WithReminders(...)` and `WithReference(ctx.Orleans)`, so Aspire injects `Orleans:*` provider configuration that `UseOrleans` consumes automatically (verified against Microsoft Learn: Aspire-Orleans integration expects `AddKeyed*Client` + parameterless `UseOrleans`). The kernel registers the keyed clients (`DigitalBrainOrleansExtensions.cs:229-240`) **and then also configures the same providers manually** with hand-constructed `TableServiceClient`/`BlobServiceClient` from connection strings inside the delegate — two configuration authorities for clustering, reminders, and all five storage providers (FRAME-200). The manual path additionally bypasses Aspire's health checks/telemetry settings. In the managed-identity branch the keyed clients are *not* registered at all, while the AppHost still injects `Orleans:*:ServiceKey` config — per the Orleans docs this is exactly the setup that "throws a dependency resolution error at runtime" if the config-driven provider binder runs (flagged inside FRAME-200; needs a deployment test). Dev-only shortcuts (localhost clustering, memory storage, prototype journals, ephemeral key ring) are correctly fenced behind `!(isAspireHosted || IsProduction)` and cannot be reached in Production without also failing the connection-string/key-material requirements — fail-closed. Clustering security relies on Azure Table access control (standard for Orleans; silo-to-silo traffic is unauthenticated by Orleans default, acceptable inside the ACA network boundary).

**Auth: principals/sessions/tenants; JWT.** The kernel process itself has **no ASP.NET authentication pipeline at all** — no `AddAuthentication`, no `UseAuthentication`, no JwtBearer (verified by repo-wide search: `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.9 is referenced and configured only in `src/DigitalBrain.Mcp/UiHostingExtensions.cs` + `UiExternalIdentity.cs`, which belong to the mcp subsystem audit). The kernel's HTTP surface is: anonymous OAuth start/callback endpoints (protected by encrypted, time-limited state and the transport boundary), health endpoints, and static files. Principals, sessions, tenants, bootstrap secret, and the JWT validation parameters are established exclusively in the Mcp UI transport. The **legacy** in-kernel session authority (`UserSessionNeuron` + `DevAuth`) is orphaned — no production caller — but remains a registered grain with fail-open first-user provisioning (SEC-202) and a config-enabled seeded credential (SEC-203); it should be deleted rather than trusted to stay unreachable. **Ino auth-gate**: within this subsystem the gate wiring is fail-closed — `DigitalBrain:Tools:Enabled` defaults false, registering `ClosedInoToolGateway` whose `TryAuthorizeMutation` unconditionally returns false and whose `ExecuteApprovedAsync` returns Failed (`src/DigitalBrain.Kernel/Runtime/ClosedInoToolGateway.cs:11-22`); `PlanInoToolGateway` is only registered on explicit opt-in (`DigitalBrainOrleansExtensions.cs:86-89`). The user-facing gate closure (per-call `AuthenticateAsync`, action tokens, forbidden-field scrubbing) lives in `UiGrpcService.SubmitAction` and is confirmed present, but its line-level verification belongs to the mcp audit.

**gRPC gateway.** The kernel's own gateway (`Protos/digitalbrain.proto`) is dead: `AddGrpc()` registered, `UseGrpcWeb` applied, dedicated h2c ports opened, external endpoint declared in the AppHost — and **no `MapGrpcService` anywhere in the kernel**; `KernelCompositionTests` pins this. So there is no per-call auth question to answer for the kernel: nothing is served (PROD-200, ARCH-200). The live gRPC surface (`DigitalBrainV2Ui`, Grpc.AspNetCore 2.71.0) is in Mcp with 128 KB receive / 2 MB send limits, detailed errors off, per-call authentication, and action authorization — usage of `AddGrpc`/`MapGrpcService`/`UseGrpcWeb` there matches current Grpc.AspNetCore guidance (grpc-web opt-in per endpoint via `EnableGrpcWeb`, CORS with exposed `Grpc-*` headers).

**Secrets.** No secret values are logged anywhere in this subsystem: `OAuthTransportBoundary` logs exception *types* only; `PackConfigStore` logs key names, never ciphertext/plaintext; `SqliteSchemaInspector` logs file names only; OAuth callback HTML is constant. Connector credentials are stored as per-value DataProtection ciphertext in blob (opaque HMAC names). The two real gaps: the DataProtection **key ring itself is unencrypted at rest in the same container** (SEC-200), and the legacy login path would journal plaintext passwords (SEC-201). LLM API keys flow in as Aspire secret parameters → env vars (AppHost side), not checked into config.

**Config: unsafe defaults / dev bypasses / secrets in config.** No secrets are checked in. Unsafe defaults found: `DigitalBrain:Auth:AllowFirstUserProvisioning` = true (SEC-202, latent), `DigitalBrain:Auth:DevAutoLogin` can force dev credentials in Production (SEC-203, latent), `TrustAzureContainerAppsIngress=true` clears all forwarded-header validation (SEC-204, documented ACA practice but weakens the OAuth HTTPS gate). `DigitalBrain:Tools:Enabled` defaults to fail-closed — correct. Prototype journals and memory storage cannot be reached in Production.

**DataProtection key ring across replicas.** Yes — shared correctly when Aspire-hosted: `SetApplicationName("DigitalBrain.PackConfig")` + `PersistKeysToAzureBlobStorage` on a single blob (`pack-config/dp-keys/keys.xml`), same for every replica in both connection-string and managed-identity modes; container ensure-created before first use. Non-hosted runs get per-process ephemeral rings by design (test isolation). The defect is not sharing but at-rest protection (SEC-200).

## Findings

### ARCH-200: Kernel gRPC gateway is dead server-side with three-way proto/stub drift
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Kernel/Protos/digitalbrain.proto:7-22` (service definition); `src/DigitalBrain.Kernel/DigitalBrain.Kernel.csproj:73` (`GrpcServices="Both"`); no `MapGrpcService` in `src/DigitalBrain.Kernel/**`; `tests/DigitalBrain.Tests/Runtime/KernelCompositionTests.cs:106` asserts `digitalbrain.DigitalBrainGateway` is NOT in the endpoint graph; `app/lib/grpc/digitalbrain.pbgrpc.dart:116-165` contains checked-in client stubs for rpcs (`SubmitPrompt`, `PushFlutterPerf`, `GetRfwLayout`, `AiHealth`) that exist in **neither** repo proto (FACT).
- **Current behavior**: the proto is compiled into server+client C# every build, `AddGrpc()`/`UseGrpcWeb`/gRPC CORS headers/h2c ports are configured in the kernel, but no gateway service is ever served; the Flutter tree carries generated stubs from a third, deleted proto revision.
- **Why it matters**: (INFERENCE) three divergent contract artifacts guarantee future confusion; the unimplemented `GetPackConfig` rpc documents an unauthenticated decrypted-secret feed as an intended contract; build time and DI weight are spent on nothing.
- **OS/product consequence**: violates "one governed contract per boundary"; the dead `SynapseEnvelope`/`Fire` design is the pre-rail arbitrary-injection model the v2 gateway explicitly replaced.
- **Recommendation**: (PROPOSAL) delete `digitalbrain.proto`, the `<Protobuf>` item, `AddGrpc`/`UseGrpcWeb`/gRPC CORS/h2c port wiring in the kernel, the kernel "grpc" endpoint in `WireKernelSilo`, and `app/lib/grpc/digitalbrain.pb*.dart`.
- **Deletion/simplification opportunity**: yes — entire contract + plumbing.
- **Dependencies**: CLEAN-200 (dead components built for this gateway); PROD-200.
- **Tests/measurements required**: `dotnet test` from root; KernelCompositionTests keeps guarding; Flutter build after stub removal.
- **Effort**: S
- **Migration/rollback concern**: none — nothing serves or calls it.

### ARCH-201: Google/Salesforce provider concerns hard-wired into kernel hosting and OAuth endpoints
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Kernel/Hosting/DigitalBrainOrleansExtensions.cs:273-292` (provider seeders, factories, keyed `IConnector` construction with concrete `SalesforceConnector`/`GoogleConnector`); `src/DigitalBrain.Kernel/Hosting/DigitalBrainAppEndpoints.cs:31-48,65-96` (per-provider grain interfaces and URL validators branched by string) (FACT).
- **Current behavior**: adding a third connector requires editing kernel composition and the OAuth endpoint body in at least four places.
- **Why it matters**: (INFERENCE) contradicts the stated connector/capability model ("provider concerns must not leak into the kernel"); each new provider multiplies branchy endpoint code that must re-implement the state-unprotect/redirect-validate pattern correctly.
- **OS/product consequence**: weakens the general connector OS primitive; kernel becomes provider-aware.
- **Recommendation**: (PROPOSAL) introduce a provider-keyed registry (provider id → begin/complete authorization grain + URL validator) consumed by one generic endpoint body; connectors self-register.
- **Deletion/simplification opportunity**: yes — collapses duplicated google/salesforce branches (the callback endpoint bodies are already near-identical).
- **Dependencies**: connectors subsystem audit.
- **Tests/measurements required**: existing OAuth connector security tests must pass unchanged; add a fake third provider registration test.
- **Effort**: M
- **Migration/rollback concern**: keep URL paths stable (`/oauth/start/{provider}`).

### ARCH-202: Orphaned legacy session authority (UserSessionNeuron/DevAuth) parallel to the v2 session gate
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Kernel/Auth/UserSessionNeuron.cs:12` registered grain; repo-wide search shows `IUserSessionNeuron`/`GetSessionAsync`/`BuildLoginSurfaceAsync` referenced only by tests and by the sample login surface (`src/DigitalBrain.Ui.Runtime/UiSurfaceRuntime.cs:147`); the live session system is `BootstrapSession`/`RefreshSession` in `src/DigitalBrain.Mcp/Protos/ui.proto:8-9` (FACT).
- **Current behavior**: two session models exist; the journal-scan one is unreachable from any production transport but remains an activatable grain with its own users, roles, and 12 h sessions, plus `GetSessionByClientIdAsync` keyed on a client-chosen id defaulting to `"flutter"` for every caller (`UserSessionNeuron.cs:26,98-106`).
- **Why it matters**: (INFERENCE) duplicated authority over "who is signed in" is exactly the kind of dormant bypass the prior "Ino auth-gate bypass" fix removed; any future code that resolves the singleton grain re-opens a weaker login path (see SEC-201/202/203); clientId-keyed lookup would confuse sessions across users.
- **OS/product consequence**: breaks single-authority auth boundary of the OS model.
- **Recommendation**: (PROPOSAL) delete `Auth/DevAuth.cs`, `Auth/UserSessionNeuron.cs`, their contracts in `DigitalBrain.Ui.Contracts`, the sample login surface, and their tests; the v2 gate is the only session authority.
- **Deletion/simplification opportunity**: yes — ~450 lines + contracts + tests.
- **Dependencies**: SEC-201, SEC-202, SEC-203, REL-200; ui-runtime subsystem (login surface samples).
- **Tests/measurements required**: root test run; verify no Flutter flow submits `LoginRequest`.
- **Effort**: S-M
- **Migration/rollback concern**: none in production (no callers); dev flows using the sample login shell must move to the v2 bootstrap path.

### PROD-200: Kernel exposes an external h2c "grpc" endpoint that serves nothing but the SPA fallback
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Aspire/DigitalBrainBuilderExtensions.cs:200-207` (`WithEndpoint("grpc", env: "ASPNETCORE_HTTP_PORTS")` + `WithExternalHttpEndpoints()`); `src/DigitalBrain.Kernel/Hosting/DigitalBrainOrleansExtensions.cs:337-345` (HTTP/2-only listener), `:316-321` (`MapFallback` serves `index.html` for any unmatched path) (FACT).
- **Current behavior**: an externally-declared HTTP/2-cleartext port answers every request with the web bundle's `index.html` (or 404s without a bundle); no gRPC service exists behind it.
- **Why it matters**: (INFERENCE) unnecessary exposed surface, misleading topology (operators see a "grpc" endpoint), and the fallback responds 200 text/html to arbitrary paths on it.
- **OS/product consequence**: noise at the trust boundary; complicates ingress reasoning.
- **Recommendation**: (PROPOSAL) remove the endpoint with ARCH-200; keep only the "web" endpoint.
- **Deletion/simplification opportunity**: yes.
- **Dependencies**: ARCH-200.
- **Tests/measurements required**: `aspire run` + `list_resources` shows only web endpoint; app still loads.
- **Effort**: S
- **Migration/rollback concern**: none.

### SEC-200: DataProtection key ring stored unencrypted in the same blob container as the secrets it protects
- **Severity**: High
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Kernel/Config/PackConfigServices.cs:19-31` — `PersistKeysToAzureBlobStorage(container("pack-config").GetBlobClient("dp-keys/keys.xml"))` with **no** `ProtectKeysWith*`; pack-config ciphertext lives in the same `pack-config` container (`AzureBlobPackConfigBackingStore.cs:19`) (FACT). Microsoft docs state that specifying an explicit key persistence location deregisters default key encryption at rest and recommend an explicit mechanism for production (Key encryption at rest, learn.microsoft.com, verified 2026-07-13) (FACT).
- **Current behavior**: anyone with read access to the storage account/container (connection string in Aspire mode, or blob-read RBAC in managed-identity mode) obtains the master key ring **and** the ciphertext, i.e. plaintext connector credentials, OAuth state keys, and every pack-config value. The runtime-state envelope encryption (grain state/journal) is NOT affected — its KEKs come from configuration, not blob.
- **Why it matters**: (INFERENCE) the encryption of connector credentials currently adds ~zero protection beyond the blob ACL itself; a leaked storage connection string is a full credential compromise.
- **OS/product consequence**: breaks the least-privilege/at-rest-protection promise for connector auth material — the exact material the "on-demand, revocable" auth model depends on.
- **Recommendation**: (PROPOSAL) `ProtectKeysWithAzureKeyVault(keyId, credential)` in hosted mode (Key Vault key + managed identity), or at minimum wrap the key ring with a KEK from `DigitalBrain:Runtime:State` (an `IXmlEncryptor` backed by the existing `RuntimeStateKeyRing`); also move `dp-keys/` to a separate container with a tighter ACL.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: runtime-state key ring (RuntimeStateHosting); deployment/bicep for Key Vault.
- **Tests/measurements required**: integration test asserting `keys.xml` contains `<encryptedSecret>` (encrypted descriptor) rather than plaintext `<masterKey>`; rotation test across replicas.
- **Effort**: M
- **Migration/rollback concern**: existing values must remain decryptable — keep the old key ring readable during transition (DataProtection handles retired keys automatically once re-wrapped).

### SEC-201: Plaintext password persisted into the durable synapse journal by the legacy login path
- **Severity**: Medium (latent — no production caller today)
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Core/Synapse.cs:72-76` (`LoginRequest(Username, Password, ClientId)` is a `Synapse`); `src/DigitalBrain.Kernel.Abstractions/Neuron.cs:52` — every delivered synapse is appended to the durable `IncomingJournal`; `UserSessionNeuron.HandleAsync(LoginRequest)` consumes it as a delivered synapse (FACT).
- **Current behavior**: if the login path is ever exercised against a durable-journal silo, the raw password is retained forever in the replayable journal (encrypted at rest via the runtime-state converter, but recoverable by anyone who can replay the journal).
- **Why it matters**: (INFERENCE) journals are designed to be replayed, exported, and debugged; credentials must never enter them even encrypted.
- **OS/product consequence**: violates the journal-as-safe-audit-log primitive.
- **Recommendation**: (PROPOSAL) delete the path (ARCH-202). If any credential-bearing synapse ever becomes necessary, add a redaction contract (e.g. `IRedactBeforeJournal`) enforced in the Neuron base before journaling.
- **Deletion/simplification opportunity**: yes (with ARCH-202).
- **Dependencies**: ARCH-202; kernel-abstractions subsystem (journal write path).
- **Tests/measurements required**: architecture test: no journaled synapse type carries a field named like password/secret/token in plaintext.
- **Effort**: S (as deletion)
- **Migration/rollback concern**: existing dev journals may already contain passwords; purge on migration.

### SEC-202: First-user provisioning is fail-open — first login creates an admin account with arbitrary credentials
- **Severity**: Medium (latent — unreachable from production transports today)
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Kernel/Auth/UserSessionNeuron.cs:48-58,308-312` — unknown user + `AllowFirstUserProvisioning` (config default **true**) + empty user list ⇒ `CreateLocalUser` with roles `["admin","user"]` (FACT).
- **Current behavior**: on a fresh journal, the first `LoginRequest` from anyone becomes the admin.
- **Why it matters**: (INFERENCE) an auth surface whose default is "first stranger wins" is fail-open by construction; combined with ARCH-202's dormancy it is a booby trap for whoever rewires login.
- **OS/product consequence**: violates "fail-closed at every authorization boundary".
- **Recommendation**: (PROPOSAL) delete with ARCH-202; any future first-user flow must require an out-of-band bootstrap secret (as the Mcp bootstrap path already does).
- **Deletion/simplification opportunity**: yes.
- **Dependencies**: ARCH-202.
- **Tests/measurements required**: n/a after deletion.
- **Effort**: S
- **Migration/rollback concern**: none.

### SEC-203: Seeded `admin`/`admin` credentials can be enabled in Production by one config flag
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Kernel/Auth/DevAuth.cs:8-9,18` — `configuration?.GetValue("DigitalBrain:Auth:DevAutoLogin", isDevelopment)`: the flag overrides the environment check in any environment; `UserSessionNeuron.cs:43,59` — dev credentials also bypass the password of an existing account (FACT).
- **Current behavior**: `DigitalBrain:Auth:DevAutoLogin=true` in Production would make `admin`/`admin` authenticate (path currently dormant).
- **Why it matters**: (INFERENCE) config-reachable credential backdoors survive environment promotion mistakes; the "off in production" comment is only true for the default.
- **OS/product consequence**: latent bypass of the auth boundary.
- **Recommendation**: (PROPOSAL) delete with ARCH-202; if kept for dev, hard-gate on `IsDevelopment()` with no config override.
- **Deletion/simplification opportunity**: yes.
- **Dependencies**: ARCH-202.
- **Tests/measurements required**: test asserting the flag is inert when environment is Production.
- **Effort**: S
- **Migration/rollback concern**: none.

### SEC-204: `TrustAzureContainerAppsIngress` clears all forwarded-header validation; OAuth HTTPS gate becomes spoofable if the pod is directly reachable
- **Severity**: Low
- **Confidence**: Medium
- **Evidence**: `src/DigitalBrain.Kernel/Program.cs:15-22` — Production + flag ⇒ `KnownIPNetworks.Clear(); KnownProxies.Clear()` with `ForwardLimit=1`; `OAuthTransportBoundary.cs:29-33` relies on `context.Request.IsHttps`, which `UseForwardedHeaders` sets from client-supplied `X-Forwarded-Proto` once validation is cleared (FACT).
- **Current behavior**: any caller that can reach the container without traversing ACA ingress can assert `X-Forwarded-Proto: https` and bypass the 426 upgrade gate (and forge client IPs in logs).
- **Why it matters**: (INFERENCE) Microsoft does document clearing these lists for ACA, and ingress normally strips inbound `X-Forwarded-*`; the residual risk is direct east-west traffic inside the environment. Low, but worth stating because the OAuth boundary's HTTPS check silently depends on it.
- **OS/product consequence**: weakens a transport-layer guarantee at the connector-auth boundary.
- **Recommendation**: (PROPOSAL) keep the flag but document the assumption; optionally restrict the kernel's ACA ingress to external-only and reject requests lacking the ACA client-certificate/header fingerprint.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: deployment topology (out of repo).
- **Tests/measurements required**: in-environment probe: direct pod call with forged header must not pass the HTTPS gate (or be network-impossible).
- **Effort**: S
- **Migration/rollback concern**: none.

### SEC-205: OAuth state tokens are replayable within their lifetime (nonce generated but never checked)
- **Severity**: Note
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Kernel/Config/DataProtectionOAuthStateProtector.cs:31-33` — a 32-byte nonce is embedded in the payload; `TryUnprotect` (36-59) validates presence only; no consumed-nonce store exists (FACT).
- **Current behavior**: the same protected state can be presented multiple times for up to 10 minutes; single-use semantics depend entirely on grain-side pending-authorization state (`no-pending`/`state-mismatch` handling at `DigitalBrainAppEndpoints.cs:104`).
- **Why it matters**: (INFERENCE) defense-in-depth gap only — the grain-side check appears to close it; but the nonce is currently dead weight implying a guarantee it doesn't deliver.
- **OS/product consequence**: none today; misleads future maintainers.
- **Recommendation**: (PROPOSAL) either enforce single-use (short-TTL consumed-nonce cache) or delete the nonce field and document that the grain is the single-use authority.
- **Deletion/simplification opportunity**: yes (nonce field) or small addition.
- **Dependencies**: connector grains (pending-flow state).
- **Tests/measurements required**: replay test: second callback with the same state must fail.
- **Effort**: S
- **Migration/rollback concern**: v3 purpose string already versions the format; bump to v4 if payload changes.

### REL-200: UserSessionNeuron journal grows unbounded; every session/user lookup is a full-journal scan
- **Severity**: Medium (latent)
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Kernel/Auth/UserSessionNeuron.cs:230-234` (`SessionJournal()` concat + `DistinctBy` + `ToList` per call), `:293-297` (users re-grouped per login), no compaction or expiry pruning anywhere (FACT).
- **Current behavior**: logins, sessions, surfaces, and failures accumulate forever in one singleton grain; cost of every auth operation grows linearly with history.
- **Why it matters**: (INFERENCE) singleton + unbounded + O(n) per call is a slow-burn outage pattern.
- **OS/product consequence**: session authority becomes the cluster's first bottleneck if revived.
- **Recommendation**: (PROPOSAL) delete with ARCH-202; the v2 session store (dedicated `Sessions` storage provider) is the model to keep.
- **Deletion/simplification opportunity**: yes.
- **Dependencies**: ARCH-202.
- **Tests/measurements required**: n/a after deletion.
- **Effort**: S
- **Migration/rollback concern**: none.

### REL-201: PackConfigStore silently drops undecryptable values and rewrites whole dictionaries — transient decrypt failure can become permanent credential loss
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Kernel/Config/PackConfigStore.cs:49-60` (skip-on-`CryptographicException` with warning), `:22-31` (`SetAsync` serializes the full dictionary; no ETag/concurrency token on `SaveAsync`, `AzureBlobPackConfigBackingStore.cs:71-72` uploads `overwrite: true`) (FACT).
- **Current behavior**: a value sealed under a missing key vanishes from `GetAsync`; a caller doing read-modify-write then persists the dictionary **without** it, deleting the ciphertext that might have become decryptable again (e.g. key ring propagation lag on a new replica). Concurrent writers last-write-win.
- **Why it matters**: (INFERENCE) the deliberate degrade-to-unconfigured design (documented in the comment) is right for reads but unsafe when combined with full-blob writes — partial-read + full-write = data loss amplifier for connector credentials.
- **OS/product consequence**: connector auth material can silently disappear, forcing user re-consent — breaks "recoverable" mutations.
- **Recommendation**: (PROPOSAL) carry undecryptable entries through opaquely on write (keep original ciphertext for keys the caller didn't change), and add ETag-conditional upload (`overwrite: false` + If-Match) to the blob backing store.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: AzureBlobPackConfigBackingStore; connector write paths.
- **Tests/measurements required**: test: value undecryptable during Get, unrelated key updated via Set ⇒ original ciphertext still present in the blob; concurrent-writer ETag conflict test.
- **Effort**: M
- **Migration/rollback concern**: none (format unchanged).

### REL-202: Vacuous health checks report Healthy without probing anything
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Kernel/Hosting/DigitalBrainOrleansExtensions.cs:288-292` (`google-connector`/`salesforce-connector` are static `Healthy("... is registered")`); `RuntimeStateHosting.cs:144-156` (`RuntimeStateHealthCheck` returns static metadata, never touches storage) (FACT).
- **Current behavior**: health endpoints assert liveness of things that were merely registered at startup.
- **Why it matters**: (INFERENCE) false-green health masks real storage/key failures from Aspire/ACA probes; worse than no check because it earns unwarranted trust.
- **OS/product consequence**: degraded observability at the ops boundary.
- **Recommendation**: (PROPOSAL) delete the two connector checks; make the runtime-state check perform a cheap real probe (e.g. seal/unseal a canary envelope, HEAD the DP key blob) or rename it to `runtime-state-config`.
- **Deletion/simplification opportunity**: yes (two checks).
- **Dependencies**: none.
- **Tests/measurements required**: health endpoint reflects an induced storage failure.
- **Effort**: S
- **Migration/rollback concern**: none.

### REL-203: OAuth boundary rate limit is a single global fixed window per replica
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Kernel/Hosting/OAuthTransportBoundary.cs:9,74-88` — one `_windowCount` for all clients, fixed-window reset (FACT).
- **Current behavior**: 120 requests/minute total; one hostile or buggy client exhausts the budget and every legitimate user's OAuth consent flow 429s; fixed window also allows 2x burst at boundaries.
- **Why it matters**: (INFERENCE) turns a per-abuser control into a shared-fate denial of the connector-auth journey.
- **OS/product consequence**: connector connect/repair flows (a core user journey) become externally starvable.
- **Recommendation**: (PROPOSAL) partition by client IP (post-forwarded-headers) using `System.Threading.RateLimiting` partitioned limiter; keep the global cap as a secondary ceiling.
- **Deletion/simplification opportunity**: yes — replace hand-rolled window with framework `RateLimiter`.
- **Dependencies**: SEC-204 (client IP trustworthiness).
- **Tests/measurements required**: two-client test: client A at limit must not 429 client B.
- **Effort**: S
- **Migration/rollback concern**: none.

### REL-204: TabularDataParser misaligns columns when the header row contains blank cells
- **Severity**: Low (dead path)
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Kernel/TabularData/TabularDataParser.cs:42-49` — headers from `CellsUsed()` (blank cells skipped) but data read positionally `row.Cell(1..headerCount)` (FACT).
- **Current behavior**: a blank header in column C shifts every subsequent header left while data stays positional; stats and UI attribute values to the wrong columns.
- **Why it matters**: (INFERENCE) silently wrong data shown to the user/LLM is worse than an error.
- **OS/product consequence**: correctness of the (currently unwired) upload-to-visualization journey.
- **Recommendation**: (PROPOSAL) if kept: read the header row positionally over the used range width; else delete with CLEAN-200.
- **Deletion/simplification opportunity**: yes (delete with upload path).
- **Dependencies**: CLEAN-200, PERF-200.
- **Tests/measurements required**: xlsx with blank middle header ⇒ correct alignment.
- **Effort**: S
- **Migration/rollback concern**: none.

### FRAME-200: Dual Orleans provider configuration — Aspire config-driven and manual explicit clients for the same providers
- **Severity**: Medium
- **Confidence**: Medium
- **Evidence**: AppHost declares the full Orleans resource graph (`src/DigitalBrain.Aspire/DigitalBrainBuilderExtensions.cs:89-95`) and references it into the kernel (`:192`), which per the Orleans/Aspire integration docs injects `Orleans:*` provider configuration consumed automatically by `UseOrleans` ("Aspire injects Orleans configuration ... via environment variables that Orleans reads automatically", learn.microsoft.com Orleans-Aspire integration, verified 2026-07-13). The kernel registers the keyed clients those providers resolve (`DigitalBrainOrleansExtensions.cs:229-240`) **and** manually configures clustering/reminders/5 storage providers with hand-built `TableServiceClient`/`BlobServiceClient` inside the `UseOrleans` delegate (`:114-148`). It additionally registers an unkeyed `AddAzureBlobServiceClient("grainstate", ...)` (`:242-246`) that its own code comment elsewhere (`PackConfigServices.cs:34-39`) says resolves as a null sentinel and must not be used (FACT).
- **Current behavior**: two authorities configure the same named providers; the delegate's explicit clients (tracing disabled, no Aspire health checks) win by later registration; in the managed-identity branch the keyed clients are *not* registered while Aspire still injects `Orleans:*:ServiceKey` config — the docs state Orleans "will throw a dependency resolution error at runtime" when a config-referenced keyed resource is missing, so that branch may only work because deployment config diverges from the AppHost model (unverified).
- **Why it matters**: (INFERENCE) ambiguous configuration authority makes every storage change a two-place edit and hides which client actually serves traffic; the managed-identity path has an unproven failure mode.
- **OS/product consequence**: hosting substrate reliability and debuggability.
- **Recommendation**: (PROPOSAL) pick one authority: either pure Aspire (keyed clients + parameterless `UseOrleans`, delegate only for journaling/DI extras) or pure manual (drop the keyed registrations and the AppHost `AddOrleans` reference). Delete the unkeyed `grainstate` client either way. Verify the managed-identity deployment boots against the published manifest.
- **Deletion/simplification opportunity**: yes — one of the two configuration stacks (~40 lines).
- **Dependencies**: ARCH-200 cleanup touches the same method; aspire subsystem audit.
- **Tests/measurements required**: `aspire run` + doctor in both modes; a silo boot test with `DigitalBrain:Storage:AccountName` set and AppHost-injected Orleans config present.
- **Effort**: M
- **Migration/rollback concern**: cluster membership table/container names must remain identical across the switch.

### FRAME-201: Alpha journaling APIs (ORLEANSEXP005) with no public documentation — recorded gap
- **Severity**: Note
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Kernel/Hosting/PrototypeJournals.cs:4` (pragma suppression); `DigitalBrainOrleansExtensions.cs:128,144-157` (`AddAzureBlobJournalStorage`, `UseJsonJournalFormat`) from `Microsoft.Orleans.Journaling` 10.2.1-preview.1.alpha.1 (FACT). Context7/Microsoft Learn have no material for this alpha package (documentation gap — not invented).
- **Current behavior**: the durable journal — the backbone of the self-evolution rail — rests on an alpha API surface whose semantics (compaction, replay ordering, failure modes) are verifiable only from source.
- **Why it matters**: (INFERENCE) upgrade risk: alpha APIs break without migration notes; nobody can validate usage against docs.
- **OS/product consequence**: replayability guarantees rest on unversioned behavior.
- **Recommendation**: (PROPOSAL) pin + record the exact package commit; add a journal round-trip/replay integration test as executable documentation; re-check on every Orleans bump.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: kernel-abstractions (journal consumers).
- **Tests/measurements required**: replay determinism test across silo restart.
- **Effort**: S
- **Migration/rollback concern**: journal format stability across package updates.

### PERF-200: TabularDataParser computes stats over the entire workbook with no row/cell cap
- **Severity**: Low (dead path)
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Kernel/TabularData/TabularDataParser.cs:45-53` — all data rows materialized as `List<List<string>>` before stats; only `Rows` is capped at 50 (FACT).
- **Current behavior**: a small xlsx that decompresses to millions of cells allocates them all as strings in memory.
- **Why it matters**: (INFERENCE) uploads are attacker-controlled input; xlsx is a zip (decompression-bomb-friendly).
- **OS/product consequence**: kernel-process memory DoS if the upload path is ever wired.
- **Recommendation**: (PROPOSAL) delete with the upload path (CLEAN-200), or add hard caps (max rows/columns/total cells) and stream stats without materializing rows.
- **Deletion/simplification opportunity**: yes.
- **Dependencies**: CLEAN-200, REL-204.
- **Tests/measurements required**: oversized-sheet test rejects within bound.
- **Effort**: S
- **Migration/rollback concern**: none.

### CLEAN-200: Nine dead components — the pre-v2 gateway stratum has no production callers
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: repo-wide reference search (FACT): `Sync/SyncManifest.cs` — zero references; `Gateway/IngressNeuron.cs` — no `GetGrain<IIngressNeuron>` anywhere; `Ui/SignalEgressBus.cs` — registered only in `tests/DigitalBrain.TestKit/NeuronTestKernelConfigurator.cs:49`, `Subscribe` never called; `Ui/SignalEgressStreamSubscriber.cs` — `AddSignalEgressStreamSubscriber` never invoked; `Ui/ChatNeuron.cs` — no `GetGrain<IChatNeuron>` in src; `TabularData/TabularDataParser.cs`, `Uploads/ChatUploadClassifier.cs` — test-only callers; `Db/SqliteSchemaInspector.cs` — DI-registered (`DigitalBrainOrleansExtensions.cs:201`) but never resolved; `Auth/*` — test-only (ARCH-202); plus `Protos/digitalbrain.proto` (ARCH-200).
- **Current behavior**: ~1,100 lines of shippable-looking code compile, register services, and pass tests while serving no user journey.
- **Why it matters**: (INFERENCE) every audit, refactor, and onboarding pays a reading tax; dormant grains (Ingress, UserSession, Chat) remain activatable attack/bug surface inside the silo.
- **OS/product consequence**: directly contradicts the repo's own delete-first WoW; blurs which components embody the OS model.
- **Recommendation**: (PROPOSAL) delete all nine (with their tests and TestKit registrations) in one commit; if tabular upload or DB inspection are near-term roadmap, park them in a PR/branch instead of main.
- **Deletion/simplification opportunity**: yes — the core of this audit; >10% net reduction of the subsystem easily met.
- **Dependencies**: ARCH-200, ARCH-202, TEST-200.
- **Tests/measurements required**: root `dotnet test` green after deletion; KernelCompositionTests unchanged.
- **Effort**: S-M
- **Migration/rollback concern**: none — no production callers by construction.

### CLEAN-201: PrototypeJournals types live in the global namespace
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Kernel/Hosting/PrototypeJournals.cs:1-34` — no `namespace` declaration; `InMemoryJournalForPrototype<T>` is `public` (FACT).
- **Current behavior**: global-namespace public types leak into every compilation unit referencing the assembly.
- **Why it matters**: (INFERENCE) collision-prone, ungreppable-by-namespace, signals unfinished extraction.
- **OS/product consequence**: none functional.
- **Recommendation**: (PROPOSAL) move into `DigitalBrain.Kernel.Hosting`, make `internal` where possible (TestKit may need `InternalsVisibleTo` or keep the list type public).
- **Deletion/simplification opportunity**: no.
- **Dependencies**: TestKit usage.
- **Tests/measurements required**: build.
- **Effort**: S
- **Migration/rollback concern**: none.

### CLEAN-202: Vacuous overrides and duplicated blocks
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `UserSessionNeuron.cs:18-21` (`OnActivateAsync` only calls base); `DigitalBrainOrleansExtensions.cs:305-321` (two consecutive `if (serveWebBundle)` blocks); `Generated/GeneratedPackRuntime.cs:42-50` (`Ensure` ignores its `journal` parameter, logs and returns) (FACT).
- **Current behavior**: noise that implies behavior which doesn't exist.
- **Why it matters**: (INFERENCE) misleads readers into hunting for activation logic / journal-based re-embodiment that isn't there.
- **OS/product consequence**: none.
- **Recommendation**: (PROPOSAL) delete the override, merge the blocks, remove `Ensure` or implement journal-based re-embodiment for real.
- **Deletion/simplification opportunity**: yes.
- **Dependencies**: CLEAN-200 (UserSessionNeuron may be deleted wholesale).
- **Tests/measurements required**: build + tests.
- **Effort**: S
- **Migration/rollback concern**: none.

### CLEAN-203: Hand-written code under `Generated/`
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Kernel/Generated/GeneratedPackRuntime.cs` — authored design comments, no generator banner; it is runtime infrastructure *for* generated packs, not generated itself (FACT).
- **Current behavior**: audit/CI conventions that exclude `Generated/**` skip real code; "clean generated output" habits endanger it.
- **Why it matters**: (INFERENCE) misclassification risk both ways.
- **OS/product consequence**: review coverage of the pack-embodiment path (a self-evolution primitive) can silently lapse.
- **Recommendation**: (PROPOSAL) move to `Foundry/` or `Runtime/` (e.g. `GeneratedPackRuntime` next to `PackAlcEmbodier`).
- **Deletion/simplification opportunity**: no (relocation).
- **Dependencies**: kernel-foundry subsystem.
- **Tests/measurements required**: build.
- **Effort**: S
- **Migration/rollback concern**: none.

### CLEAN-204: Duplicated Aspire-detection logic reading raw environment variables
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Kernel/Hosting/DigitalBrainHostEnvironment.cs:7-18` — checks `ConnectionStrings__*` via `Environment.GetEnvironmentVariable` **and** the same keys via `IConfiguration.GetConnectionString` (FACT).
- **Current behavior**: two detection paths for the same fact; the raw-env path bypasses config providers (user-secrets, appsettings) and can disagree with the config path.
- **Why it matters**: (INFERENCE) mode-selection ambiguity is how "dev shortcut in prod" bugs are born.
- **OS/product consequence**: hosting-mode determinism.
- **Recommendation**: (PROPOSAL) keep only the `IConfiguration` checks (env vars flow through the env config provider anyway).
- **Deletion/simplification opportunity**: yes.
- **Dependencies**: callers in Kernel + Mcp.
- **Tests/measurements required**: mode-selection unit test for env-var-only and config-only cases.
- **Effort**: S
- **Migration/rollback concern**: none.

### TEST-200: Tests lock in dead components, signaling false liveness
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `tests/DigitalBrain.Tests/Uploads/ChatUploadClassifierTests.cs`, `tests/DigitalBrain.Tests/TabularData/TabularDataParserTests.cs`, `tests/DigitalBrain.Tests/Db/SqliteSchemaInspectorTests.cs`, `tests/DigitalBrain.Tests/Auth/UserSessionNeuron*Tests.cs`, TestKit registrations of `SignalEgressBus`/`SqliteSchemaInspector` — all exercising code with zero production callers (FACT).
- **Current behavior**: green tests certify components no user journey reaches; deletion (CLEAN-200) will *look* like coverage loss.
- **Why it matters**: (INFERENCE) test suites should mirror the product; these are maintenance anchors keeping dead code alive.
- **OS/product consequence**: distorts the "what is real" signal the audit/refinement effort depends on.
- **Recommendation**: (PROPOSAL) delete alongside CLEAN-200; keep `AzureBlobPackConfigBackingStoreTests`, `PackConfigStoreTests`, `OAuthStateProtectorTests`, `KernelCompositionTests` (these guard live paths).
- **Deletion/simplification opportunity**: yes.
- **Dependencies**: CLEAN-200, ARCH-202.
- **Tests/measurements required**: root test run stays green.
- **Effort**: S
- **Migration/rollback concern**: none.

### TEST-201: No tests for the hosting composition's riskiest behaviors
- **Severity**: Low
- **Confidence**: Medium
- **Evidence**: no test covers `ConfigureDigitalBrainKestrel` port parsing (`DigitalBrainOrleansExtensions.cs:330-357`), the managed-identity Orleans branch, DP key-ring sharing/protection (`PackConfigServices`), or OAuthTransportBoundary rate/timeout behavior (searched `tests/` — `KernelCompositionTests`, `PackConfigBackingStoreSelectionTests`, `OAuthConnectorSecurityTests` exist but do not exercise these) (FACT for absence within searched scope).
- **Current behavior**: mode branching (the code most likely to differ between dev and prod) is verified only by running the app.
- **Why it matters**: (INFERENCE) exactly where "works locally, breaks deployed" defects live (see FRAME-200 managed-identity concern).
- **OS/product consequence**: deployment confidence.
- **Recommendation**: (PROPOSAL) add composition tests: env-matrix Kestrel port selection; DP `keys.xml` protected-at-rest assertion (after SEC-200); boundary tests for 429/504 paths.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: SEC-200, FRAME-200.
- **Tests/measurements required**: the tests are the measurement.
- **Effort**: M
- **Migration/rollback concern**: none.

---

## Second-pass corroborating audit (merged from redundant parallel audit `kernel-b.md`)

A redundant parallel audit independently reviewed the same files and is folded in here so all findings live in one subsystem document. Its findings use a different ID block; they are reconciled into the canonical findings register. Where it agrees with the primary audit above, treat as corroboration; where it adds new findings, they are additive.

## Findings

### SEC-100: Config flag enables admin/admin dev credentials in any environment and bypasses existing-account passwords
- **Severity**: High
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Kernel/Auth/DevAuth.cs:11-19` — `Enabled` returns `configuration.GetValue("DigitalBrain:Auth:DevAutoLogin", isDevelopment)`; `src/DigitalBrain.Kernel/Auth/UserSessionNeuron.cs:43,59` — when `isDevCredentials` is true the stored-password check (`VerifyPassword`) is skipped for an existing matching account. (FACT)
- **Current behavior**: In Development (or wherever `DigitalBrain:Auth:DevAutoLogin=true` is set, including Production), `admin`/`admin` authenticates unconditionally; if a real account named `admin` exists, its password is ignored; the login surface pre-fills these credentials (`UserSessionNeuron.cs:113-119`). (FACT)
- **Why it matters**: (INFERENCE) A single configuration value converts the primary login path to a fixed public credential; config channels (env vars, appsettings, Aspire parameters) are a much weaker trust boundary than code. This is a standing bypass switch, not a dev seam.
- **OS/product consequence**: Breaks "auth fail-closed at every boundary"; anyone with config influence owns every account on the deployment.
- **Recommendation**: (PROPOSAL) Make DevAuth compile-time or `IsDevelopment()`-only (remove the config override); never bypass the stored password of an already-provisioned account — dev creds should only auto-provision a dedicated dev user.
- **Deletion/simplification opportunity**: yes — delete the config override branch.
- **Dependencies**: SEC-101, ARCH-101.
- **Tests/measurements required**: test asserting `admin/admin` is rejected when environment is Production regardless of configuration; test asserting an existing `admin` account's password is always verified.
- **Effort**: S
- **Migration/rollback concern**: local dev flows keep working via environment detection.

### SEC-101: First-login user provisioning is fail-open by default and grants admin
- **Severity**: High
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Kernel/Auth/UserSessionNeuron.cs:50-57` — when no users exist, any `LoginRequest` creates the account; `:308-312` — `AllowFirstUserProvisioning` defaults `true`; `:318` — roles `["admin","user"]`. (FACT)
- **Current behavior**: On a fresh deployment (empty journal), the first credentials submitted through the login surface become a persistent admin account. (FACT)
- **Why it matters**: (INFERENCE) Fresh-install race: whoever reaches the login form first (any holder of the UI bootstrap secret, or anyone during a window where the transport is mis-secured) becomes the permanent administrator. Fail-open default at the identity-creation boundary contradicts the stated model.
- **OS/product consequence**: Tenant/root identity can be claimed by an unintended party; everything downstream (approvals in the self-evolution rail) inherits that identity.
- **Recommendation**: (PROPOSAL) Default `AllowFirstUserProvisioning` to false; require an explicit provisioning ceremony (setup token printed to operator console, or config-supplied initial admin hash).
- **Deletion/simplification opportunity**: no.
- **Dependencies**: SEC-100, ARCH-101.
- **Tests/measurements required**: test that first login on a Production-profile host with defaults is rejected.
- **Effort**: S-M
- **Migration/rollback concern**: existing local setups need the flag set true or a seeded user.

### SEC-102: DataProtection key ring persisted to blob without at-rest key encryption
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Kernel/Config/PackConfigServices.cs:19-31` — `AddDataProtection().SetApplicationName(...)` + `PersistKeysToAzureBlobStorage(container.GetBlobClient("dp-keys/keys.xml"))`, no `ProtectKeysWith*`. Microsoft docs (Data Protection configuration overview, verified this session): pointing the system at a specific key repository **disables automatic at-rest key encryption**. (FACT)
- **Current behavior**: The key ring that protects every pack-config secret and OAuth state token is stored as plaintext XML in the same `pack-config` container as the ciphertext it protects. (FACT)
- **Why it matters**: (INFERENCE) Blob-storage read access (SAS leak, misconfigured RBAC, backup exfiltration) yields both ciphertext and the keys — the per-value encryption in `PackConfigStore` adds nothing against that adversary.
- **OS/product consequence**: Connector OAuth tokens and API keys for all tenants compromised together.
- **Recommendation**: (PROPOSAL) Add `ProtectKeysWithAzureKeyVault` (managed identity path already exists) or at minimum move `dp-keys/` to a separately-ACLed container.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: REL-101 (key management story should be designed once).
- **Tests/measurements required**: composition test asserting an `IXmlEncryptor` is registered in hosted/Production profiles.
- **Effort**: S-M
- **Migration/rollback concern**: existing key ring must be re-protected or values re-encrypted; plan a dual-read window.

### SEC-103: Production HTTPS enforcement depends on spoofable forwarded headers when ACA trust flag is on
- **Severity**: Low
- **Confidence**: Medium
- **Evidence**: `src/DigitalBrain.Kernel/Program.cs:15-22` — Production + `TrustAzureContainerAppsIngress=true` clears `KnownIPNetworks`/`KnownProxies` (ForwardLimit 1); `src/DigitalBrain.Kernel/Hosting/OAuthTransportBoundary.cs:29-33` — 426 gate uses `context.Request.IsHttps`. (FACT)
- **Current behavior**: With the flag on, any peer that can reach the container directly can assert `X-Forwarded-Proto: https` and pass the HTTPS gate. This mirrors Microsoft's documented Container Apps guidance (ingress is assumed to be the only network path) and the flag defaults off. (FACT)
- **Why it matters**: (INFERENCE) Defense-in-depth gap only if the internal network assumption breaks (sidecar compromise, misconfigured ingress restriction).
- **OS/product consequence**: OAuth legs could transit plaintext inside the environment without detection.
- **Recommendation**: (PROPOSAL) Keep, but document the assumption next to the flag; consider requiring the ACA ingress client certificate header when available.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: none.
- **Tests/measurements required**: none beyond documentation.
- **Effort**: S
- **Migration/rollback concern**: none.

### SEC-104: PBKDF2 iteration count below current guidance; no password policy
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Kernel/Auth/UserSessionNeuron.cs:14` — 100_000 iterations, PBKDF2-HMAC-SHA256; no minimum length/complexity check anywhere in `HandleAsync(LoginRequest)`. (FACT)
- **Current behavior**: Any non-empty password is accepted at provisioning; hashes use 100k iterations (OWASP 2023+ guidance: 600k for PBKDF2-HMAC-SHA256). (FACT)
- **Why it matters**: (INFERENCE) Weak/1-char admin passwords are permitted; offline cracking of a leaked journal is ~6x cheaper than guidance.
- **OS/product consequence**: Identity primitive weaker than intended for the root account.
- **Recommendation**: (PROPOSAL) Raise iterations (store per-record so it can evolve), add a minimal length requirement at provisioning.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: SEC-101.
- **Tests/measurements required**: provisioning-rejects-short-password test.
- **Effort**: S
- **Migration/rollback concern**: existing hashes keep verifying if iteration count is stored per record (currently it is a constant — needs a versioned record).

### SEC-105: IngressNeuron is an unauthenticated arbitrary-signal broadcast (currently unreachable)
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Kernel/Gateway/IngressNeuron.cs:9-10` — `IngestAsync(signalName, props)` → `Broadcast(new Signal(...))` with no validation; no production caller of `IIngressNeuron` exists (repo-wide search). (FACT)
- **Current behavior**: Dead grain; if activated by any future caller it injects unvalidated signals into the cluster-wide timeline. (FACT)
- **Why it matters**: (INFERENCE) Dead capability with dangerous semantics tends to get wired up "because it's there"; every timeline subscriber trusts broadcast signals.
- **OS/product consequence**: Would let an external transport forge internal synapse traffic.
- **Recommendation**: (PROPOSAL) Delete grain + `IIngressNeuron` (part of CLEAN-101 sweep); reintroduce only with caller identity + signal-name allowlist.
- **Deletion/simplification opportunity**: yes — delete.
- **Dependencies**: CLEAN-101.
- **Tests/measurements required**: none (deletion).
- **Effort**: S
- **Migration/rollback concern**: none.

### SEC-106: Session lookup by guessable clientId returns live session state
- **Severity**: Note
- **Confidence**: Medium
- **Evidence**: `src/DigitalBrain.Kernel/Auth/UserSessionNeuron.cs:98-106,253-265` — `GetSessionByClientIdAsync("flutter")` returns the newest active session for that client id; client ids default to `"flutter"` (`:26`). (FACT)
- **Current behavior**: Any grain-side caller that knows a client id string can resolve the associated user/roles/session id. (FACT)
- **Why it matters**: (INFERENCE) Treats a non-secret correlation id as a bearer credential; safety depends entirely on which surfaces can invoke the grain.
- **OS/product consequence**: Weak link if any transport ever proxies this method.
- **Recommendation**: (PROPOSAL) Key lookups by opaque session id only; carry clientId as metadata.
- **Deletion/simplification opportunity**: possibly (method removal if unused after ARCH-101).
- **Dependencies**: ARCH-101.
- **Tests/measurements required**: n/a.
- **Effort**: S
- **Migration/rollback concern**: connector session-scope resolution (Gmail/Salesforce) may rely on it — verify before removal.

### ARCH-100: digitalbrain.proto is a dead, divergent contract still generated on both sides
- **Severity**: High
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Kernel/Protos/digitalbrain.proto:7-22` (9 RPCs incl. `GetPackConfig` returning decrypted secrets with no identity field); `src/DigitalBrain.Kernel/DigitalBrain.Kernel.csproj:73` (`GrpcServices="Both"`); no `DigitalBrainGatewayBase` implementation repo-wide; `tests/DigitalBrain.Tests/Runtime/KernelCompositionTests.cs:106` asserts the service is NOT mapped; `app/lib/grpc/digitalbrain.pbgrpc.dart:116-165` contains RPCs (`AiHealth`, `SubmitPrompt`, `PushFlutterPerf`, `WatchVisualLoadHint`, `GetLatestCard`, `GetRfwLayout`) absent from the proto. (FACT)
- **Current behavior**: Kernel builds server+client C# stubs for a service it refuses to host; the Flutter app ships checked-in Dart stubs from an older proto revision; `docs/execution-plan.md` P1.22 already flags the cleanup. (FACT)
- **Why it matters**: (INFERENCE) Two stale generated contracts invite accidental revival (especially `GetPackConfig`, an unauthenticated secret-pull design) and mislead every reader about the real transport (v2 `DigitalBrainV2Ui` in Mcp).
- **OS/product consequence**: Contract ambiguity at the kernel's outermost trust boundary; dead secret-egress design lingering in the source of truth.
- **Recommendation**: (PROPOSAL) Delete the proto, the csproj `<Protobuf>` item, and `app/lib/grpc/digitalbrain.*`; migrate any still-used Dart message types to the v2 contract.
- **Deletion/simplification opportunity**: yes — proto + ~3k generated Dart LOC + kernel gRPC plumbing (CLEAN-102).
- **Dependencies**: CLEAN-102; flutter-app subsystem (stub deletion); mcp subsystem (v2 contract is the survivor).
- **Tests/measurements required**: solution + Flutter build green after deletion; `KernelCompositionTests` still green.
- **Effort**: M
- **Migration/rollback concern**: verify no Flutter code path still constructs `DigitalBrainGatewayClient` (`app/lib/shell/digitalbrain_client_scope.dart` does — must be removed with it).

### ARCH-101: Two parallel, unreconciled session/auth systems
- **Severity**: High
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Kernel/Auth/UserSessionNeuron.cs` (journal sessions, 12h, logout-by-id) vs `src/DigitalBrain.Mcp/RuntimeSessionAuthority.cs` + `UiGrpcService` (token sessions, 15-min access + refresh + revocation + per-stream revalidation). No code path links a `UserSessionCreated` to a runtime session or vice versa. (FACT)
- **Current behavior**: The transport authenticates with bootstrap-secret/OIDC tokens; the product "login" inside the SDUI creates an unrelated journal session; revoking one does not affect the other. (FACT)
- **Why it matters**: (INFERENCE) Duplicated authority is the classic bypass generator: authorization decisions made against whichever system a given surface happens to consult; lifetime/revocation semantics differ (12h vs 15min); the weaker system (SEC-100/101) defines user identity.
- **OS/product consequence**: "Revocable, tenant-isolated auth" cannot be reasoned about while two sources of truth exist.
- **Recommendation**: (PROPOSAL) Choose the Mcp token authority as the single system; reduce `UserSessionNeuron` to a credential-verification + user-registry service invoked by it (or fold user records into the session authority), and derive all UI identity from the validated runtime session.
- **Deletion/simplification opportunity**: yes — most of `UserSessionNeuron`'s session-resolution machinery.
- **Dependencies**: SEC-100, SEC-101, SEC-106, REL-100; mcp subsystem.
- **Tests/measurements required**: end-to-end test: revoking the runtime session invalidates everything the UI can do.
- **Effort**: L
- **Migration/rollback concern**: existing journaled sessions become inert; acceptable pre-GA.

### ARCH-102: Provider concerns hardcoded in kernel hosting
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Kernel/Hosting/DigitalBrainAppEndpoints.cs:31-48,65-96` — `IGmailReadToolGrain`/`ISalesforceReadToolGrain`, `GmailReadStatus`/`SalesforceReadStatus`, `GoogleClientFactory`/`SalesforceClientFactory` referenced directly; `DigitalBrainOrleansExtensions.cs:273-292` — provider seeders, factories, keyed connectors, per-provider health checks registered in kernel composition. (FACT)
- **Current behavior**: Adding a third connector requires editing kernel hosting files in ≥3 places. (FACT)
- **Why it matters**: (INFERENCE) Violates the stated connector/capability model ("provider concerns must not leak into the kernel"); each provider edit re-risks the OAuth boundary code.
- **OS/product consequence**: Kernel trust surface grows with every integration instead of staying fixed.
- **Recommendation**: (PROPOSAL) Introduce a provider-agnostic OAuth participant interface (begin/complete by provider key, resolving the keyed `IConnector` or a registry) and move registration into per-connector `IHostApplicationBuilder` extensions living in the integration projects.
- **Deletion/simplification opportunity**: yes — collapses duplicated per-provider branches in the callback endpoint.
- **Dependencies**: connectors subsystem; ARCH-100.
- **Tests/measurements required**: existing OAuth flow tests pass against the dispatcher; adding a fake provider requires zero kernel edits.
- **Effort**: M
- **Migration/rollback concern**: URL shapes (`/oauth/*/{provider}`) unchanged.

### ARCH-103: Login handler triggers Aspire distributed-app orchestration
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Kernel/Auth/UserSessionNeuron.cs:74` — `GetGrain<IAspireNeuron>("aspire-main").FireAsync(new StartDistributedApp("digitalbrain"))` on every successful login. (FACT)
- **Current behavior**: Authentication success fires a dev-orchestration command as "the product-surface startup path". (FACT)
- **Why it matters**: (INFERENCE) Auth grain now depends on a dev-tooling grain; every login re-fires a start command (idempotency burden pushed onto `IAspireNeuron`); in production this is at best a no-op with journal noise.
- **OS/product consequence**: Blurs the trusted-kernel boundary; login latency and failure modes coupled to orchestration.
- **Recommendation**: (PROPOSAL) Emit a `UserSignedIn` signal and let interested surfaces react; remove the direct `IAspireNeuron` call.
- **Deletion/simplification opportunity**: yes.
- **Dependencies**: ARCH-101.
- **Tests/measurements required**: login test asserting no orchestration synapse is fired.
- **Effort**: S
- **Migration/rollback concern**: verify nothing depends on login-triggered app start in the demo flow.

### REL-100: UserSessionNeuron derives all state by full-journal scans; unbounded growth
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Kernel/Auth/UserSessionNeuron.cs:230-234` — `SessionJournal()` concatenates both journals, `DistinctBy`, `ToList` on **every** `GetSessionAsync`/`GetSessionByClientIdAsync`/login; journal grows by ≥4 synapses per login forever with no compaction. (FACT)
- **Current behavior**: Session validation cost and memory grow linearly with lifetime login count; ended/expired sessions are never pruned. (FACT)
- **Why it matters**: (INFERENCE) Session validation sits on hot paths (connector scope resolution); a year of daily logins makes each check re-materialize thousands of records.
- **OS/product consequence**: Latency creep on every authenticated interaction; activation cost grows unboundedly on the durable path.
- **Recommendation**: (PROPOSAL) Maintain materialized dictionaries (users, active sessions) updated on write, rebuilt once at activation; add journal compaction/snapshot for superseded session events.
- **Deletion/simplification opportunity**: yes — several LINQ pipelines collapse.
- **Dependencies**: ARCH-101 (may obsolete this entirely).
- **Tests/measurements required**: benchmark GetSessionAsync at 10k journal entries before/after.
- **Effort**: M
- **Migration/rollback concern**: replay semantics must produce identical state.

### REL-101: Pack-config blob addressing breaks silently on signing-key rotation
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Kernel/Config/AzureBlobPackConfigBackingStore.cs:21-41,119-132` — blob names = HMAC(identifierKey, scope‖pack) where identifierKey = HMAC(SigningKey, purpose); `RuntimeStateHosting.cs` supports versioned KEKs but the **signing key is a single unversioned value**. (FACT)
- **Current behavior**: Rotating `DigitalBrain:Runtime:State:SigningKey` changes every entry's blob name; `LoadAsync` finds nothing, legacy migration only covers plaintext-named blobs, callers degrade to "not configured" — all connector tokens/config silently orphaned (blobs remain, unreachable). (FACT)
- **Why it matters**: (INFERENCE) Key rotation is a *routine security operation*; here it is indistinguishable from data loss, with no error, no health signal.
- **OS/product consequence**: "Revocable, recoverable auth" breaks: every tenant re-consents after rotation, old ciphertext lingers.
- **Recommendation**: (PROPOSAL) Version the identifier key like the KEKs (try current, fall back to previous versions on read + rewrite), or store a keyed manifest mapping HMAC names to a stable per-entry id.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: SEC-102 (design the key lifecycle once).
- **Tests/measurements required**: rotation test: save under key v1, rotate to v2 with v1 retained, load succeeds.
- **Effort**: M
- **Migration/rollback concern**: current deployments fine until first rotation; fix before any rotation runbook exists.

### PROD-100: TabularDataParser misaligns headers/data when the header row has blank cells
- **Severity**: Low (latent — no production caller)
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Kernel/TabularData/TabularDataParser.cs:42-49` — headers via `CellsUsed()` (skips blanks) but data via positional `row.Cell(col)` for `1..headers.Count`. (FACT)
- **Current behavior**: A blank header cell shifts all following headers one column left relative to their data; trailing data columns beyond the last used header cell are silently dropped. (FACT)
- **Why it matters**: (INFERENCE) Column stats would be attributed to the wrong headers — wrong numbers presented as insight if this ships.
- **OS/product consequence**: Silent data corruption in a future "drop a spreadsheet" journey.
- **Recommendation**: (PROPOSAL) Read headers positionally (`usedRows[0].Cell(col)` across the used range width) with placeholder names for blanks.
- **Deletion/simplification opportunity**: see CLEAN-101 (delete/park until the feature exists).
- **Dependencies**: PERF-100, CLEAN-101.
- **Tests/measurements required**: sparse-header-row test.
- **Effort**: S
- **Migration/rollback concern**: none.

### PROD-101: Post-login TaskManager surface is built from the wrong journal (always empty)
- **Severity**: Low
- **Confidence**: Medium
- **Evidence**: `src/DigitalBrain.Kernel/Auth/UserSessionNeuron.cs:127,132` — `taskEvents = OutgoingJournal.Concat(IncomingJournal)` of the **session grain** feeds `UiSurfaceLiveData.TaskManagerFromTasks`. (FACT)
- **Current behavior**: The session grain's journal contains login/session/surface synapses, not task synapses, so the TaskManager surface renders from an effectively empty set. (FACT)
- **Why it matters**: (INFERENCE) Either dead ceremony or a bug (tasks were expected from a task-owning grain).
- **OS/product consequence**: Post-login home never shows real tasks via this path.
- **Recommendation**: (PROPOSAL) Source tasks from the task-owning neuron, or drop the surface from the login flow.
- **Deletion/simplification opportunity**: yes.
- **Dependencies**: ARCH-101, ARCH-103.
- **Tests/measurements required**: assertion on surface contents after login with pre-existing tasks.
- **Effort**: S
- **Migration/rollback concern**: none.

### PERF-100: TabularDataParser has no input-size bound (zip-bomb / memory exhaustion if wired)
- **Severity**: Medium (latent — no production caller)
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Kernel/TabularData/TabularDataParser.cs:25-52` — `new XLWorkbook(stream)` on caller bytes with no cap; `MaxUiRows` caps only returned rows; stats iterate every data row (`allDataRows` fully materialized). (FACT)
- **Current behavior**: A crafted small xlsx expanding to millions of rows/cells is fully materialized (twice: ClosedXML model + `allDataRows` string lists). (FACT)
- **Why it matters**: (INFERENCE) An upload endpoint feeding this is a one-request memory-exhaustion DoS on the silo host.
- **OS/product consequence**: Kernel availability tied to a single hostile file.
- **Recommendation**: (PROPOSAL) Cap input bytes at the boundary AND cap parsed rows/cells (e.g. stats over first N thousand rows with a truncation flag) before any production wiring.
- **Deletion/simplification opportunity**: see CLEAN-101.
- **Dependencies**: PROD-100, CLEAN-101; FRAME-100 (ClosedXML doc verification pending).
- **Tests/measurements required**: pathological-workbook memory test.
- **Effort**: S-M
- **Migration/rollback concern**: none.

### CLEAN-100: SyncManifest is dead code
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Kernel/Sync/SyncManifest.cs:3-5` — zero references repo-wide (src + tests). (FACT)
- **Current behavior**: Compiles, ships, does nothing. (FACT)
- **Why it matters**: (INFERENCE) Implies a sync/export capability that does not exist; reading tax.
- **OS/product consequence**: none directly.
- **Recommendation**: (PROPOSAL) Delete file + folder.
- **Deletion/simplification opportunity**: yes — whole directory.
- **Dependencies**: none. **Tests/measurements required**: build green. **Effort**: S. **Migration/rollback concern**: none.

### CLEAN-101: A production-dead feature stratum ships inside the kernel (Db, TabularData, Uploads, ChatNeuron, SignalEgress, IngressNeuron)
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: Repo-wide caller search: `TabularDataParser`, `ChatUploadClassifier`, `ChatNeuron`/`VisualizeDataRequest`, `IIngressNeuron` — test-only callers; `SignalEgressBus`/`SignalEgressStreamSubscriber` registered only in `tests/DigitalBrain.TestKit/NeuronTestKernelConfigurator.cs:49-50`; `SqliteSchemaInspector` registered (`DigitalBrainOrleansExtensions.cs:201`) but never resolved in production; their consuming RPCs (`WatchSynapses`, `WatchHomeFeed`, `Transcribe`) have no server implementation (ARCH-100). (FACT)
- **Current behavior**: Six components + their Core synapse contracts exist solely to satisfy their own tests. (FACT)
- **Why it matters**: (INFERENCE) ~800+ LOC of kernel surface (some with latent security/DoS properties: SEC-105, PERF-100) that every audit, build, and reader pays for; tests assert behavior no user can reach — false confidence.
- **OS/product consequence**: Kernel bloat contradicts the minimal-trusted-kernel goal; delete-first principle (CLAUDE.md step 2) unapplied.
- **Recommendation**: (PROPOSAL) Delete (with their tests and Core contracts) or move to a parked `experiments/` project outside the kernel; re-introduce each only with its real transport/feature.
- **Deletion/simplification opportunity**: yes — the largest in this subsystem.
- **Dependencies**: ARCH-100 (proto deletion unlocks most), SEC-105, PERF-100, PROD-100.
- **Tests/measurements required**: full solution + Flutter build/test green post-deletion.
- **Effort**: M
- **Migration/rollback concern**: git history preserves them; none live.

### CLEAN-102: Kernel registers gRPC + grpc-web + CORS with no gRPC service mapped
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `DigitalBrainOrleansExtensions.cs:203` (`AddGrpc()`), `:209-213` (CORS `browser`), `:302` (`UseGrpcWeb(DefaultEnabled=true)`); no `MapGrpcService` call in this host; `KernelCompositionTests.cs:106`. (FACT)
- **Current behavior**: Dead middleware in the pipeline of every request; CORS policy applied globally for a browser gRPC client that connects to a different host. (FACT)
- **Why it matters**: (INFERENCE) Misleads readers into thinking the kernel is the gRPC gateway; unused `AddGrpc` default limits (4MB) would silently apply if a service were ever mapped without review.
- **OS/product consequence**: none functional today.
- **Recommendation**: (PROPOSAL) Remove with ARCH-100; keep CORS only if the static web bundle needs it (it serves same-origin, so likely not).
- **Deletion/simplification opportunity**: yes.
- **Dependencies**: ARCH-100. **Tests/measurements required**: `KernelCompositionTests` green. **Effort**: S. **Migration/rollback concern**: none.

### CLEAN-103: GeneratedPackRuntime.Ensure ignores its journal parameter; Generated/ folder misnames human code
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Kernel/Generated/GeneratedPackRuntime.cs:42-50` — `Ensure(IEnumerable<Synapse> journal, string primaryKey)` never reads `journal`; the folder `Generated/` contains only this human-authored file. (FACT)
- **Current behavior**: Callers pass a journal expecting reinstall-from-journal semantics; method only logs. (FACT)
- **Why it matters**: (INFERENCE) Contract lies: after grain reactivation an embodied pack is NOT restored from the journal — the LLM fallback silently takes over (behavioral regression masked as design). Folder name risks generated-code exclusion by tools/reviewers.
- **OS/product consequence**: Pack embodiment is not activation-durable via this path.
- **Recommendation**: (PROPOSAL) Either implement journal-based reinstall (find last `NeuroPack` synapse and `Install`) or drop the parameter; move file to `Foundry/` or `Packs/`.
- **Deletion/simplification opportunity**: yes (parameter) .
- **Dependencies**: foundry subsystem (`GeneratedNeuron` caller semantics).
- **Tests/measurements required**: reactivation test asserting embodiment survives (or explicitly does not).
- **Effort**: S
- **Migration/rollback concern**: none.

### CLEAN-104: Vacuous health checks, vacuous override, duplicated registrations
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `DigitalBrainOrleansExtensions.cs:288-292` — connector health checks that always return Healthy; `UserSessionNeuron.cs:18-21` — `OnActivateAsync` override that only calls base; `DigitalBrainOrleansExtensions.cs:215-218` duplicates `ITelemetrySink`/`SchemaRegistry` registration in `src/DigitalBrain.Mcp/Program.cs:34-37` with identical descriptors. (FACT)
- **Current behavior**: Health endpoints report connector health that measures nothing; two hosts define the schema registry independently. (FACT)
- **Why it matters**: (INFERENCE) Always-green health checks are worse than none (mask real failures); duplicated schema descriptors will drift.
- **OS/product consequence**: Operational signals not trustworthy for connectors.
- **Recommendation**: (PROPOSAL) Delete the two health checks (or make them probe pack-config presence); delete the vacuous override; move `SchemaRegistry` construction to one shared extension.
- **Deletion/simplification opportunity**: yes.
- **Dependencies**: none. **Tests/measurements required**: build + health-endpoint smoke. **Effort**: S. **Migration/rollback concern**: none.

### FRAME-100: ClosedXML usage could not be doc-verified (Context7 quota exhausted)
- **Severity**: Note
- **Confidence**: High (that the gap exists)
- **Evidence**: `mcp__context7__resolve-library-id` and the plugin variant both returned "Monthly quota exceeded" during this audit; `TabularDataParser.cs:25-49` uses `XLWorkbook(stream)`, `RangeUsed()`, `RowsUsed()`, `CellsUsed()`, `GetString()`, `GetFormattedString()`, `Cell(col)`. (FACT)
- **Current behavior**: APIs match ClosedXML's long-stable public surface (`RangeUsed()` nullability is correctly handled at line 31), but the repo-pinned version's docs were not consulted. (FACT + stated limitation)
- **Recommendation**: (PROPOSAL) Re-verify against pinned ClosedXML docs when quota resets, especially `GetFormattedString` culture behavior vs the invariant-culture `double.TryParse` at line 66 (locale-formatted numbers may not round-trip).
- **Dependencies**: PERF-100, PROD-100. **Effort**: S. **Migration/rollback concern**: none.

### FRAME-101: Preview/alpha framework stack in the trusted kernel
- **Severity**: Note
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Kernel/DigitalBrain.Kernel.csproj:4,12,18` — `net11.0`, `NoWarn ORLEANSEXP005;MEAI001`, `aspnet:11.0-preview` container base; `Hosting/PrototypeJournals.cs:4` and `DigitalBrainOrleansExtensions.cs:150-157` build on Orleans Journaling alpha APIs (`Orleans.Journaling 10.2.1-preview.1.alpha.1` per `Directory.Packages.props`). The audit brief said ".NET 10" — repo reality is .NET 11 preview. (FACT)
- **Current behavior**: The durability substrate (journaling format, `IDurableList`, `IJournaledStateManager`) is alpha-labeled by its vendor; no version-pinned public docs exist to verify against (documentation gap, not invented).
- **Why it matters**: (INFERENCE) Journal wire-format or API breaks across alpha bumps could strand persisted journals — the system's source of truth.
- **Recommendation**: (PROPOSAL) Add a journal-format round-trip/replay compatibility test that runs on every package bump; record the alpha dependency as an accepted risk in an ADR.
- **Dependencies**: kernel-a (Program/KernelServices), runtime subsystem. **Effort**: S-M. **Migration/rollback concern**: keep old-format readers until re-journaled.

---

## Finding index

| ID | Severity | Title |
|---|---|---|
| SEC-100 | High | DevAutoLogin config flag enables admin/admin anywhere, bypasses existing passwords |
| SEC-101 | High | First-login provisioning fail-open by default, grants admin |
| SEC-102 | Medium | DP key ring stored unencrypted beside protected data |
| SEC-103 | Low | HTTPS gate spoofable under ACA forwarded-header trust |
| SEC-104 | Low | PBKDF2 100k iterations; no password policy |
| SEC-105 | Low | IngressNeuron = unauthenticated broadcast (dead) |
| SEC-106 | Note | Session lookup by guessable clientId |
| ARCH-100 | High | Dead, divergent digitalbrain.proto generated on both sides |
| ARCH-101 | High | Two parallel unreconciled auth/session systems |
| ARCH-102 | Medium | Provider concerns hardcoded in kernel hosting |
| ARCH-103 | Medium | Login fires Aspire orchestration command |
| REL-100 | Medium | UserSessionNeuron unbounded journal scans |
| REL-101 | Medium | Signing-key rotation orphans all pack-config entries |
| PROD-100 | Low | TabularDataParser header/data misalignment |
| PROD-101 | Low | Post-login TaskManager surface built from wrong journal |
| PERF-100 | Medium | TabularDataParser unbounded parse (zip bomb, latent) |
| CLEAN-100 | Low | SyncManifest dead |
| CLEAN-101 | Medium | Production-dead feature stratum in kernel |
| CLEAN-102 | Low | gRPC/grpc-web/CORS plumbing with no mapped service |
| CLEAN-103 | Low | GeneratedPackRuntime.Ensure ignores parameter; folder misnamed |
| CLEAN-104 | Low | Vacuous health checks / override / duplicated registrations |
| FRAME-100 | Note | ClosedXML doc verification blocked (Context7 quota) |
| FRAME-101 | Note | net11.0-preview + Orleans Journaling alpha in trusted kernel |
