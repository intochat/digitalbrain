# 06 — Security Threat Model

Threat model for DigitalBrain, prioritized by trust impact. Each threat: asset · attacker · trust boundary · attack path · existing control · control weakness · mitigation · validation · residual risk. Findings are cross-referenced to [findings-register.md](findings-register.md).

**Framing note.** The system has two security postures. The **INO/V2 path is fail-closed and well-controlled**; most residual risk there is hardening. The **neuron/self-evolution/Foundry path is where the exploitable and near-exploitable defects concentrate.** Several Critical findings are *control-absence* defects whose live reachability is currently limited by missing entry-point wiring — they are rated by the severity of the absent control, with reachability stated explicitly, because the product roadmap intends to wire exactly those entry points.

## Priority 1 — Untrusted code execution inside the trusted process

- **Asset:** the kernel process (holds all connector tokens, DataProtection keys, cluster identity).
- **Attacker:** a prompt-injected LLM producing code that passes the static gate; a socially-engineered approver; any cluster-internal caller.
- **Boundary:** self-evolution rail → code executor.
- **Attack path:** `FoundryRequest`/direct `RunGeneratedCode` → `InProcessAlcExecutor` compiles + loads + invokes generated C# **in-process at full trust**. The gate is bypassable (`typeof(bannedType)` renders without the trailing dot the exclusion expects; reflection `GetType().GetMethod().Invoke()` is un-excluded; `dynamic`/null-symbol nodes are skipped; `System.Environment` is not excluded → read all env/secrets; `Environment.Exit` kills the silo). Deploy tier applies **no gate**. Automation `ScriptRunner` gates a **zero-reference** compilation → always empty → no-op.
- **Existing control:** static `CapabilityGate`; approval rail; a real `OutOfProcessSandbox`.
- **Weakness:** gate is "not a security boundary" (its own comment); the real sandbox is **dead code** (registered, zero consumers); approval is forgeable and bypassable. (`foundry:SEC-300/301/302/304/308/151`, `SEC-153`, `REL-300`)
- **Mitigation (fail-closed):** stop registering `InProcessAlcExecutor`; route **all** execution (Run, Deploy, scripts) through `OutOfProcessSandbox` with wall-clock timeout, memory/CPU caps, and no ambient host capabilities (target: WASM). Gate the Deploy tier. Make executor grains reachable only from the apply registry. Until done, disable Foundry by default.
- **Validation:** escape-attempt tests (reflection, `dynamic`, env read, `Exit`, infinite loop) that **must fail to execute**; a test asserting no in-process executor is registered.
- **Residual risk after mitigation:** out-of-process still runs as the host OS user (no container/seccomp) — medium until WASM/container isolation lands.

## Priority 2 — Forged / unbound self-evolution approvals

- **Asset:** the governance guarantee that only human-approved changes apply.
- **Attacker:** any component that can deliver a `SelfEvolutionDecision`; a compromised or buggy neuron.
- **Boundary:** self-evolution decision handler.
- **Attack path:** deliver `SelfEvolutionDecision{ProposalId, Approved:true, DecidedBy:"anything"}` → grain checks only that `DecidedBy` is non-empty → approved → apply runs (incl. code exec).
- **Existing control:** the allowlisted, fail-closed apply registry.
- **Weakness:** no approver authentication; approval not bound to proposal content; `RequiresHumanApproval` never read; proposer-supplied risk trusted. (`kernel-runtime:SEC-050/051/052`, `core:PROD-001`)
- **Mitigation:** adopt the INO evidence model — principal-bound, content-hashed, single-use decisions verified by the grain; enforce `RequiresHumanApproval`; classify risk in-kernel. Put the rail in the TCB with a tenant boundary.
- **Validation:** negative tests — unauthenticated decider rejected; decision whose content-hash ≠ proposal rejected; replayed decision rejected.
- **Residual risk:** low once bound to the (already-strong) INO identity/evidence model.

## Priority 3 — Authentication fail-open and credential exposure

