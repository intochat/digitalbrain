# Subsystem audit: connectors-and-contracts

- **Subsystem**: connectors (Gmail + Salesforce integrations) and the contracts layer (Kernel.Abstractions, Pack.Contracts, Ui.Contracts, Ui.Runtime, Aspire hosting)
- **Scope**: all 22 files in `integrations/DigitalBrain.Google` + `integrations/DigitalBrain.Salesforce`, and all 22 files in `src/DigitalBrain.Aspire`, `src/DigitalBrain.Kernel.Abstractions`, `src/DigitalBrain.Pack.Contracts`, `src/DigitalBrain.Ui.Contracts`, `src/DigitalBrain.Ui.Runtime` (44 files, all human-authored, all read in full)
- **Commit**: `72400e3ebbec27e17af4ae6b5b2c4158c2797fa4` (branch `docs/refinement-audit`)
- **Date**: 2026-07-13
- **Verification note (FACT)**: Context7 monthly quota was exhausted during this audit; `Google.Apis.Auth 1.75.0` and `DeveloperForce.Force 2.1.0` API usage could NOT be verified against current vendor docs. This is recorded as FRAME-400 and statements about those libraries below are labeled accordingly.

## Subsystem overview

This subsystem is the boundary between the kernel and the outside world. `IConnector` (Kernel.Abstractions) defines the connector contract; `GoogleConnector`/`SalesforceConnector` implement it; `GmailReadNeuron`/`SalesforceReadNeuron`/`SalesforceMutationNeuron` are the Orleans tool grains INO calls; the `*ClientFactory`/`*ApiClient` pairs do provider I/O. Tokens and OAuth flow state live in `IPackConfigStore` (DataProtection-encrypted per value — verified in `src/DigitalBrain.Kernel/Config/PackConfigStore.cs`, outside this list). Kernel.Abstractions also carries the conversation/session/surface-feed runtime state machines, which are the strongest code in the repo (revision-fenced, idempotent, bounded, fail-closed). Pack.Contracts holds the behavior-pack model (manifest, signing, UI-kit authoring); Ui.Contracts/Ui.Runtime hold the server-driven-UI vocabulary; DigitalBrain.Aspire wires storage, Orleans, LLMs, connectors and Flutter clients.

The overall picture: **auth flows and Salesforce mutation safety are engineered to a very high standard; the connector *abstraction* is not**. `IConnector` covers only the auth lifecycle, every actual capability (read/mutate) is a hand-built provider-specific grain contract living inside the kernel, and provider names are hardcoded in kernel state validation. Pack signing exists as a helper but is enforced nowhere.

---

## Per-file review

Format per file: purpose / layer; key observations against the 16-point standard (only non-trivial points); verdict; OS-model note. Findings referenced by ID.

### integrations/DigitalBrain.Google

**`AssemblyInfo.cs`** (1-3). InternalsVisibleTo for tests only. Verdict: retain.

**`DigitalBrain.Google.csproj`** (1-19). net11.0, references Core + Kernel.Abstractions, Google.Apis.Gmail.v1/Auth via central versions. Clean boundary (no kernel-host reference). Verdict: retain.

**`GmailApiClientFactory.cs`** (1-28). Builds an `IGmailApiClient` from merged app+user config; requires client_id/secret/refresh_token. Correct fail-closed behavior when keys missing. Every call constructs a fresh credential → token refresh per operation (PERF-400). Verdict: retain (add credential caching).

**`GmailNeuron.cs`** (1-569). `GmailReadNeuron` grain implementing read/metadata/mutation tool grains. Strong: owner-bound flow references (state protector unprotect + ordinal compare), one-shot start tokens with fingerprints and expiry, exhaustive request validation (`Valid(...)` bounds every field), safe-reason error mapping, `OperationCanceledException` passthrough. Weak: auth-failure classification by exception-message substrings (REL-401); duplicated request/selection mapping via unchecked enum casts (ARCH-402); `BuildConnectionResultAsync` persists pending state with `CancellationToken.None` (deliberate, commented pattern — acceptable). Verdict: simplify (extract shared connection-result machinery; kill mapping layer).

**`GoogleAppConfigSeeder.cs`** (1-74). IHostedService that seeds app-scope client_id/secret/redirect_uri from configuration into the encrypted store. Idempotent (writes only on change). Verdict: retain.

**`GoogleClientFactory.cs`** (1-422). Static OAuth helper: ~25 magic string keys, authorization URL builder, code exchange, pending-state resolution state machine. Scopes are exactly `gmail.readonly` + `gmail.send` (least privilege confirmed — lines 48-49, 82). State fingerprints are SHA-256, compared fixed-time. **No PKCE**: `CreateAuthorizationUrl` emits no `code_challenge`, `ExchangeAuthorizationCodeAsync` sends no `code_verifier`, and `OAuthCodeVerifierKey` (line 30) is dead (SEC-400, CLEAN-402). Exchange failure throws with raw response body in the message (SEC-402). `new HttpClient()` per exchange (PERF-403). `AccessTokenKey` stored at exchange but never consumed afterwards (CLEAN-402). The `ResolveAuthorization` state machine is a near-clone of the Salesforce one but *without* completed-witness expiry (ARCH-403). Verdict: split/replace — hoist the flow state machine into one shared, PKCE-bearing implementation.

**`GoogleConnector.cs`** (1-469). `IConnector` implementation. Strong: pinned client_id/redirect_uri at challenge time and re-validated at exchange; durable "processing" claim before crossing the token endpoint (one-shot callback state across restarts, lines 246-249); replay path returns success idempotently for the exact completed flow; credential + completion witness committed in a single pack write (lines 320-325). Weak: `catch { }` on config read (line 69) converts store outages into "credential-form-needed" (PROD-404); dead `store is null`/nullable dance (CLEAN-400); wipes the user's existing token pack when issuing a fresh challenge without revoking the old refresh token at Google (lines 139-143, SEC-401); `ValidateConfigAsync` demands `redirect_uri` although both `BeginAuthAsync` and `CompleteAuthAsync` fall back to defaults (PROD-402); error path returns `"error:" + ex.Message` inside `AuthChallenge.UrlOrForm` (stringly-typed error channel). `TestConnectionAsync` probes `labels.list` — cheap and scope-compatible. Verdict: simplify + fix (PROD-404, SEC-400/401).

**`GoogleCredentialFactory.cs`** (1-19). Builds `UserCredential` from refresh token via `GoogleAuthorizationCodeFlow` with no `IDataStore` — refreshed access tokens are never persisted, so each new client re-refreshes (PERF-400). Usage pattern is plausible for Google.Apis.Auth but unverified against 1.75.0 docs (FRAME-400/401). Verdict: retain (add caching or IDataStore).

**`GoogleGmailApiClient.cs`** (1-610). The Gmail wire client. Strong: data minimization is real — metadata format with only From/To/Subject headers, explicit `Fields` projections on every request, no bodies ever fetched; careful RFC2047 decode with regex timeouts and control-char rejection; deterministic ordering; coverage reporting (pages read, candidates, exhaustion); send validation whitelists the `UniqueTag` charset before it is embedded in a Message-ID (header injection blocked). Send idempotency = search `in:sent rfc822msgid:<tag@digitalbrain.invalid>` then send — non-atomic and subject to Gmail search indexing lag (PROD-400). Metadata window issues one `Users.Messages.Get` per candidate sequentially, up to 64 (PERF-401); filters (sender/subject/date) are applied client-side after fetch rather than pushed into `q` (deliberate for stable coverage, but costs quota). No 429/backoff handling (REL-400). Verdict: retain + fix PROD-400, batch the gets.

**`IGmailApiClient.cs`** (1-121). Provider-side read-model records + client interface. Near-duplicate of the `DigitalBrain.Kernel.Runtime` Gmail types in `GmailTool.cs` (ARCH-402). Verdict: merge with kernel contracts or generate mapping.

**`IGmailApiClientFactory.cs`** (1-9). One-method factory interface. Verdict: retain.

### integrations/DigitalBrain.Salesforce

**`DigitalBrain.Salesforce.csproj`** (1-19). DeveloperForce.Force 2.1.0 + Newtonsoft.Json 13.0.4 (central). FRAME-402 (dormant package). Verdict: retain.

**`ISalesforceApiClient.cs`** (1-41). Client interface with default interface methods returning `Unsupported`/`Unavailable` for the entire semantic read/mutation surface — a compile-time-silent capability hole (CLEAN-404). Legacy string-JSON methods (`ListAccountsAsync` returning `string[]` of serialized JSON) coexist with the typed page model. Verdict: simplify — remove DIM defaults and legacy string methods.

**`ISalesforceApiClientFactory.cs`** (1-9). Factory interface. Verdict: retain.

**`SalesforceApiClient.cs`** (1-937). The largest and best provider client in the repo. Reads: semantic entity/field resolution against live describe metadata (labels only — the LLM never supplies API names), SOQL built exclusively from provider-derived API names + escaped literals (`EscapeSoql`/`EscapeLike`/`EscapeSosl`, record-ID charset validation with keyPrefix binding) — SOQL/SOSL injection is convincingly closed. Continuations bound to `SalesforceProviderScope` (org + user) and to a path-validated `/services/data/...` URL (cross-tenant reuse blocked). Mutations: `PreviewUpdateAsync` resolves schema, requires field updateable, canonicalizes the value by type, captures the original value into a size-bounded `PreparedUpdateDocument`; `ApplyUpdateAsync` re-resolves schema, re-validates the document against live metadata, re-reads the current value → `AlreadyApplied` (idempotent) / `Conflict` (drift) / applies / **post-write verification read** → `VerificationFailed` if not confirmed. This is the preview→approve→apply→verify pattern the OS model demands, minus an atomic conditional update (PROD-401). Error classification by message substring (`REQUEST_LIMIT_EXCEEDED` etc., lines 884-894) is brittle (REL-401); `IsSalesforceClientException` only catches `Salesforce.*` namespace exceptions so raw `HttpRequestException` escapes the catch filters. `GetCurrentUserProfileAsync` returns email/username — justified for profile display but is the widest data exposure in the file. No caching of describes (PERF-402); ForceClient calls take no CancellationToken (REL-402). Verdict: retain (fix classification, add describe cache).

