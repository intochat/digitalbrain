# DigitalBrain Google SDK-First Execution Plan (Grok-orchestrated)

> **For agentic workers:** one writer per worktree; orchestrator (Claude session) creates worktrees, writes briefs, verifies every claim itself, merges in lane order. Workers never merge, never push, never touch files outside their lane surface.

**Design:** `docs/superpowers/specs/2026-08-01-digitalbrain-google-sdk-productization-design.md` (decisions G1–G7)
**Parent:** `docs/superpowers/specs/2026-07-30-neurons-synapses-behaviors-design.md`
**Ground at start:** `feature/digitalbrain-architecture-continuation` @ `4a04cceb` + PRM WIP (committed in Lane g0)

## Absolute gates (every lane)

- TDD: failing proof first; never delete red proofs (Explicit if live-only). Never weaken assertions.
- Five Steps per slice; CodeGraph before/after structural edits; Context7/microsoft-learn before any `Google.Apis.*` API use.
- No secrets in git/journals/traces/prompts/manifests/vectors. No placeholder credentials.
- Journaled wire aliases (`db.mcp.*`, `db.google.*`) are immutable (O1). New synapses get new aliases.
- Commit at green boundaries only; one logical change; grill answers in the message. **Never push** — owner-only.
- Root gate before any lane-complete claim: `dotnet build DigitalBrain.slnx -c Release` + root `dotnet test` (or per-assembly `dotnet exec` if MTP handshake is broken — document which).
- Bridge shells: set `$env:OS='Windows_NT'` (and standard preamble) before any build; long commands via `Start-Process` + log + poll.

## Lanes

