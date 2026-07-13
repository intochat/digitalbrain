# 03 — Operating-System Assessment

Evaluates DigitalBrain against the responsibilities of an operating system. For each primitive: **implemented-and-proven**, **partial**, **placeholder**, **aspirational-naming-only**, or **missing** — with the concrete contract it should satisfy. The recurring theme: primitives exist in a strong form on the **INO/V2** side and a weak/absent form on the **neuron/self-evolution** side, and the OS-critical ones (identity, capability, rollback, sandboxing) are governed by the weak side.

## Scorecard

| OS responsibility | Status | Evidence |
|---|---|---|
| Kernel / trusted computing base | **Partial** — TCB not delineated | `kernel-runtime:SEC-056`, `core:ARCH-001` |
| Identity: principals, tenants, workspaces | **Partial / duplicated** | `core:ARCH-002`, `kernel-hosting:ARCH-101` |
| Sessions | **Partial** — strong V2, dead-but-present v1 | `kernel-hosting:ARCH-101`, `SEC-100/101` |
| Capability / permission model | **Partial** — grants exist, fail open for unlisted | `connectors:ARCH-401`, `foundry:SEC-303` |
| Process/actor lifecycle | **Implemented** — Orleans grains | `kernel-runtime` |
| Scheduling, leases, cancellation, recovery | **Split** — INO strong, neuron weak | `kernel-runtime` (INO) vs `REL-103` |
| Durable state & migration | **Partial** — INO encrypted state good; journals unbounded, no migration story | `kernel-runtime:PERF-100`, `core:REL-001` |
| IPC via synapses | **Implemented shape, weak contract** — no mandatory identity, non-deterministic ids | `core:ARCH-002/REL-001` |
| Connector / device model | **Placeholder** — auth-only interface | `connectors:ARCH-400` |
| Package / behavior installation | **Aspirational** — signing unused | `connectors:SEC-405` |
| Policy enforcement | **Partial** — INO fail-closed; rail unenforced | `PlanInoToolGateway` vs `kernel-runtime:SEC-051` |
| Isolation / sandboxing | **Aspirational-naming-only** — real sandbox is dead code | `foundry:SEC-302` |
| Resource limits / backpressure / quotas | **Missing** on exec + journals; partial on edge | `foundry:REL-300`, `kernel-runtime:PERF-100` |
| Observability / tracing / audit | **Implemented** — OTEL + journals | `mcp-hosts-build`, `kernel-runtime` |
| Bootstrapping / health | **Partial** — health overclaims | `mcp-hosts-build:REL-500` |
| Upgrades / rollback / disaster recovery | **Aspirational** — rollback doesn't restore; self-update simulated | `kernel-runtime:ARCH-050/051` |
| User interaction / control surfaces | **Partial** — V2 surface feed strong; no unified control UI | `02`, `flutter-ui` |
| Secrets / credential management | **Split** — tokens encrypted well; DP keyring unencrypted; plaintext passwords journaled | `connectors`, `kernel-hosting:SEC-200`, `core:SEC-001` |
| Self-evolution governance | **Aspirational** — rail authenticates nothing | `kernel-runtime:SEC-050`, `05` |

## Primitive-by-primitive

### Kernel and trusted computing base — Partial
**Contract:** a small, explicitly-privileged core that everything else is untrusted relative to; never dynamically self-modifiable.
**Reality:** there is a silo host and privileged services, but no *delineated* TCB. Crypto and rate-limiting live in the "shared" Core layer (`core:ARCH-001`); the self-evolution rail — a mutation authority — is an ordinary grain with no trust distinction and no tenant boundary (`kernel-runtime:SEC-056`). **Recommendation:** define the TCB explicitly (host, identity, crypto, effect-plan authority, apply registry) and forbid self-evolution from touching it (see [05](05-self-evolution.md), [10](10-target-architecture.md)).

### Identity: principals, tenants, workspaces — Partial / duplicated
**Contract:** every actor and message carries a verified principal within a tenant/workspace; isolation is enforced, not conventional.
**Reality:** two models coexist. The V2 model (`TenantId`/`WorkspaceId`/`PrincipalRef`, `RuntimeRequestContext`) is proper and used on the live path. The legacy model (`string UserId = "anonymous"` on some synapses; `Neuron`/journal sessions) is weaker and still defines user identity in the kernel-journal auth path (`core:ARCH-002`, `kernel-hosting:ARCH-101`). **`Synapse` carries no mandatory principal at all** — the most fundamental OS contract (every message is attributable and isolatable) is not enforced at the type level. **Recommendation:** make principal/tenant mandatory on `Synapse`; delete the legacy identity model.

### Capability / permission model — Partial (fails open)
**Contract:** least-privilege, per-capability grants, checked fail-closed.
**Reality:** grants exist (`brain.read`, `ui.action`, `gmail.*`, `salesforce.*`) and the INO gateway is fail-closed. But `InoMutationGrants.RequiredForTool` **fails open for unlisted tools** (`connectors:ARCH-401`), and `TrustedAutoApply` is a blanket capability bypass (`foundry:SEC-303`). **Recommendation:** default-deny for unknown tools; remove blanket bypasses.

