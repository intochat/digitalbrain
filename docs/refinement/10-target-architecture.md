# 10 — Target Architecture

The proposed architecture. It is deliberately **not a rewrite** — the target trust model already exists in this repository as the INO runtime and the V2 UI transport. The target is: *converge everything onto that model, delete the weaker duplicate, and lift self-evolution and connectors up to the same standard.* Migration is incremental and compatibility-aware. Evidence and rationale trace to [03](03-operating-system-assessment.md), [04](04-connectors-and-auth.md), [05](05-self-evolution.md), [06](06-security-threat-model.md).

## Design principles

1. **One trusted core, small and explicit.** Everything else is untrusted relative to it and may only change the world through it.
2. **One of everything.** One identity model, one session authority, one gateway, one approval-evidence model, one execution path.
3. **Governed effects are the spine.** Every external mutation and every self-modification flows through preview → approve → apply → journal → verify → rollback, with the same evidence model.
4. **Extensibility at the edges, not the core.** New connectors and behaviors register into typed contracts; the kernel resolves by capability, never by name.
5. **The core is never self-modifiable.** Self-evolution can change prompts, automations, packs, and non-TCB neurons — never identity, crypto, the verifier, the sandbox, the apply registry, or host code.

## Layered target

```
┌──────────────────────────────────────────────────────────────────────┐
│  CLIENTS (Flutter, MCP machine clients)                                │
│  - single V2 rail; legacy v1 deleted                                   │
│  - RFW with a constrained action allowlist                             │
└───────────────▲────────────────────────────────────────────────────────┘
                │ V2 gRPC (session tokens + capability action tokens)
┌───────────────┴────────────────────────────────────────────────────────┐
│  EDGE (DigitalBrain.Mcp)  — the ONLY external boundary                  │
│  - RuntimeSessionAuthority (one session/identity authority)            │
│  - UiGrpcService / MCP ino_interact (fail-closed, capability-scoped)   │
│  - scalable (multi-replica; session/feed state replica-safe)          │
└───────────────▲────────────────────────────────────────────────────────┘
                │ principal/tenant-scoped synapses (mandatory identity)
┌───────────────┴──── TRUSTED CORE (TCB — small, never self-modifiable) ──┐
│  Identity & tenancy   │ Crypto & secrets    │ INO Effect-Plan Authority │
│  (Principal/Tenant/   │ (state/checkpoint   │ (sign, preview, lease,    │
│   Workspace)          │  protectors, DP key │  fence, outcome-unknown)  │
│                       │  ring encrypted)    │                           │
│  Self-Evolution Rail (in TCB): decision VERIFIER (principal-bound,     │
│   content-hashed, single-use) + allowlisted apply registry + risk      │
│   classifier + real checkpoint/restore + verify phase + tamper-evident │
│   audit journal                                                        │
│  Sandbox Boundary: out-of-process (→ WASM) executor w/ timeouts+caps   │
└───────────────▲───────────────────────────▲────────────────────────────┘
                │                            │
┌───────────────┴──────────┐   ┌─────────────┴──────────────────────────┐
│  NEURONS (untrusted)     │   │  EXTENSION SURFACES (untrusted)         │
│  - conversation, memory, │   │  ConnectorRegistry: IConnector + typed  │
│    domain grains         │   │   CapabilityManifest (read/mutation)    │
│  - bounded, compacted    │   │  ConnectorHost: rate-limit/backoff/     │
│    journals; deterministic│   │   pagination/minimization/egress-allow │
│    synapse ids           │   │  PackHost: SIGNED, publisher-verified   │
│                          │   │   packs; embodied out-of-process         │
└──────────────────────────┘   └─────────────▲──────────────────────────┘
                                              │ OAuth on-demand, per-value encrypted tokens
                                    ┌─────────┴──────────┐
                                    │ Providers: Gmail,   │
                                    │ Salesforce, Nth…    │
                                    └─────────────────────┘
```

## The trusted core (definitive list)

Only these are privileged, audited, and **never reachable by the self-evolution rail**:

1. Host/edge process wiring (`Program.cs`, hosting/UI transport extensions).
2. Identity & session authority (`RuntimeSessionAuthority`, `Principal/Tenant/Workspace`).
3. Cryptography & secrets (state/checkpoint protectors; DataProtection key ring **encrypted** via Key Vault/managed identity).
4. INO effect-plan authority + fail-closed tool gateway.
5. Self-evolution **decision verifier**, **apply registry**, and **risk classifier**.
6. The **sandbox boundary** (out-of-process/WASM executor).
7. Connector trust/OAuth-state machinery + encrypted token store.