- **Assets:** user identity, connector credentials, DataProtection keys.
- **Attacker:** first network client to reach a fresh deployment; anyone with blob-container read; anyone with journal read.
- **Attack paths & weaknesses:**
  - **`DevAutoLogin` enables `admin/admin` in any environment** and bypasses existing-account passwords (`kernel-hosting:SEC-100`).
  - **First-user provisioning defaults on and grants admin** — first login on a fresh deploy wins the admin identity (`kernel-hosting:SEC-101`).
  - **Plaintext passwords journaled** into the durable, replayable, timeline-readable journal (`core:SEC-001`); password hash+salt also journaled (`core:SEC-002`).
  - **DataProtection key ring stored unencrypted** in the same blob container as the connector ciphertext it protects — blob read = plaintext credentials (`kernel-hosting:SEC-200`, verified vs MS Learn).
- **Existing control:** V2 session auth is strong; connector tokens are per-value encrypted.
- **Mitigation:** environment-gate `DevAutoLogin` (Development only, never config-flippable in Production); require an out-of-band bootstrap secret for first-user provisioning; remove password fields from journaled synapse vocabulary (authenticate outside the journal); `ProtectKeysWith*` the key ring (separate key vault/managed identity). Delete the legacy `UserSessionNeuron`/`DevAuth` authority entirely and converge on the V2 session authority.
- **Validation:** boot test proving Production rejects `DevAutoLogin`; test asserting no password material reaches any journal; key-ring-at-rest encryption test.
- **Residual risk:** low after convergence on V2 identity.

## Priority 4 — Prompt injection and tool-output poisoning across connectors

- **Asset:** the integrity of INO's actions on the user's behalf.
- **Attacker:** an email sender or CRM record author placing instructions in content INO reads.
- **Boundary:** connector read output → LLM → proposed action / generated code.
- **Attack path:** malicious email/CRM text → INO summarizes/acts → (a) proposes an unwanted mutation, or (b) if wired, flows into `FoundryRequest` → generated code. Model output is treated as near-trusted (markdown-fence extraction, no structured-output contract) (`foundry` LLM notes).
- **Existing control:** every mutation is previewed and approved (INO path); Foundry is not currently wired to connector content.
- **Weakness:** indirect injection can craft a *plausible* proposed action a human rubber-stamps; no provenance/quarantine on connector-derived text; no structured-output contract.
- **Mitigation:** treat all connector content as untrusted data, never instructions; structured-output contracts for any model→action step; never wire connector content into code generation; make previews show *why* an action was proposed (provenance) so injected instructions are visible; keep human approval mandatory (fixes the forged-approval prerequisite first).
- **Validation:** injection corpus tests on read→propose; assert connector content cannot reach `FoundryRequest`.
- **Residual risk:** medium — social-engineering of approvals is inherent; mitigated by provenance-rich previews.

## Priority 5 — SSRF via connector/automation HTTP

- **Asset:** internal network, cloud metadata endpoints.
- **Attacker:** generated automation/pack code; a poisoned tool output supplying a URL.
- **Attack path:** `CapabilityBroker.HttpGetAsync` fetches **any host** despite an interface comment promising an allowlist (`foundry:SEC-307/154`).
- **Existing control:** none effective.
- **Mitigation:** enforce a real egress allowlist (host + scheme + private-range/metadata blocklist) in the connector/automation host; deny by default.
- **Validation:** tests that private/metadata/link-local targets are refused.
- **Residual risk:** low after allowlist.

## Priority 6 — Supply chain and preview/alpha toolchain

- **Asset:** the build and the trusted core's stability.
- **Attack path / weakness:** `Directory.Build.targets` runs **unpinned remote npm** (`npx --yes @colbymchenry/codegraph`) on every post-clean build including CI; `.mcp.json`/`.codex` run `@latest` packages (`mcp-hosts-build:SEC-500`). The entire production stack is preview/alpha (`net11.0`, `aspnet:11.0-preview`, CI `dotnet-quality: preview`, Orleans preview + **Journaling alpha**) with no servicing guarantee (`mcp-hosts-build:PROD-500`, `kernel-runtime:FRAME-050`).
- **Mitigation:** pin the codegraph tool to a hash/version or remove it from the build; pin all MCP/codex tool versions; establish a plan to move the trusted core off alpha journaling (or formally accept + document the risk with a monitored servicing commitment).
- **Validation:** build with network egress blocked; SBOM/dependency pinning check in CI.
- **Residual risk:** medium while on alpha journaling.

## Priority 7 — Server-driven UI action forwarding