### IPC via synapses — Implemented shape, weak contract
**Contract:** typed, versioned, principal-scoped, deterministically-identified, causally-linked messages safe to journal and replay.
**Reality:** synapses are typed and carry causal ids, but (a) no mandatory principal (`core:ARCH-002`), (b) `Guid.NewGuid()`+`UtcNow` minted at construction → **replay is non-deterministic and there is no logical idempotency key** (`core:REL-001`), and (c) ~40 records rely on *implicit positional* Orleans field ids, so a parameter reorder silently corrupts journal/wire compatibility (`core:REL-001`, verified against Orleans 10 serialization docs). **Recommendation:** deterministic ids + explicit `[Id]` on every field + contract-freeze tests.

### Durable state & migration — Partial
**Contract:** durable, bounded, replayable state with a schema-migration path.
**Reality:** the INO runtime's `EncryptedPersistentState`/reconciliation is strong. But neuron journals are **unbounded** (no compaction/archival), projections are O(journal) per message (`kernel-runtime:PERF-100/REL-050`), and there is no state-migration story for the alpha journaling substrate (`kernel-runtime:FRAME-050`). **Recommendation:** compaction/snapshotting; a migration plan off alpha journaling.

### Isolation / sandboxing — Aspirational-naming-only
**Contract:** untrusted code runs confined, with resource caps, unable to reach host capabilities it wasn't granted.
**Reality:** the live executor is **in-process, full-trust** (`InProcessAlcExecutor`; collectible `AssemblyLoadContext` is memory reclamation, not isolation — `foundry:SEC-153`). The only real boundary, `OutOfProcessSandbox`, is registered in DI and **invoked nowhere** (`foundry:SEC-302`). The static `CapabilityGate` is bypassable by reflection, is a no-op for scripts, and is absent on the Deploy tier (`foundry:SEC-300/301/308`), and even names a WASM tier that does not exist. This is the single largest gap between naming and reality in the system. **Recommendation:** route all execution through out-of-process (later WASM) isolation with resource caps, or disable Foundry.

### Upgrades / rollback / disaster recovery — Aspirational
**Contract:** safe rolling upgrades, real rollback to a prior state, recoverable after partial failure.
**Reality:** `PerformKernelSelfUpdate` is a **simulation** — hardcoded 3 replicas, `RestartResource` only logs, a `FailAtReplica` *test hook ships in production* (`kernel-runtime:ARCH-050/PROD-101`). `RestoreCheckpointAsync` **appends** the snapshot back into the journal rather than replacing state (`kernel-runtime:ARCH-051`), so "rollback" grows state instead of reverting it. A failed self-evolution apply is terminal and non-retriable (`kernel-runtime:REL-103`). **Recommendation:** real snapshot+restore; retriable idempotent applies; either implement or remove the rolling-update theatre.

### Observability / audit — Implemented
OTEL tracing/metrics/logging via ServiceDefaults, structured logs, and durable journals give genuine audit material. Caveats: `/health` overclaims readiness (`mcp-hosts-build:REL-500`), and the audit log is itself on the unbounded, tamper-not-hardened journal (see [06](06-security-threat-model.md) on audit-log integrity).

### Secrets / credential management — Split
Connector tokens: per-value DataProtection encryption, strict scope isolation — **strong**. But the DataProtection **key ring is stored unencrypted** in the same blob container as the ciphertext it protects (`kernel-hosting:SEC-200`, verified against MS Learn), and **plaintext passwords are journaled** (`core:SEC-001`). **Recommendation:** `ProtectKeysWith*` the key ring; remove secrets from journaled vocabulary.

## The minimal trusted kernel (recommendation)

The TCB that must be small, audited, and **never self-modifiable**:

1. The silo/edge host process and its wiring (`Program.cs`, hosting extensions).
2. Identity & session authority (`RuntimeSessionAuthority`, principal/tenant model).
3. Cryptography: state/checkpoint protectors, DataProtection key management.
4. The INO effect-plan authority and the fail-closed tool gateway.
5. The self-evolution **apply registry and decision verifier** (the gate itself), and the sandbox boundary.
6. The connector trust/OAuth-state machinery and token store.

Everything else — individual neurons, packs, generated code, prompts, automations, UI surfaces — is untrusted relative to this core and may only change the world through it. What must **never** be dynamically self-modifiable: identity/tenancy, crypto, the decision verifier, the sandbox, the apply registry, and the TCB's own code. Self-evolution may propose changes to prompts, automations, packs, and non-TCB neurons — never to the list above (see [05](05-self-evolution.md)).

## Boundary problems (ranked)

1. **The mutation authority (self-evolution rail) is not in the TCB and authenticates nothing** — the most dangerous boundary inversion (`kernel-runtime:SEC-050/056`).
2. **Untrusted code runs inside the TCB process** (`foundry:SEC-302`).
3. **Two identity models, weak one authoritative** (`kernel-hosting:ARCH-101`).
4. **Provider (Gmail/Salesforce) names leak into kernel logic** (`connectors:ARCH-401`).
5. **Secrets cross the durability boundary in plaintext** (`core:SEC-001`, `kernel-hosting:SEC-200`).