## Key contracts (target)

### Synapse (fix the IPC contract)
- Mandatory `PrincipalRef` + `TenantId`/`WorkspaceId` on every synapse (`core:ARCH-002`).
- Deterministic id derived from content + causal context (no `Guid.NewGuid()` at construction); a logical idempotency key (`core:REL-001`).
- Explicit `[Id(n)]` on every serialized field; contract-freeze tests.

### Connector capability model (from [04](04-connectors-and-auth.md))
- `IConnector` keeps the auth lifecycle, adds refresh/rotate/revoke.
- Connectors declare `CapabilityManifest[]` (id, kind, schemas, scopes, grants, reversibility, rate class) and self-register.
- Uniform `ReadAsync` (minimized, provenance-tagged) and `Preview→Apply(idempotencyKey)→Verify` (generalized from Salesforce) with `OutcomeUnknown`.
- Kernel/INO resolve by capability; **no provider names in the core**; unknown capability = deny.

### Self-evolution rail (from [05](05-self-evolution.md))
- Decisions carry principal + content-hash + single-use nonce; the verifier (in TCB) checks all three (copy `DurableInoContracts`).
- Risk classified in-kernel, not by the proposer; `RequiresHumanApproval` enforced.
- Per-tier policy (T0 prompt … T5 kernel/never); T4 code execution always out-of-process; T5 outside the rail entirely.
- Real checkpoint/restore; retriable idempotent apply; a verify phase; tamper-evident (hash-chained) audit journal.
- Executor grains reachable **only** from the apply registry — not public.

## Data & control flow (target, unified)

Every effect — connector mutation, automation, pack install, generated code — takes the same shape:

```
intent → INO proposes → bounded human-readable diff → in-kernel risk class
  → deterministic validation → principal-bound single-use approval
  → authenticated artifact (signed pack / source hash == approved hash)
  → isolated apply (connector host | out-of-process sandbox)
  → tamper-evident journal → post-apply verify → rollback-capable → observable
```

The INO effect rail already implements the right half of this for connector mutations; the work is extending the same shape to automations, packs, and code.

## Compatibility and migration strategy

The migration is **strangler-pattern, deletion-led**, staged to keep the app working at every step:

1. **Delete the dead duplicate first** (legacy gateway/proto/stubs, second auth authority, dead Core/kernel/Flutter code). This is non-breaking (the code is unreached) and immediately shrinks attack surface. *Compatibility risk: near-zero* (verified-dead). *Journal risk:* audit historical journals for dead synapse aliases before removing them; tombstone if present (`core:CLEAN-001` precondition).
2. **Converge identity/session** onto `RuntimeSessionAuthority`; add mandatory principal/tenant to `Synapse` as additive fields (default-populated from the authenticated context) before making them required. *Compatibility:* additive-then-enforced; journal-compatible via explicit `[Id]`.
3. **Bind self-evolution to the INO evidence model** and put the rail in the TCB. *Compatibility:* new decision fields are additive; the verifier rejects unbound legacy decisions (which is the point). Disable Foundry/pack execution by default during this step.
4. **Introduce the connector capability registry** behind the existing neurons; migrate Gmail/Salesforce to manifests; delete hardcoded provider branches. *Compatibility:* registry resolves the same capabilities the neurons already expose; behavior-preserving.
5. **Replace in-process execution with the out-of-process sandbox**; add timeouts/caps; gate Deploy. *Compatibility:* execution semantics preserved, isolation added.
6. **Fix durability**: real checkpoint/restore, journal compaction, deterministic ids. *Compatibility:* requires a journal-format migration — do it behind the additive-id work in step 2; provide a one-time replay/rewrite.
7. **Scale the edge** (multi-replica; resolve the Orleans double-config; replica-safe session/feed). *Compatibility:* validated by a multi-replica boot/drain test before rollout.

**Rolling-deployment note:** step 7 must land a *real* drain/verify rolling update to replace the current simulation (`kernel-runtime:ARCH-050`), and must first resolve the Orleans provider double-config that may throw under managed identity (`kernel-hosting:FRAME-200`). Until then, treat deploys as single-replica with brief downtime rather than claiming zero-downtime.

## What this architecture deliberately does *not* do

- It does not introduce new frameworks ([08](08-framework-and-dependency-audit.md) found the framework choices sound).
- It does not rewrite the INO runtime or connector auth layer — those are the reference the rest is converging toward.
- It does not enable autonomous mutation: the rail is human-in-the-loop by construction, with `TrustedAutoApply` removed or hard-restricted.
- It does not make the core self-modifiable — that boundary is the difference between governed evolution and an uncontrolled system.