### Lane g0 — Preflight (orchestrator inline, no Grok)
1. Commit PRM WIP as historical commit (G5): the 2 modified + 2 untracked PRM files, message records it ships dead and dies in g4.
2. Commit the two design/plan docs.
3. CodeGraph sync; `dotnet build DigitalBrain.slnx -c Release` baseline; record MTP `dotnet test` state.
4. Create worktrees `E:\intochat\_worktrees\g1-auth`, `g2-rail`, `g3-ops` off the branch; briefs in `E:\intochat\_streams\`.

### Lane g1 — Google auth + token custody (worktree g1-auth)
**Surface:** `src/modules/google/**` (new `Auth/` folder), `Directory.Packages.props` (add `Google.Apis.Gmail.v1`, `Google.Apis.Auth`), Google tests.
- `DurableGoogleTokenStore : IDataStore` over `IDurableValue<byte[]>` + `IDurablePayloadProtector` (rollback-on-failed-commit like `DurableMcpTokenCache`).
- `GoogleSignIn` internal: builds authorize URL via `GoogleAuthorizationCodeFlow` (offline, PKCE, state, redirect from config), exchanges code (`ExchangeCodeForTokenAsync`), constructs `UserCredential`/`GmailService`.
- L1 proofs against a **fake token endpoint** (flow initializer `TokenServerUrl` override): exchange, refresh, refresh-preserved-on-re-store, deny, expiry→re-park precondition.
- Does NOT touch the rail or the planner. Gmail neuron keeps compiling on the MCP path in this lane.

### Lane g2 — Sign-in rail rework (worktree g2-rail)
**Surface:** `src/core/mcp/DigitalBrain.Mcp/**`, `os/DigitalBrain.OS.Ui/McpOAuthCallbackEndpoints.cs` + `FlutterHttpContract` path, Integrations auth tests, `src/core/mcp/DigitalBrain.Mcp.Aspire.Hosting/**`.
- One flow (G3): delete `LocalLoopbackMcpAuthorizationCallback`, `EdgeMcpAuthorizationCallback` robot-GET branch, `AuthorizationMode`/`AuthorizationPreflight` keys, `mcp-authorization-mode` parameter, synthetic-token branch (`McpAuthorizationRail.cs:79-91`).
- Connect the missing wire: MCP callback handler (Salesforce path) awaits the app-callback-delivered code — consumer of `TakeCompletedCode`/hub; delete the never-awaited `CodeReady` if it ends with zero consumers.
- O2: pending authorization code protected-at-rest (payload protector) or not persisted.
- Callback path → `/oauth/callback` (G6) with the old path answering 404 (or redirect — worker picks, tests pin it).
- Rail parks on missing authorization for **any** provider mode-lessly; L1 branch matrix (happy/denied/state-mismatch/expiry) stays green with fakes.

### Lane g3 — Typed ops contracts (worktree g3-ops)
**Surface:** `src/modules/google/DigitalBrain.Modules.Google.Contracts/**`, `src/DigitalBrain.PublishGate.Tests/Contracts/GoogleVocabulary.cs`, catalog metadata tests.
- New synapses per design §3.2 (`GmailSearchRequest/Response`, `GmailGetMessageRequest/Response`, `GmailMessageHeader`) with new `db.google.*` aliases; bounded validation in constructors.
- PublishGate vocabulary updated to the new closed set; contracts assembly law: no `Google.Apis.*` reference.
- Handlers stubbed to fail-closed typed errors until g4 merges (tests scripted accordingly, marked to be re-pointed in g4).

### Lane g4 — Reflected SDK catalog + planner + facade (sequential, after g1+g3 merge; worktree g4-planner)
**Surface:** `src/modules/google/DigitalBrain.Modules.Google/**`, Google tests, Integrations Gmail tests.
- `SdkCatalogAdmission` (G7): activation-time enumeration of the allowlisted read-only request surface (`Users.Messages.List/Get`, `Users.Threads.List/Get`, `Users.Labels.List`), XML-doc descriptions, materialized `AIFunction`s binding args → typed request → module-owned `GmailService`; cached per activation. Allowlist tests: mutating verbs never admitted; unknown members never admitted; descriptions non-empty.
- Planner loop re-targeted to the reflected catalog; results bounded to `GmailMessage` before conversation re-entry; id-mismatch guard preserved.
- Typed op handlers (`GmailSearchRequest`, `GmailGetMessageRequest`) call the facade **directly** — no model in the loop.
- Delete: `Gmail.Admit.cs`, Google `McpServerDefinition`/endpoint override, Google use of `McpRuntime`/session factory, PRM handler + tests (the G5-committed files die here).
- PublishGate law: Google module must not reference `ModelContextProtocol.*`.
- L1: fake Gmail REST host (service `BaseUri` override) or scripted facade edge — same TestBrain ergonomics as the old MCP edge.

### Lane g5 — Naming sweep + README + integration (orchestrator or single worker, after g2+g4)
- Provider-neutral renames (CLR only; aliases untouched): `McpAuthorization*` → `Authorization*` where now generic, hosting types, "Edge" eliminated everywhere in names and text.
- `GoogleHostingExtensions` description text (REST, not MCP); README Built/Designed rows for Gmail REST + 7-day testing-mode consent note.
- Root gate + Flutter gates if `clients/` touched.

### Lane g6 — Live proof (owner-in-the-loop, after g5)
- Owner re-registers redirect `http://localhost:5080/oauth/callback` in Google console (G6) — orchestrator prompts when reached.
- `aspire run` → chat "Read my last three emails" → sign-in card → real consent → same-Task continuation → journals quoted (`read_neuron_journal`, `read_chat_transcript`). Typed-op live proof (search+get). Explicit suite run.

## Merge order
g0 → (g1 ∥ g2 ∥ g3) → g4 → g5 → g6. Orchestrator verifies every worker claim (build + owning-project tests minimum) before merging; root gate at g4, g5. Conflicts on shared files (`Directory.Packages.props`, `FlutterHttpContract`) resolved by orchestrator only.

## Worker handoff format (every lane)
```
lane: / base_sha: / head_sha: / commits: / files_changed: / files_deleted:
tests_added: / red_command_and_failure: / green_commands_and_results:
build_command_and_result: / remaining_risks: / scope_deviations:
```