- **Asset:** the user's authority as exercised through the client.
- **Attacker:** a compromised or buggy server pushing UI descriptions; (historically) client widgets forwarding raw envelopes.
- **Attack path:** RFW server-driven UI selects `synapseType`/props that flow through `onEvent` into synapse dispatch; some widgets bypass `onEvent` to send raw envelopes; `UiKitLink` launches any URI with no scheme allowlist (`flutter-ui:SEC-800/801/802`).
- **Reachability caveat:** the raw-envelope path (`SEC-801`) is on the **orphaned legacy v1 client rail** (`flutter-runtime:ARCH-700`) — the client scope is never mounted, so it no-ops today; and the live V2 gateway **re-authorizes every action via capability action-tokens**. So the residual live risk is the server offering an over-privileged action binding, not the client forging one.
- **Mitigation:** delete the legacy v1 client rail; constrain the RFW widget dictionary's action surface to a declared allowlist; add a URI scheme allowlist to `UiKitLink`; keep all action authority server-side on the V2 capability-token model.
- **Validation:** RFW action-surface allowlist test; URI scheme test.
- **Residual risk:** low after v1 deletion.

## Priority 8 — Web OIDC implicit flow

- **Asset:** user identity tokens on web.
- **Weakness:** web sign-in uses the deprecated **implicit flow** (`id_token` in URL fragment), not auth-code+PKCE (`flutter-runtime:SEC-700`).
- **Mitigation:** move web to authorization-code + PKCE.
- **Residual risk:** low after change.

## Priority 9 — Durability, audit integrity, and DoS

- **Unbounded state / DoS:** neuron journals grow without bound (`kernel-runtime:PERF-100/REL-050`); in-process execution has no timeout (`foundry:REL-300`) — an infinite loop wedges a grain. **Mitigation:** journal compaction/archival; execution timeouts/resource caps.
- **Corrupted durable state / replay:** non-deterministic synapse ids and implicit positional Orleans field ids make replay fragile and reorder-sensitive (`core:REL-001`). **Mitigation:** deterministic ids, explicit `[Id]`, contract-freeze tests.
- **Audit-log tampering:** the audit trail is the same unbounded journal; there is no tamper-evidence (hash chaining) on the evolution/audit stream. **Mitigation:** hash-chain the governance/audit journal; store approvals with the INO evidence model.
- **Rollback failure:** "rollback" appends instead of restoring (`kernel-runtime:ARCH-051`); failed applies are terminal (`REL-103`). **Mitigation:** real snapshot+restore; retriable idempotent applies.
- **Rolling-deployment incompatibility:** kernel self-update is simulated (`kernel-runtime:ARCH-050`); no real drain/verify. **Mitigation:** implement or remove; validate with a real multi-replica boot/drain test (`kernel-hosting:FRAME-200` flags an Orleans double-config that may throw at boot).

## Cross-tenant / confused-deputy / privilege escalation (summary)

- **Cross-tenant:** the INO path is principal/tenant-scoped and tested (`dotnet-tests` scope-isolation). The gap is that `Synapse` carries no mandatory tenant (`core:ARCH-002`) and the self-evolution rail has no tenant boundary (`kernel-runtime:SEC-056`) — a multi-tenant deployment could leak across the neuron side. The `CapabilityIsolationGate` that "proves" isolation in tests **has no production caller** (`dotnet-tests:SEC-600`) — isolation is untested end-to-end.
- **Confused deputy:** INO acts with the user's grants under a fail-closed gateway; the risk is grant fail-open for unlisted tools (`connectors:ARCH-401`) and blanket bypass (`foundry:SEC-303`).
- **Privilege escalation:** the forged-approval → code-exec chain (P1+P2) is the escalation path; closing either link breaks it.

## Prioritized remediation (maps to [12](12-implementation-plan.md))

1. Disable Foundry/pack execution by default; route surviving execution out-of-process with caps (P1).
2. Bind self-evolution approvals to the INO evidence model; enforce `RequiresHumanApproval` (P2).
3. Fix auth fail-open + secret hygiene; delete the legacy auth authority (P3).
4. Enforce connector grant default-deny and SSRF egress allowlist (P4/P5).
5. Pin the build; plan off alpha journaling (P6).
6. Delete the legacy client rail; constrain RFW action surface (P7).
7. Durability: bounded journals, deterministic ids, real rollback, tamper-evident audit (P9).