**`SalesforceApiClientFactory.cs`** (1-15). Merges scoped config, creates session per call (token refresh per operation — PERF-400). Verdict: retain.

**`SalesforceAppConfigSeeder.cs`** (1-105). Mirror of the Google seeder plus login_url/api_version defaults. Verdict: retain (could share one generic seeder with Google — deletion opportunity).

**`SalesforceClientFactory.cs`** (1-777). Static OAuth + session helper. **Has full PKCE** (`CreatePkceCodeVerifier`/`CreatePkceCodeChallenge` S256, verifier sent at exchange, lines 422-426, 476-487) — the asymmetry with Google is the core of SEC-400. Completed-witness has a 1h expiry (`OAuthCompletedWitnessLifetime`) that Google's clone lacks (ARCH-403). Login-URL allowlist accepts any `*.salesforce.com` and `*.site.com` host (lines 541-545, SEC-403). Redirect URI normalization enforces the fixed callback path and HTTPS-or-loopback. `CreateOAuthSessionAsync` final branch (lines 634-641) is unreachable (CLEAN-401); `CreateOAuthStartUrl(values, flowReference)` ignores `values` (CLEAN-402). Token-endpoint error text can include the trimmed raw body (lines 734-737, SEC-402). `RequestTokenAsync` creates a new HttpClient per call (PERF-403). Verdict: split/replace along with the Google twin (shared flow machine).

**`SalesforceConnector.cs`** (1-451). `IConnector` implementation, same durable-claim/replay design as Google plus PKCE and pinned login_url. Weak: replays an existing challenge without first checking whether the credential is already Ready (Google does check — PROD-403); `ValidateConfigAsync` mutates state (clears expired pending) — a validator with side effects; `TestConnectionAsync` runs a real `ListAccountsAsync(1)` — heavier than Google's labels probe but acceptable. Verdict: retain + converge with Google connector.

**`SalesforceMutationNeuron.cs`** (1-94). Thin grain: config check → credential check → delegate preview/apply to client; safe reasons; exception type name only in logs. Correct fail-closed ordering. Verdict: retain.

**`SalesforceReadContracts.cs`** (1-56). Provider-side failure enum, safe-message exception, scope + continuation records (internal fields — good encapsulation). Verdict: retain.

**`SalesforceReadNeuron.cs`** (1-722). Read tool grain with persisted continuation store (max 32, FIFO eviction, versioned JSON codec with strict bounds + cycle-free validation and graceful invalid-state discard) and a legacy + persisted OAuth-start token model (SHA-256 hash, fixed-time compare, expiry). State writes are compensated on failure (restore previous state on `WriteStateAsync` throw). `BuildConnectionResultAsync` wipes the user credential pack when starting a new local flow (SEC-401 applies here too). Message-substring failure classification (REL-401). Verdict: retain (this is the most complete tool-grain implementation; use it as the template the connector model should generate).

### src/DigitalBrain.Aspire

**`AssemblyInfo.cs`** (1-3). Verdict: retain.

**`DigitalBrain.Aspire.csproj`** (1-22). Aspire.Hosting + Orleans + Azure Storage + Ollama toolkit; packable v0.3.0. Verdict: retain.

**`DigitalBrainBuilderExtensions.cs`** (1-361). Storage/Orleans/LLM composition. Sensible: secrets as Aspire secret parameters (never inline), per-namespace Azurite volume, fresh local cluster id to dodge stale membership, model registry flattened to env vars, `WaitFor` sequencing on all storage resources. `WithExternalHttpEndpoints()` on the kernel exposes web+grpc endpoints externally in publish mode — authorization must therefore live in the kernel itself (it does; noted, no finding). `WithOptionalEnvironment` (309-321) is currently uncalled — speculative (CLEAN-402). Verdict: retain.

**`DigitalBrainContext.cs`** (1-40). Context record for wiring. Verdict: retain.

**`DigitalBrainOptions.cs`** (1-89). Fluent model registration with role routing; `ResolvedLlmProvider/Model` precedence logic is subtle but correct (overridden flags reset by `SelectLlm`). Verdict: retain.

**`FlutterAspireExtensions.cs`** (1-192). Desktop client gets a bootstrap secret via env (documented as exchange-only credential); web client is deliberately secret-free with validated HTTPS OIDC issuer — a real trust-boundary distinction, done right. Path resolution env override checked with `Directory.Exists`. Verdict: retain.

**`GoogleAspireExtensions.cs`** (1-82). google-client-id/secret as secret parameters, redirect defaulted to kernel web port + fixed callback path; operator-facing markdown descriptions encode the verification/test-user pitfalls. Verdict: retain.

**`SalesforceAspireExtensions.cs`** (1-104). Mirror for Salesforce incl. login_url/api_version parameters. Verdict: retain (another shared-seeder/extension dedup candidate).

### src/DigitalBrain.Kernel.Abstractions

**`AuthRequiredAIFunction.cs`** (1-57). DelegatingAIFunction gate: checks connection before invoking inner tool; never leaks inner execution to unauthenticated caller; `LastInvocationRequiredAuthentication` is safe only because instances are per-request (comment says so; hidden temporal coupling — Note). Verdict: retain.

**`ConversationArchive.cs`** (1-240). Hash-chained archive segments (segment id = keyed hash of scope + prev + through + digest; digest = SHA-256 over prev-digest + turns), cycle detection on read, strict binding validation. Tamper-evident conversation history — genuinely OS-grade. Verdict: retain.

**`ConversationModel.cs`** (1-66). LLM-facing intent/mutation proposal contracts. `SemanticMutationKind` hardcodes `GmailSend`/`SalesforceFieldUpdate` (part of ARCH-401's evidence). Verdict: retain (generalize kinds with connector model).

**`ConversationNeuron.cs`** (1-1419). The conversation state machine: idempotent command acceptance (AcceptedCommands + input hash), lease fencing (`DemandLeaseFence` binds completion to the exact lease owner + attempt), approval/effect lifecycle with explicit `outcome-unknown` state and `VerifyBeforeRetry` policy, atomic turn+outbox+operation transitions, bounded retention with deterministic compaction, migration transitions for rolling deployments. This file *is* the mutation-safety rail. One leak: `IsProviderTool` (1393-1397) hardcodes `google→gmail.*` else `salesforce.*` — an unknown provider name falls into the salesforce branch, and a third provider requires editing this kernel file (ARCH-401). Verdict: retain; extract the provider/tool registry.

**`EncryptedRuntimeStateContracts.cs`** (1-181). Envelope-encrypted (KEK-wrapped DEK, AES-GCM nonce/tag fields, signature), keyed scope hashes with length-prefixed hashing (no delimiter-collision), fail-closed `RuntimeStateIntegrityException : IOException`. Verdict: retain.

**`GmailTool.cs`** (1-249) and **`SalesforceTool.cs`** (1-236). Provider-specific tool grain contracts + request/result records + tool-id constants living in the kernel abstractions assembly. Well-bounded and serializer-annotated, but they are the concrete evidence that every new connector means new kernel contract files (ARCH-400/401) and they duplicate the provider-side models (ARCH-402). Verdict: retain short-term; this is the layer a generalized capability contract must replace.

**`IScopedChatClientFactory.cs`** (1-13). Justified placement (comment explains the layering). Verdict: retain.

**`InoEffectPlan.cs`** (1-157). Immutable effect plan with payload purge on completion (approved provider payload does not linger). `InoMutationGrants.RequiredForTool` hardcodes gmail.send/salesforce.write (ARCH-401). Verdict: retain; registry-ify grants.

**`LlmAttribute.cs`** (1-55). Orleans facet-based `[Llm<TModel>]` keyed IChatClient injection; validates parameter type; uses the documented `IAttributeToFactoryMapper` extension point. `Activator.CreateInstance` per resolution is cheap enough at activation. Verdict: retain.

**`Neuron.cs`** (1-397). Full `DurableGrain` base implementation (journals, timeline subscription, causal stamping, checkpoint/branch/restore, instrumentation) inside an *Abstractions* assembly (ARCH-405). Fail-fast on missing journal registration (no silent in-memory fallback) — good. Reflection `IHandle<>` dispatch path logs at Debug when used. `IsJournalWriterUninitialized` matches an exception message string — brittle coupling to Orleans journaling alpha internals (accepted risk given ORLEANSEXP005). Verdict: move to a runtime assembly; retain behavior.

**`NeuronStateProtectors.cs`** (1-64). AES-GCM protector (proper nonce/tag layout, decrypt throws on tamper) + explicit pass-through for dev with logged warning. No AAD binding of ciphertext to its owning neuron/scope (a swapped blob decrypts fine) — Note-level. Verdict: retain.

**`SemanticIntent.cs`** (1-149). Intent enums + records. `SemanticProvider` enum hardcodes Gmail/Salesforce (ARCH-401); `SemanticFilterOperator.Set` appears unused by the Salesforce compiler (falls to `Unsupported` at runtime). Verdict: retain.

**`SessionNeuron.cs`** (1-258). Session state machine: hashed refresh tokens only, rotation with replay ledger (replay of a consumed hash detected), revocation bumps SessionVersion (immediate access invalidation), canonicalized bounded grants. Verdict: retain.

**`SurfaceFeedNeuron.cs`** (1-749). Surface feed with single-use-bounded action bindings: random 32-byte token hashes, fixed-time comparison, per-binding MaxUses + idempotency-key dedupe, revision-bound consumption, renewal transition validated against the stored presentation shape. Legacy shape compatibility handled explicitly. Verdict: retain.

**`SynapseDispatch.cs`** (1-47), **`SynapseStream.cs`** (1-13). Frozen reflection handler cache; single global timeline stream id ("timeline"/"global" — a global broadcast domain; tenancy filtering must happen at subscribers — Note). Verdict: retain.

**`DigitalBrain.Kernel.Abstractions.csproj`** (1-21). References Orleans Core/Journaling/Streaming + M.E.AI — heavy for "abstractions" (ARCH-405). Verdict: retain, revisit split.

### src/DigitalBrain.Pack.Contracts

**`AssemblyInfo.cs`** (1-3). Verdict: retain.

**`Configuration.cs`** (1-95). `ConfigurationProvided` synapse (comment: secrets must never be logged — enforcement lives elsewhere) + generic pack-config→UI form mapping (Secret kind marks fields). Verdict: retain.

**`DigitalBrain.Pack.Contracts.csproj`** (1-22). Packable protocol assembly; references Core + Ui.Contracts only — correct for "pack authors reference stable protocol packages". Verdict: retain.

**`Distribution/BundleManifest.cs`** (1-33). Tier/channel/dependency metadata; retired enum ordinal documented (serialization-compat aware). Verdict: retain.

**`Distribution/IPackBehavior.cs`** (1-47). The behavior-pack contract: pure synchronous `Respond`/`Handle`, `PackManifest` with `HandledSynapseTypes` + `RequiredConfig`. Capability declaration is coarse (synapse types + config fields only — no resource/permission grammar). Implemented, not placeholder: consumed by `GeneratedPackRuntime`/`PackAlcEmbodier` in the kernel. Verdict: retain; capability model needs depth.

**`Distribution/NeuroPack.cs`** (1-21). Pack record with author ECDSA key + signature fields. Note: `Id(5)` is skipped (gap presumably from a removed member — fine for compat, worth a comment). Verdict: retain.

**`Trust/PackSignatureVerifier.cs`** (1-75). ECDSA-P256/SHA-256 over `Name|Version|SHA256(Code)|PubKey`. Correct canonicalization (code hashed, so pipes in code cannot forge fields; name/version are identifier-like). Malformed input → false, not crash. **But no code in `src/` outside the Trust folder calls `VerifyPack` or `PublisherTrust.IsTrusted`** (SEC-405): signing exists, enforcement does not. Verdict: retain + wire into the embodiment path.

**`Trust/PublisherTrust.cs`** (1-21). Integrity + allowlist conjunction, correctly documented. Same enforcement gap (SEC-405). Verdict: retain + enforce.

**`UiKit/KitExperience.cs`** (1-93). Experience state machine base; accumulates flow state per pack instance in a plain dictionary (`_state` unbounded and never cleared across hops — Low; bounded in practice by field count). Verdict: retain.

**`UiKit/UiExperience.cs`** (1-320). Fluent hop/widget builder over the ui: vocabulary. Pure construction, no I/O. Verdict: retain.

### src/DigitalBrain.Ui.Contracts

**`DigitalBrain.Ui.Contracts.csproj`** (1-21). Verdict: retain.

**`Ui/RfwCard.cs`** (1-21). Legacy RFW payload + `IChatNeuron`; overlaps `UiSurface.ForRfw` (its own comment admits the duplication) (CLEAN-405). Verdict: merge/delete.

**`UiNeuronContracts.cs`** (1-28). Session/observability neuron contracts + chart synapses. Verdict: retain.

**`UiSurfaces.cs`** (1-608). The SDUI vocabulary: `UiSurface` (a Synapse), widget tree, three parallel vocabularies (`NeuronUiKit` neuron:/forui:, `UiKitVocabulary` ui:, plus raw kind strings) — vocabulary sprawl invites client drift (Note, folded into CLEAN-405's cleanup). Props are `IReadOnlyDictionary<string, object?>` — untyped bag; serializer must round-trip arbitrary object graphs (works today via Orleans JSON surrogates but is schema-free: no versioning of prop shapes except the SurfaceFeedPresentation compat shim). Verdict: retain; converge vocabularies.

### src/DigitalBrain.Ui.Runtime

**`DigitalBrain.Ui.Runtime.csproj`** (1-17). Packable "runtime and sample builders". Verdict: retain.

**`UiSurfaceRuntime.cs`** (1-850). `UiSurfaceSamples` (demo/stub surfaces with hardcoded demo tasks) shipped in a packable runtime assembly next to `UiSurfaceLiveData` (real projections used by `UserSessionNeuron`) (CLEAN-403). `Login(... string? defaultPassword)` embeds a plaintext password as a field value inside a `UiSurface` — which is a `Synapse` and thus journal-eligible (SEC-404). `WithCommon` helper duplicated verbatim in both classes in the same file (CLEAN-403). Live projections are pure functions over synapse timelines — testable, good. Verdict: split (samples out of the shipped package), fix SEC-404.

---

## Subsystem-specific questions

### 1. Is `IConnector` a coherent general capability model?

**FACT**: `IConnector` (`src/DigitalBrain.Kernel.Abstractions/IConnector.cs:7-19`) has exactly four methods — `ValidateConfigAsync`, `BeginAuthAsync`, `CompleteAuthAsync`, `TestConnectionAsync` — plus a static `ConnectorDescriptor { Id, DisplayName, RequiredConfigKeys, Scopes }`. There is no capability declaration beyond OAuth scope strings, no typed read surface, no mutation surface, no preview/apply contract, no pagination/continuation contract, no rate-limit metadata, no health-detail schema.

**FACT**: All actual capability lives outside the contract: `IGmailReadToolGrain`/`IGmailMetadataToolGrain`/`IGmailMutationToolGrain` (`GmailTool.cs`) and `ISalesforceReadToolGrain`/`ISalesforceMutationToolGrain` (`SalesforceTool.cs`) are hand-written per provider **inside Kernel.Abstractions**, and their implementations resolve `IConnector` only for config/auth (`[FromKeyedServices("google")] IConnector` in `GmailNeuron.cs:27`).

**Could a 10th/100th connector be added without touching the kernel or INO?** **No (INFERENCE, high confidence).** A new provider today requires edits to at least:
1. New tool-grain interfaces + request/result records in `Kernel.Abstractions` (the GmailTool/SalesforceTool pattern).
2. `ConversationNeuron.IsProviderTool` (`ConversationNeuron.cs:1393-1397`) — hardcoded `google→gmail.*` else `salesforce.*`.
3. `InoMutationGrants.RequiredForTool` (`InoEffectPlan.cs:144-149`) — hardcoded grant switch.
4. `SemanticProvider`/`SemanticMutationKind` enums (`SemanticIntent.cs:5-12`, `ConversationModel.cs:22-28`).
5. `OAuthCallbackPaths` provider registry in Core (referenced from `ConversationNeuron.cs:1385`).
6. INO tool composition/dispatch (outside this list, but implied by the tool-id constants).

### 2. OAuth verification

- **PKCE**: Salesforce yes (S256, verifier persisted in pending state and sent at exchange). Google **no** (SEC-400); the dead `OAuthCodeVerifierKey` shows it was planned.
- **State**: DataProtection-protected owner-bound state (`IOAuthStateProtector.Protect(NeuronId)`), verified with `TryUnprotect` + ordinal owner comparison in both connectors and both neurons; state fingerprint (SHA-256) persisted and compared fixed-time. Strong.
- **Nonce**: N/A — pure authorization-code flow for API scopes, no OIDC id_token consumed. Correctly absent.
- **Callback validation**: state unprotect → pending existence → phase/flow-id validation → exact state equality → fingerprint match → error/code checks → pinned client_id/redirect_uri revalidation. Strong on both providers.
- **Replay protection**: durable "processing" claim written *before* interpreting the callback; completed fingerprint + flow id written atomically with the credential; replay of the exact completed callback returns success idempotently, everything else fails. Salesforce adds a 1h completed-witness expiry; Google's witness never expires (ARCH-403 divergence).
- **Least-privilege scopes**: Google is exactly `https://www.googleapis.com/auth/gmail.readonly` + `https://www.googleapis.com/auth/gmail.send` (confirmed, `GoogleClientFactory.cs:48-49`); Salesforce is `api refresh_token` (broad by Salesforce's model — `api` grants full API access within the user's profile; no narrower Salesforce scope exists for field-level access — Note).
- **Google.Apis.Auth 1.75.0 verification**: NOT performed — Context7 quota exhausted (FRAME-400). The token exchange is hand-rolled HTTP anyway (FRAME-401), so library-version risk is limited to `UserCredential`/`GoogleAuthorizationCodeFlow` refresh behavior.

### 3. Token handling

- **At rest**: `IPackConfigStore` → `PackConfigStore` (kernel) encrypts per value with ASP.NET DataProtection (purpose-chained per scope/pack/key), undecryptable values are skipped without logging ciphertext. Verified by reading `src/DigitalBrain.Kernel/Config/PackConfigStore.cs:19-60`. Good.
- **Isolation**: per-user scope (`PackConfigScopes.ForUser`), app-owned keys (client_id/secret/redirect) cannot be overridden from user scope (`IsAppOwnedConfigurationKey` / `AppOwnedKeys` filters in both factories). Good.
- **Refresh**: on-demand per operation; refreshed access tokens are never persisted (PERF-400). No proactive expiry tracking.
- **Rotation/revocation**: no provider-side revocation call exists anywhere; local credential wipes (Google `BeginAuthAsync`, Salesforce new local start) orphan live refresh tokens at the provider (SEC-401). No disconnect/sign-out surface either.
- **Leakage**: neuron logging is exemplary (exception *type name* only). Two lower-tier leaks: raw token-endpoint error bodies in exception messages (SEC-402) and the Ui.Runtime login default password (SEC-404). Tokens never appear in URLs; OAuth start URLs carry only opaque protected flow references.

### 4. Salesforce mutations (DeveloperForce.Force 2.1.0)

Preview/approval, idempotency, duplicate suppression, post-write verification, conflict detection, and outcome-unknown handling are all present and layered: provider level (`SalesforceApiClient.ApplyUpdateAsync`: AlreadyApplied / Conflict / VerificationFailed / verify-after-write) plus kernel level (`ConversationNeuron` effect states `approved→applying→succeeded|failed|outcome-unknown` with `VerifyBeforeRetry`). Rollback = the preserved `OriginalValue` in the prepared document (a reverse update is constructible, though no automated rollback path exists — INFERENCE: acceptable at this maturity, worth an explicit "undo" affordance later). Weaknesses: non-atomic check-then-update (PROD-401), no CancellationToken support in ForceClient (REL-402), Force client usage unverified against docs (FRAME-400 — Context7 has no reachable material this session; recorded gap).

### 5. Provider leakage into kernel/INO

Yes — see ARCH-401. The *dataplane* boundary is clean (kernel never sees raw provider payloads; neurons return bounded safe records), but the *nameplane* leaks: provider ids, tool-id prefixes, grant names, and semantic enums are hardcoded in kernel files.

### 6. Pack.Contracts behavior-pack model

Implemented but shallow: `IPackBehavior` + `PackManifest` are consumed by the kernel Foundry (Roslyn → collectible ALC → GeneratedNeuron dispatch). Signing (`PackSignatureVerifier`) and publisher allowlisting (`PublisherTrust`) are real, correct crypto — **and enforced nowhere in `src/`** (SEC-405). Capability declaration = handled synapse types + config fields; there is no permission/resource grammar (network, connectors, mutation grants) for packs.

### 7. Rate limits, backoff, pagination, data minimization

- Rate limits/backoff: none in either integration (REL-400); Salesforce at least classifies `REQUEST_LIMIT_EXCEEDED` into `LimitReached`.
- Pagination: Gmail uses bounded candidate windows with coverage reporting (honest about incompleteness); Salesforce uses provider `NextRecordsUrl` continuations, scope-bound and persisted server-side with opaque client tokens — the strongest pagination design in the repo.
- Data minimization: Gmail metadata-only (headers From/To/Subject, explicit field masks, no bodies); Salesforce label-resolved field projection defaulting to Id + name field only. Both good.

### 8. Gap between current `IConnector` and a target "connect to anything" contract

What exists is an **auth-lifecycle contract**; what the OS model needs is a **capability contract**. The gap, concretely (PROPOSAL):

| Dimension | Today | Target |
|---|---|---|
| Identity/auth | `BeginAuth`/`CompleteAuth`/`TestConnection` per connector, flow state hand-rolled per provider in pack-config KV | One shared, PKCE-mandatory durable flow machine owned by the kernel; connector supplies only endpoints/scopes/quirks descriptor |
| Capability declaration | Static `Scopes: string[]` | Typed capability manifest: read capabilities (entity/selection/pagination schema), mutation capabilities (preview/apply/verify shape, idempotency key contract, rollback support), health probe, rate-limit class, data-sensitivity class |
| Reads | Per-provider grain interfaces hand-written in Kernel.Abstractions | Generic `Query(capabilityId, typed selection) → bounded page + coverage + continuation` executed by a kernel-owned tool grain; provider supplies a capability handler |
| Mutations | Per-provider preview/apply grains; grants hardcoded in `InoMutationGrants` | Generic `Preview → PreparedEffect(originalState, desiredState, idempotencyKey)` / `Apply(PreparedEffect) → Applied\|AlreadyApplied\|Conflict\|VerificationFailed\|OutcomeUnknown` — exactly the shape `SalesforceApiClient` already implements, promoted to the contract; grant derived from the capability manifest |
| Kernel registration | Edits to `IsProviderTool`, enums, `OAuthCallbackPaths`, INO wiring | A connector registry (DI-discovered descriptors) that the conversation validator, grant checker, and INO tool composer all read from |
| Error taxonomy | Per-provider status enums + message-substring classification | Shared `NeedsAuth / AccessDenied / RateLimited / Conflict / Unavailable / OutcomeUnknown` taxonomy mapped by each provider from typed provider errors |
| Revocation | Absent | `RevokeAsync(user)` on the contract; called on disconnect and on credential-wipe paths |

The good news (INFERENCE): the Salesforce mutation pipeline and the Gmail coverage-reporting read model are already the right *shapes* — the work is extraction and registry-ification, not invention.

---

## Findings

### ARCH-400: IConnector is an auth-lifecycle contract, not a capability model — Nth connector cannot be added without kernel edits
- **Severity**: High
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Kernel.Abstractions/IConnector.cs:7-19` (four auth/config methods only); `src/DigitalBrain.Kernel.Abstractions/GmailTool.cs` and `SalesforceTool.cs` (per-provider tool grain contracts hand-written in the kernel assembly); `integrations/DigitalBrain.Google/GmailNeuron.cs:27` (connector used only for auth/config)
- **Current behavior**: Adding a connector means writing new kernel contract files, a new tool grain family, and INO wiring; `IConnector` contributes only the OAuth flow.
- **Why it matters**: (INFERENCE) The "general connector model" the OS narrative depends on does not exist yet; connector count scales kernel surface linearly.
- **OS/product consequence**: Breaks the "Gmail/Salesforce are the first two of a general model" primitive; every marketplace/pack-provided connector would require a kernel release.
- **Recommendation**: (PROPOSAL) Introduce a typed capability manifest + generic query/mutation contract per the gap table above; keep `IConnector` as the auth facet of a larger `IConnectorCapability` family; drive INO tool composition from a registry.
- **Deletion/simplification opportunity**: yes — collapses GmailTool.cs/SalesforceTool.cs duplication over time.
- **Dependencies**: ARCH-401, ARCH-402, ARCH-403; INO subsystem audit.
- **Tests/measurements required**: a third "fake" connector added purely via registry with zero kernel-file diffs (contract test).
- **Effort**: L
- **Migration/rollback concern**: existing grain interfaces must remain for rolling upgrade; introduce registry alongside, migrate INO, then delete.

### ARCH-401: Provider names/prefixes hardcoded inside kernel state validation and grant checks
- **Severity**: High
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Kernel.Abstractions/ConversationNeuron.cs:1393-1397` — `IsProviderTool` maps `google→"gmail."` and treats **every other provider** as `"salesforce."`; `src/DigitalBrain.Kernel.Abstractions/InoEffectPlan.cs:144-149` — grant switch on `GmailTools.Send`/`SalesforceTools.UpdateRecord`; `src/DigitalBrain.Kernel.Abstractions/SemanticIntent.cs:5-12` and `ConversationModel.cs:22-28` — provider enums.
- **Current behavior**: Kernel validation logic special-cases the two providers; the `IsProviderTool` else-branch silently validates any future provider's tool ids against the salesforce prefix (wrong-branch hazard today, not just tomorrow).
- **Why it matters**: (INFERENCE) A third provider passes/fails suspended-invocation validation by accident; grants for new mutations default to "no grant required" (`RequiredForTool` returns null → `Demand` allows).
- **OS/product consequence**: Fail-open grant default for unlisted mutation tools violates "fail-closed at every mutation boundary".
- **Recommendation**: (PROPOSAL) Replace with a connector/tool registry; make `RequiredForTool` fail-closed (unknown mutation tool ⇒ demand explicit grant or reject).
- **Deletion/simplification opportunity**: yes — deletes three hardcoded switch/enum sites.
- **Dependencies**: ARCH-400.
- **Tests/measurements required**: test that an unknown tool id with mutation semantics is rejected by `InoMutationGrants.Demand`.
- **Effort**: M
- **Migration/rollback concern**: persisted `SuspendedInvocation.Provider/ToolId` values must keep validating.

### ARCH-402: Gmail read model duplicated between integration and kernel with unchecked enum-cast mapping
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `integrations/DigitalBrain.Google/IGmailApiClient.cs:25-99` re-declares `GmailMailboxScope`, `GmailMessageSelection`, etc. that also exist in `src/DigitalBrain.Kernel.Abstractions/GmailTool.cs:121-231`; `GmailNeuron.cs:387-398` maps with raw casts `(GmailMailboxScope)selection.Mailbox`.
- **Current behavior**: Two parallel type families kept in sync by hand; enum member reordering in either family silently changes semantics (cast preserves ordinal, not meaning).
- **Why it matters**: (INFERENCE) A reordered or inserted enum member produces wrong mailbox scopes/filters with no compile error — a correctness time bomb.
- **OS/product consequence**: Silent misread of user mailbox scope (e.g. Drafts read as Sent).
- **Recommendation**: (PROPOSAL) Single source the records (integration references the kernel types directly — it already references Kernel.Abstractions) or add exhaustive switch-based mapping with tests.
- **Deletion/simplification opportunity**: yes — ~100 lines of duplicate records + 35 lines of mapping.
- **Dependencies**: ARCH-400.
- **Tests/measurements required**: enum round-trip equivalence test at minimum.
- **Effort**: S
- **Migration/rollback concern**: none (internal types).

### ARCH-403: Security-critical OAuth pending-state machine duplicated and divergent between Google and Salesforce
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `GoogleClientFactory.ResolveAuthorization` (`GoogleClientFactory.cs:225-287`) vs `SalesforceClientFactory.ResolveAuthorization` (`SalesforceClientFactory.cs:147-224`) — ~90% identical logic; divergences: Salesforce has PKCE (`CreatePkceCodeVerifier`) and a completed-witness expiry (`OAuthCompletedWitnessLifetime`, line 57); Google has neither.
- **Current behavior**: Two hand-maintained copies of the flow/fingerprint/expiry machine; fixes land in one and not the other (PKCE and witness expiry already prove this).
- **Why it matters**: (INFERENCE) Divergence in a replay-protection state machine is exactly where subtle auth bugs breed; every new connector would add a third copy.
- **OS/product consequence**: Weakens the "one governed auth rail" idea; inconsistent security posture per provider.
- **Recommendation**: (PROPOSAL) Extract one `OAuthFlowStateMachine` (phases, fingerprints, expiries, PKCE) parameterized by a provider descriptor; both factories become thin.
- **Deletion/simplification opportunity**: yes — several hundred duplicated lines.
- **Dependencies**: SEC-400; ARCH-404.
- **Tests/measurements required**: shared state-machine test suite runs identically for both providers (extend `OAuthConnectorSecurityTests`).
- **Effort**: M
- **Migration/rollback concern**: persisted pending-pack keys must be read-compatible.

### ARCH-404: OAuth flow state persisted as magic-key string dictionaries in the pack-config KV store
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `GoogleClientFactory.cs:20-46` (~25 string key constants), `SalesforceClientFactory.cs:23-54` (~30), all flow transitions expressed as dictionary writes (e.g. `GoogleConnector.cs:145-172`).
- **Current behavior**: A multi-phase, expiring, fingerprinted protocol is encoded as untyped KV mutations; invariants (which keys co-occur in which phase) live only in scattered `TryGetValue` chains.
- **Why it matters**: (INFERENCE) High cognitive load and easy to violate invariants on edit; contrast with the typed, validated state machines in ConversationNeuron/SessionNeuron.
- **OS/product consequence**: The auth rail is the least-typed durable state in the system while being among the most security-sensitive.
- **Recommendation**: (PROPOSAL) Typed `OAuthFlowState` record with a `Validate()` like the runtime states, serialized into the same encrypted store.
- **Deletion/simplification opportunity**: yes — deletes the key-constant walls.
- **Dependencies**: ARCH-403.
- **Tests/measurements required**: state-validation tests analogous to `ConversationTransitions.Validate`.
- **Effort**: M
- **Migration/rollback concern**: needs a legacy-dictionary read path for in-flight flows (or accept flow restart on deploy).

### ARCH-405: Kernel.Abstractions is a grab-bag; `Neuron` is a full implementation living in an "Abstractions" assembly
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Kernel.Abstractions/Neuron.cs:28-397` (DurableGrain base with journaling, streams, metrics); csproj references Orleans Journaling/Streaming + Microsoft.Extensions.AI.
- **Current behavior**: Contracts, runtime state machines, DI facets, and a concrete grain base class share one assembly consumed by integrations.
- **Why it matters**: (INFERENCE) Integrations pull in journaling/streaming/AI dependencies they don't need; the "protocol assembly" story (as done properly in Pack.Contracts/Ui.Contracts) is diluted.
- **OS/product consequence**: Blurs the pack-facing stable-protocol boundary.
- **Recommendation**: (PROPOSAL) Split: contracts (pure) / runtime transitions / grain base.
- **Deletion/simplification opportunity**: no net deletion, but dependency pruning.
- **Dependencies**: none.
- **Tests/measurements required**: build graph check.
- **Effort**: M
- **Migration/rollback concern**: assembly moves require serializer alias stability (aliases are already explicit — safe).

### SEC-400: Google OAuth flow has no PKCE (Salesforce does)
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `GoogleClientFactory.CreateAuthorizationUrl` (`GoogleClientFactory.cs:71-102`) emits no `code_challenge`; `ExchangeAuthorizationCodeAsync` (107-159) sends no `code_verifier`; `OAuthCodeVerifierKey` (line 30) is declared and never used. Salesforce: full S256 PKCE (`SalesforceClientFactory.cs:476-487`, `SalesforceConnector.cs:87-93`).
- **Current behavior**: Google authorization codes are protected only by client_secret possession + owner-bound state + one-shot processing claim.
- **Why it matters**: (INFERENCE) Current OAuth security BCP (RFC 9700) expects PKCE even for confidential clients (code injection/mix-up defense). The existing state/claim machinery mitigates but does not replace it. Could not verify Google.Apis.Auth 1.75.0 helper support this session (FRAME-400).
- **OS/product consequence**: Weaker code-interception posture on the highest-privilege connector (user mailbox).
- **Recommendation**: (PROPOSAL) Add S256 PKCE to the Google flow (the Salesforce implementation is directly reusable); persist verifier in pending state like Salesforce does.
- **Deletion/simplification opportunity**: yes if done via ARCH-403's shared machine.
- **Dependencies**: ARCH-403.
- **Tests/measurements required**: extend `OAuthConnectorSecurityTests` to assert code_challenge present and verifier sent for Google.
- **Effort**: S
- **Migration/rollback concern**: in-flight challenges without verifiers must still complete once (phase-gated).

### SEC-401: No provider-side token revocation anywhere; credential wipes orphan live refresh tokens
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `GoogleConnector.cs:139-143` (existing user pack cleared when issuing a new challenge); `SalesforceReadNeuron.cs:453-459` (user pack cleared on new local-start flow); no call to `https://oauth2.googleapis.com/revoke` or Salesforce `/services/oauth2/revoke` exists in either integration (grep-verified).
- **Current behavior**: "Disconnect"/re-auth deletes the local ciphertext but the refresh token remains valid at the provider indefinitely (Google) / until org policy expiry (Salesforce).
- **Why it matters**: (INFERENCE) Violates "revocable" in the auth model; a leaked backup/snapshot of past state or any exfiltrated token stays usable with no kill switch, and users get no true disconnect.
- **OS/product consequence**: Breaks the revocability guarantee of the auth primitive.
- **Recommendation**: (PROPOSAL) Add `RevokeAsync` to `IConnector`; call best-effort revoke before every credential wipe and from an explicit disconnect surface; journal the revocation.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: ARCH-400 (contract change).
- **Tests/measurements required**: fake-handler test asserting revoke endpoint hit before wipe.
- **Effort**: S-M
- **Migration/rollback concern**: none.

### SEC-402: Raw token-endpoint response bodies embedded in exception messages
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `GoogleClientFactory.cs:140-143` — `throw new InvalidOperationException("Google token exchange failed: " + responseBody)`; `SalesforceClientFactory.cs:734-737` — non-JSON error fallthrough returns `"Salesforce returned: " + trimmed` (raw body) into the exception message.
- **Current behavior**: Provider error bodies (arbitrary content, potentially reflective) ride in exception messages. Main connector paths catch and translate to safe results, but `GmailApiClientFactory`/legacy paths and any future caller can surface or log them.
- **Why it matters**: (INFERENCE) Exception messages are the most commonly logged strings; this undermines the otherwise-strict "exception type name only" logging discipline.
- **OS/product consequence**: Log-channel data-leak risk at the trust boundary.
- **Recommendation**: (PROPOSAL) Parse and whitelist `error`/`error_description` only (Salesforce already tries; delete the raw fallthrough); never concatenate raw bodies.
- **Deletion/simplification opportunity**: yes (small).
- **Dependencies**: none.
- **Tests/measurements required**: unit test with hostile error body.
- **Effort**: S
- **Migration/rollback concern**: none.

### SEC-403: Salesforce login-host allowlist accepts any `*.salesforce.com` and `*.site.com`
- **Severity**: Low
- **Confidence**: Medium
- **Evidence**: `SalesforceClientFactory.cs:541-545` (`IsAllowedLoginHost`).
- **Current behavior**: `login_url` (app-scope, operator-seeded, pinned per flow) may point at any host under those suffixes, including Experience Cloud tenant sites that can serve tenant-controlled content.
- **Why it matters**: (INFERENCE) The allowlist reads as a strong control but is broad; combined with a compromised app-config write it directs the token exchange (with client_secret in the form) to a tenant-controlled Salesforce-suffixed host.
- **OS/product consequence**: Defense-in-depth gap on the credential-bearing egress.
- **Recommendation**: (PROPOSAL) Restrict to `login.salesforce.com`, `test.salesforce.com`, `*.my.salesforce.com` (the identity-URL check at `SalesforceApiClient.cs:896-902` already uses exactly this tighter set — align them).
- **Deletion/simplification opportunity**: yes — one shared allowlist.
- **Dependencies**: none.
- **Tests/measurements required**: allowlist unit tests incl. `evil.site.com`.
- **Effort**: S
- **Migration/rollback concern**: orgs using community login URLs would need the tighter host registered explicitly.

### SEC-404: Plaintext default password embedded in a journal-eligible UiSurface synapse
- **Severity**: Medium
- **Confidence**: Medium
- **Evidence**: `src/DigitalBrain.Ui.Runtime/UiSurfaceRuntime.cs:108-127` — `Login(..., string? defaultPassword)` puts the password into `fields[].value`; `UiSurface` derives from `Synapse` (`UiSurfaces.cs:8`), and Neuron journals record fired/delivered synapses durably.
- **Current behavior**: If a caller passes a default password (dev convenience via `UserSessionNeuron`), it is serialized into the surface payload and durably journaled/broadcast to clients.
- **Why it matters**: (INFERENCE) Secrets in durable journals violate the "secret values must never be logged" rule stated in Pack.Contracts and outlive the session.
- **OS/product consequence**: Credential material in replayable timeline state.
- **Recommendation**: (PROPOSAL) Remove the `defaultPassword` parameter entirely; dev auto-login should exchange out-of-band, never through a rendered/journaled surface.
- **Deletion/simplification opportunity**: yes — delete the parameter.
- **Dependencies**: kernel `UserSessionNeuron` (caller) — outside this list; coordinate.
- **Tests/measurements required**: assert login surfaces never contain non-empty password values.
- **Effort**: S
- **Migration/rollback concern**: dev login UX needs an alternative prefill (username only).

### SEC-405: Pack signing and publisher trust exist but are enforced nowhere
- **Severity**: High
- **Confidence**: Medium (grep-verified absence of callers in `src/`; enforcement could be intended in a path not on this list)
- **Evidence**: `PackSignatureVerifier.VerifyPack` and `PublisherTrust.IsTrusted` have zero callers outside `src/DigitalBrain.Pack.Contracts/Trust/` (repo-wide grep); pack embodiment (`src/DigitalBrain.Kernel/Foundry/PackAlcEmbodier.cs`) consumes `NeuroPack`/`IPackBehavior` without signature checks.
- **Current behavior**: A `NeuroPack` with arbitrary C# `Code` can be compiled and loaded into the kernel without integrity or publisher verification; the signature fields are decorative.
- **Why it matters**: (INFERENCE) Packs are signed C# "embodied at runtime" per the OS vision — running unverified code is the single largest trust gap in the pack rail, even if today's entry paths are themselves gated by the approval rail.
- **OS/product consequence**: The marketplace/self-evolution trust chain has a hole between "approved" and "executed".
- **Recommendation**: (PROPOSAL) Enforce `PublisherTrust.IsTrusted` (or at minimum `VerifyPack`) in the embodiment path before compile/load, config-gated allowlist; unsigned packs allowed only in an explicit dev mode with logged warning (mirroring `PassThroughNeuronStateProtector`'s pattern).
- **Deletion/simplification opportunity**: no.
- **Dependencies**: Foundry subsystem audit (kernel side).
- **Tests/measurements required**: embodiment rejects tampered/self-signed packs; accepts allowlisted signed pack.
- **Effort**: S-M
- **Migration/rollback concern**: existing locally-authored packs need a dev signing story.

### PROD-400: Gmail send duplicate suppression is non-atomic and subject to search-indexing lag
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `GoogleGmailApiClient.cs:41-68` — `SendAsync` searches `in:sent rfc822msgid:{messageId}` (max 1) then sends; no `GmailSendStatus.OutcomeUnknown` exists (`GmailTool.cs:26-34`).
- **Current behavior**: A retry after an outcome-unknown send (network cut after Gmail accepted) re-runs the search; recently sent mail is not reliably indexed for `rfc822msgid:` search within seconds, so the duplicate check can miss and the message is sent twice. Provider-level status for a thrown send is `Unavailable` (via the neuron catch), which reads as retryable.
- **Why it matters**: (INFERENCE) The kernel's `VerifyBeforeRetry`/outcome-unknown machinery is only as good as the provider verification primitive, and this one is eventually-consistent.
- **OS/product consequence**: User-visible duplicate emails — the exact failure the previewed/approved/idempotent mutation rail exists to prevent.
- **Recommendation**: (PROPOSAL) (a) verify via `threads.get`/`messages.list` with the Message-ID *header* fetch on recent SENT messages instead of search, or track the returned message id durably keyed by UniqueTag; (b) add an explicit `OutcomeUnknown` send status so the kernel's verify-before-retry path engages rather than plain retry; (c) document the residual window.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: conversation effect rail (kernel) — status mapping.
- **Tests/measurements required**: fake-service test: send succeeds, response lost, retry with lagging search index must not double-send.
- **Effort**: M
- **Migration/rollback concern**: none.

### PROD-401: Salesforce apply is check-then-update without a conditional write
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `SalesforceApiClient.cs:373-392` — read current value → compare to `OriginalValue` → `UpdateAsync` → verify read.
- **Current behavior**: A concurrent writer between the conflict check and the update is silently overwritten; post-write verification only confirms the final value equals the desired value.
- **Why it matters**: (INFERENCE) Small window, single-field updates, human-approved cadence — low practical risk, but the `Conflict` guarantee is weaker than it reads.
- **OS/product consequence**: "Preview matched" claim can be stale at apply time.
- **Recommendation**: (PROPOSAL) Note the limitation in the contract; Salesforce REST lacks ETag on sObject PATCH, so consider including `LastModifiedDate` in the prepared document and re-checking it in the same read.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: none.
- **Tests/measurements required**: interleaved-writer fake test documenting behavior.
- **Effort**: S
- **Migration/rollback concern**: prepared documents gain a field (versioned; Version=1 check exists).

### PROD-402: GoogleConnector.ValidateConfigAsync demands redirect_uri that every other path defaults
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `GoogleConnector.cs:36` (Descriptor.RequiredConfigKeys includes `RedirectUriKey`) + `39-51` (ValidateConfig fails on missing key) vs `BeginAuthAsync:78-88` and `CompleteAuthAsync:282-290` (fall back to `DefaultRedirectUri`/config). `GoogleAppConfigSeeder` seeds redirect only when configured.
- **Current behavior**: If only client_id/secret are present in the store (e.g. seeded from a config source without redirect), every Gmail read/send returns `ConfigurationMissing` even though the auth flow itself would work with the default redirect.
- **Why it matters**: (INFERENCE) Inconsistent validity definition produces a hard-to-diagnose "configuration missing" that contradicts the working default; Salesforce's `TryValidateAppConfig` treats redirect as optional-with-default — the two connectors disagree about what "configured" means.
- **OS/product consequence**: On-demand auth journey dead-ends for a config shape the system otherwise supports.
- **Recommendation**: (PROPOSAL) Align: validate redirect only if present (normalize), like Salesforce; or seed the default explicitly.
- **Deletion/simplification opportunity**: yes (one key out of RequiredConfigKeys).
- **Dependencies**: none.
- **Tests/measurements required**: ValidateConfig test with clientId+secret only.
- **Effort**: S
- **Migration/rollback concern**: none.

### PROD-403: SalesforceConnector replays an auth challenge even when the credential is already Ready
- **Severity**: Low
- **Confidence**: Medium
- **Evidence**: `SalesforceConnector.cs:73-79` replays whenever a replayable challenge exists; `GoogleConnector.cs:104-117` gates replay on `ResolveAuthorization(...) != Ready`.
- **Current behavior**: A user who completed auth but still has a live pending challenge record gets sent back to Salesforce consent instead of "already connected".
- **Why it matters**: (INFERENCE) Minor UX/consistency divergence between the two implementations of the same contract; another ARCH-403 symptom.
- **OS/product consequence**: Redundant consent prompts.
- **Recommendation**: (PROPOSAL) Add the Ready check (or get it free from the shared state machine).
- **Deletion/simplification opportunity**: yes via ARCH-403.
- **Dependencies**: ARCH-403.
- **Tests/measurements required**: BeginAuth-after-complete contract test.
- **Effort**: S
- **Migration/rollback concern**: none.

### PROD-404: Swallowed store-read failure turns outages into "credential form needed"
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `GoogleConnector.cs:60-70` — `catch { }` around the app-config read; execution continues with empty `values` and returns `AuthChallenge("credential-form-needed", IsForm: true)` at line 90-93.
- **Current behavior**: A transient storage/DataProtection failure is indistinguishable from "operator never configured Google": the user is shown a credential form.
- **Why it matters**: (INFERENCE) Misdiagnosis at the trust boundary; a user might re-enter (and overwrite) real credentials during an outage. Also the only bare `catch { }` in the audited surface.
- **OS/product consequence**: Fail-open UX on infrastructure failure instead of fail-closed "unavailable".
- **Recommendation**: (PROPOSAL) Let the exception propagate to the neuron's Unavailable mapping (it already handles this), or catch narrowly and return an explicit unavailable challenge kind.
- **Deletion/simplification opportunity**: yes — delete the catch and the nullable-store scaffolding (CLEAN-400).
- **Dependencies**: CLEAN-400.
- **Tests/measurements required**: throwing-store test asserting Unavailable (not form).
- **Effort**: S
- **Migration/rollback concern**: none.

### REL-400: No retry/backoff/rate-limit strategy in either integration
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: No backoff/retry code in `GoogleGmailApiClient.cs` or `SalesforceApiClient.cs`; Gmail 429/5xx surface as exceptions → `Unavailable`; Salesforce maps `REQUEST_LIMIT_EXCEEDED` to `LimitReached` (`SalesforceApiClient.cs:886-888`) but nothing throttles or retries.
- **Current behavior**: Every provider hiccup is a user-visible "try again later"; Gmail's per-operation fan-out (up to 64 gets + token refresh) amplifies quota pressure with no shaping. Whether Google.Apis' default `ExponentialBackOff` covers 429 for these calls is **unverified** (FRAME-400).
- **Why it matters**: (INFERENCE) An OS-grade connector layer needs deterministic behavior under provider throttling — especially with multiple kernel replicas multiplying load.
- **OS/product consequence**: Availability and quota exhaustion under normal multi-user load.
- **Recommendation**: (PROPOSAL) Provider-classified retry policy (retryable statuses, jittered backoff, budget per operation) at the client factory layer; surface `LimitReached` with retry-after to the kernel retry scheduler (`ScheduleRetryAsync` already exists to receive it).
- **Deletion/simplification opportunity**: no.
- **Dependencies**: PERF-400/401/402 reduce pressure first.
- **Tests/measurements required**: fake 429 sequences; assert bounded retries + LimitReached mapping.
- **Effort**: M
- **Migration/rollback concern**: none.

### REL-401: Auth/permission failure classification by exception-message substring matching
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `GmailNeuron.cs:559-568` (`"credential"`, `"unauthorized"`, ... in message); `SalesforceReadNeuron.cs:605-623` (`"permission"`, `"reconnect"`, `"credential"` ...); `SalesforceApiClient.Classify` (`SalesforceApiClient.cs:884-894`).
- **Current behavior**: Any exception whose (base) message happens to contain e.g. "permission" or "credential" — including bugs, serialization errors, or provider messages in other contexts — is classified as an auth failure and triggers the reconnect flow (which can wipe/replace pending state).
- **Why it matters**: (INFERENCE) Misclassification converts transient faults into "reconnect Google/Salesforce" prompts and can churn auth state; matching `"reconnect"` even makes the classification self-referential (their own safe messages contain it).
- **OS/product consequence**: Wrong-branch behavior at the authorization boundary; user trust erosion via spurious reconnect demands.
- **Recommendation**: (PROPOSAL) Classify on typed signals: `GoogleApiException.HttpStatusCode` (already partially done), `TokenResponseException.Error.Error == "invalid_grant"`, ForceException error codes / HTTP status — fall back to Unavailable, never to NeedsAuth, on unknown text.
- **Deletion/simplification opportunity**: yes — deletes substring lists.
- **Dependencies**: FRAME-400 (verify exception types in pinned versions).
- **Tests/measurements required**: classification table tests with representative provider exceptions.
- **Effort**: S-M
- **Migration/rollback concern**: none.

### REL-402: ForceClient calls are not cancellable; DeveloperForce.Force is dormant
- **Severity**: Note
- **Confidence**: High (code), Low (ecosystem status — unverified, FRAME-400)
- **Evidence**: `SalesforceApiClient.cs` throughout — `ct.ThrowIfCancellationRequested()` before/after every `client.*Async(...)` call because the DeveloperForce API takes no CancellationToken.
- **Current behavior**: Cancellation is checked at call boundaries only; an in-flight HTTP call runs to completion/timeouts.
- **Why it matters**: (INFERENCE) Grain-call timeouts can leave provider requests running; and a dormant dependency on the mutation path is supply-chain debt.
- **OS/product consequence**: Slow cancellations; long-term maintenance risk on the CRM connector.
- **Recommendation**: (PROPOSAL) Wrap ForceClient with an HttpMessageHandler-injected HttpClient (it supports handler injection) carrying the CT via `HttpClient.Timeout`/linked tokens, or plan migration to first-party REST calls.
- **Effort**: M
- **Dependencies**: FRAME-402.
- **Deletion/simplification opportunity**: no.
- **Tests/measurements required**: cancellation latency measurement.
- **Migration/rollback concern**: none.

### PERF-400: Token-endpoint round trip on (nearly) every provider operation
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `GmailApiClientFactory.cs:8-27` + `GoogleCredentialFactory.cs:9-18` (fresh `UserCredential` with null AccessToken per operation → refresh on first use); `SalesforceClientFactory.CreateOAuthSessionAsync:597-625` (refresh_token grant per `CreateAsync`); stored `access_token` values are written once at exchange and never reused.
- **Current behavior**: Every read/send pays an extra OAuth token request; under INO's multi-tool turns this multiplies latency and hits provider token-endpoint rate limits.
- **Why it matters**: (INFERENCE) Latency + rate-limit exposure scale with usage; also more secret material in flight than necessary.
- **OS/product consequence**: Sluggish tool turns; avoidable 429s from token endpoints.
- **Recommendation**: (PROPOSAL) Cache access tokens per principal (in-memory with expiry, or persist expiry alongside the encrypted access_token that is already stored) and reuse until near-expiry.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: REL-400.
- **Tests/measurements required**: token-endpoint call count per N operations before/after.
- **Effort**: S-M
- **Migration/rollback concern**: cached-token invalidation on auth failure must fall back to refresh.

### PERF-401: Gmail metadata window issues sequential N+1 message gets with client-side filtering
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `GoogleGmailApiClient.cs:280-302` — `foreach (var id in ids)` with an awaited `Users.Messages.Get` per id (up to `MaxCandidates` = 64); filters applied post-fetch at 304-308.
- **Current behavior**: A thread/message list costs up to 65+ serial HTTP round trips; sender/subject/date filters discard already-fetched metadata.
- **Why it matters**: (INFERENCE) Latency is O(candidates) serial; quota consumption is maximal for the information returned. Batch HTTP (or bounded parallelism) would cut wall time ~10x. (Gmail batch endpoint suitability unverified — FRAME-400.)
- **OS/product consequence**: Slow INO Gmail answers; quota pressure.
- **Recommendation**: (PROPOSAL) Bounded-concurrency gets (e.g. 8 in flight) as a safe first step; evaluate batch endpoint; consider pushing exact-match filters into `q` while keeping coverage accounting.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: REL-400 (retry policy should exist before adding parallelism).
- **Tests/measurements required**: wall-time per 64-candidate window before/after.
- **Effort**: S-M
- **Migration/rollback concern**: none.

### PERF-402: Salesforce global describe + object describe on every request, twice per mutation
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `SalesforceApiClient.ResolveObjectAsync:522-577` calls `GetObjectsAsync` + `DescribeAsync` per invocation; both `PreviewUpdateAsync` and `ApplyUpdateAsync` call it (so a full preview+apply cycle performs 4 describe-class calls plus reads).
- **Current behavior**: Describe metadata (stable on the minutes-to-days scale) is re-fetched every call.
- **Why it matters**: (INFERENCE) Describe calls are among the heaviest Salesforce REST calls; this dominates read latency and burns API limits. Apply's re-resolution is a *correctness feature* (metadata drift detection) — caching must preserve a freshness bound rather than blind reuse.
- **OS/product consequence**: Slow CRM answers; REQUEST_LIMIT_EXCEEDED sooner.
- **Recommendation**: (PROPOSAL) Short-TTL per-org describe cache keyed by `SalesforceProviderScope` (e.g. 60s), bypassed on Conflict/metadata-mismatch.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: none.
- **Tests/measurements required**: API call counts per preview+apply before/after; drift test still detects mismatch.
- **Effort**: M
- **Migration/rollback concern**: none.

### PERF-403: New HttpClient per token exchange
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `GoogleClientFactory.cs:134`, `SalesforceClientFactory.cs:658` — `new HttpClient(...)` per call.
- **Current behavior**: Socket/TLS handshake per token request; combined with PERF-400 this happens per operation.
- **Recommendation**: (PROPOSAL) Static `SocketsHttpHandler`-backed shared client (the injectable `HttpMessageHandler` test seam already exists).
- **Why it matters**: (INFERENCE) socket churn/port exhaustion under load.
- **OS/product consequence**: throughput ceiling.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: PERF-400.
- **Tests/measurements required**: none beyond existing.
- **Effort**: S
- **Migration/rollback concern**: none.

### FRAME-400: Pinned SDK usage could not be verified against current docs (Context7 quota exhausted)
- **Severity**: Note
- **Confidence**: High (that verification did not happen)
- **Evidence**: Context7 `resolve-library-id` returned "Monthly quota exceeded" for both `Google.Apis.Auth` (1.75.0 pinned, `Directory.Packages.props:114`) and `DeveloperForce.Force` (2.1.0, line 116) during this audit session.
- **Current behavior**: Statements in this audit about `UserCredential`/`GoogleAuthorizationCodeFlow` refresh semantics, Google.Apis backoff defaults, Gmail batch endpoints, and ForceClient capabilities are from model knowledge, not verified docs.
- **Recommendation**: (PROPOSAL) Re-verify SEC-400 (PKCE helper availability), REL-400 (default backoff behavior), PERF-401 (batch endpoint) against vendor docs before implementing; record in the fix PRs.
- **OS/product consequence**: none direct; audit-confidence qualifier.
- **Why it matters**: audit honesty requirement.
- **Deletion/simplification opportunity**: no. **Dependencies**: SEC-400, REL-400, PERF-401, REL-402. **Tests/measurements required**: n/a. **Effort**: S. **Migration/rollback concern**: none.

### FRAME-401: Hand-rolled Google token exchange bypasses Google.Apis.Auth's flow machinery
- **Severity**: Low
- **Confidence**: Medium (see FRAME-400)
- **Evidence**: `GoogleClientFactory.ExchangeAuthorizationCodeAsync:107-159` posts the form manually; `GoogleAuthorizationCodeFlow` (already referenced via `GoogleCredentialFactory`) provides `ExchangeCodeForTokenAsync` with clock-skew handling and PKCE support in current versions.
- **Current behavior**: Two token paths exist — manual exchange for the code grant, library flow for refresh — with different error surfaces.
- **Why it matters**: (INFERENCE) Duplicates vendor-maintained logic; the manual path is where PKCE and body-leak issues (SEC-400/402) live.
- **OS/product consequence**: More bespoke security-critical code than necessary.
- **Recommendation**: (PROPOSAL) Either standardize on the library flow (verify version support first) or keep manual but fold into the ARCH-403 shared machine.
- **Deletion/simplification opportunity**: yes.
- **Dependencies**: FRAME-400, ARCH-403.
- **Tests/measurements required**: existing OAuth security tests must pass unchanged.
- **Effort**: M
- **Migration/rollback concern**: token response shape differences (refresh_token presence) must be preserved.

### FRAME-402: Mutation-path dependency on dormant DeveloperForce.Force 2.1.0
- **Severity**: Note
- **Confidence**: Low (dormancy unverified this session — FRAME-400)
- **Evidence**: `Directory.Packages.props:116`; used for all Salesforce I/O including mutations.
- **Why it matters**: (INFERENCE) An unmaintained client on the highest-risk path (external writes) accumulates API-version and security drift; api_version is at least configurable (`v60.0` default).
- **Recommendation**: (PROPOSAL) Evaluate replacing ForceClient with direct REST calls through the existing token machinery (the client surface used is small: query, search, describe, getObjects, update, userinfo, queryContinuation).
- **OS/product consequence**: long-term maintainability of the CRM connector.
- **Current behavior**: works today. **Deletion/simplification opportunity**: yes eventually (drops a dependency). **Dependencies**: REL-402. **Tests/measurements required**: contract tests against fake handler already exist to support a swap. **Effort**: L. **Migration/rollback concern**: response-shape parity.

### CLEAN-400: Dead null-store scaffolding and speculative nullability in GoogleConnector
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `GoogleConnector.cs:56` (`IPackConfigStore? store = _store;` from a non-nullable field), 90 (`store is null` can never be true), 69 (`catch { }` — see PROD-404).
- **Current behavior**: Misleading defensive code implying the store can be absent.
- **Recommendation**: (PROPOSAL) Delete the nullable dance; handle store failure explicitly.
- **Why it matters**: (INFERENCE) obscures the real failure mode it papers over.
- **OS/product consequence**: none direct. **Deletion/simplification opportunity**: yes. **Dependencies**: PROD-404. **Tests**: compile + existing. **Effort**: S. **Migration/rollback**: none.

### CLEAN-401: Unreachable final branch in SalesforceClientFactory.CreateOAuthSessionAsync
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `SalesforceClientFactory.cs:634-641` — reachable only when `HasOAuthCredential` was true yet neither the refresh-token-with-app-config branch (597) nor the access-token branch (627) matched, which the `HasOAuthCredential` definition (103-105) makes impossible; the `RequireConnectedAppConfig` at 636 throws first in the residual case anyway.
- **Recommendation**: (PROPOSAL) Delete lines 634-641; replace with a throw describing the invariant.
- **Current behavior**: dead code. **Why it matters**: (INFERENCE) reads as a supported credential mode that doesn't exist. **OS/product consequence**: none. **Deletion/simplification opportunity**: yes. **Dependencies**: none. **Tests**: existing. **Effort**: S. **Migration/rollback**: none.

### CLEAN-402: Unused/vestigial API surface across the factories
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `GoogleClientFactory.OAuthCodeVerifierKey` (line 30, never used — the PKCE ghost); `AccessTokenKey` written at exchange (`GoogleClientFactory.cs:149`) but never read; `SalesforceClientFactory.CreateOAuthStartUrl(values, flowReference)` (354-360) ignores `values`; `DigitalBrainBuilderExtensions.WithOptionalEnvironment` (309-321) has no callers; `SemanticFilterOperator.Set` (`SemanticIntent.cs:58`) has no compiler support in `SalesforceApiClient.CompileFilters`.
- **Recommendation**: (PROPOSAL) Delete or implement each; the code-verifier key should be deleted by SEC-400's fix (which introduces a real one).
- **Current behavior**: dead surface. **Why it matters**: (INFERENCE) each is a false affordance. **OS/product consequence**: none. **Deletion/simplification opportunity**: yes. **Dependencies**: SEC-400. **Tests**: compile. **Effort**: S. **Migration/rollback**: `access_token`/verifier keys may exist in stored packs; readers must tolerate their absence (they already do).

### CLEAN-403: Demo/sample surfaces shipped inside the packable Ui.Runtime; duplicated helper
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `UiSurfaceRuntime.cs:8-321` (`UiSurfaceSamples` with hardcoded demo tasks/charts) in the `DigitalBrain.Ui.Runtime` NuGet-packable assembly; `WithCommon` duplicated verbatim at 298-320 and 827-849; `UiSurfaceSamples.SynapseAction` is a pure pass-through to `UiSurfaceActions.SynapseAction`.
- **Recommendation**: (PROPOSAL) Move samples to tests/dev tooling; keep `UiSurfaceLiveData`; single `WithCommon`.
- **Current behavior**: demo payloads are product API. **Why it matters**: (INFERENCE) sample data can leak into real feeds and bloats the protocol package. **OS/product consequence**: none direct. **Deletion/simplification opportunity**: yes (~300 lines). **Dependencies**: kernel callers (`SystemNeurons.cs`, `UserSessionNeuron.cs`) use both classes — coordinate. **Tests**: CoreBoundaryTests reference these — update. **Effort**: S. **Migration/rollback**: none.

### CLEAN-404: ISalesforceApiClient default interface methods silently report capabilities as unavailable
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `ISalesforceApiClient.cs:12-39` — all semantic reads and both mutation methods have DIM bodies returning `Unsupported`/`Unavailable`.
- **Current behavior**: A partial implementation compiles and quietly answers "unavailable" at runtime instead of failing at build time; the legacy string-JSON methods (7-10) coexist with the typed page model.
- **Recommendation**: (PROPOSAL) Make members abstract (the only implementation implements everything); delete legacy string methods once neuron callers migrate to typed reads.
- **Why it matters**: (INFERENCE) capability holes should be compile errors in an OS kernel boundary. **OS/product consequence**: latent partial-connector risk. **Deletion/simplification opportunity**: yes. **Dependencies**: SalesforceReadNeuron legacy read methods. **Tests**: compile. **Effort**: S. **Migration/rollback**: none.

### CLEAN-405: Legacy RfwCard/IChatNeuron duplicate the UiSurface RFW path; UI vocabulary sprawl
- **Severity**: Note
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Ui.Contracts/Ui/RfwCard.cs:8-20` (comment admits "the canonical SDUI model stays UiSurface"); three vocabularies in `UiSurfaces.cs` (`NeuronUiKit` neuron:/forui:, `UiKitVocabulary` ui:, bare kind strings) plus raw node names like `"fcard"`/`"text"` used directly in `UiSurfaceRuntime.cs:470-489`.
- **Recommendation**: (PROPOSAL) Fold RfwCard consumers onto `UiSurface.ForRfw`; converge on the ui: vocabulary; replace raw string node names in Ui.Runtime with the constants.
- **Current behavior**: duplicate payload kinds, mixed vocabularies. **Why it matters**: (INFERENCE) every extra vocabulary is client renderer surface that must be maintained. **OS/product consequence**: UI contract drift. **Deletion/simplification opportunity**: yes. **Dependencies**: Flutter client audit. **Tests**: renderer contract tests. **Effort**: M. **Migration/rollback**: journaled RfwCard synapses must still deserialize (keep the record, deprecate emission).

### TEST-400: Strong OAuth/contract test coverage; identified untested hazards
- **Severity**: Medium
- **Confidence**: Medium (based on test-file grep, not full test read)
- **Evidence**: Coverage exists: `tests/DigitalBrain.Tests/Integrations/IConnectorContractTests.cs`, `OAuthConnectorSecurityTests.cs`, `GoogleGmailApiClientTests.cs`, `tests/DigitalBrain.Salesforce.Tests/*` (mutation, continuation, OAuth-start, semantic reads with fake token handlers). Not found: tests for Gmail send retry under search-index lag (PROD-400), Salesforce concurrent-writer apply race (PROD-401), GoogleConnector store-outage path (PROD-404), Google PKCE absence (SEC-400 — a security test suite that doesn't assert PKCE for Google), Ui.Runtime login-surface secret handling (SEC-404).
- **Current behavior**: The happy/adversarial OAuth paths are tested; the flagged failure windows are not.
- **Recommendation**: (PROPOSAL) Add the five tests above alongside their fixes; they are the acceptance criteria for PROD-400/404, SEC-400/404.
- **Why it matters**: (INFERENCE) the untested paths are exactly the mutation-safety and fail-closed windows. **OS/product consequence**: regressions in the trust rail would land silently. **Deletion/simplification opportunity**: no. **Dependencies**: the referenced findings. **Tests/measurements required**: themselves. **Effort**: M. **Migration/rollback**: none.
