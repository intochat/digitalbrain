# 00 — Executive Assessment

**Assessed commit:** `72400e3ebbec27e17af4ae6b5b2c4158c2797fa4` (merge of PR #14 `shape-v3`, 2026-07-13)
**Scope:** all 728 tracked files, reviewed file-by-file (see [coverage-ledger.md](coverage-ledger.md)).
**Method:** CodeGraph architecture mapping → line-by-line reads → framework verification against current docs → 337 evidence-backed findings.

## Verdict

DigitalBrain is **two systems wearing one name**. There is a genuinely well-engineered modern core — the **INO runtime** (`src/DigitalBrain.Kernel/Runtime/*`) and the **V2 UI transport** (`src/DigitalBrain.Mcp/UiGrpcService.cs`) — with signed effect plans, AES-GCM encrypted state, lease/fence/outcome-unknown operation semantics, capability-scoped action tokens, fail-closed gateways, principal-bound approvals, per-value encrypted connector tokens, and an adversarial test suite that exercises real production code. Judged alone, that half is close to the operating-system ambition the project states.

Wrapped around it is an **older "neuron" substrate** — the `Neuron`/`NeuronJournals` base, the self-evolution rail, the Foundry code-generation/execution machinery, the legacy `DigitalBrainGateway` proto, and a second identity/session model — that is weaker, partly simulated, partly dead, and built on an **alpha** Orleans Journaling API. Crucially, **the project's headline capability — governed self-evolution — lives entirely on the weaker substrate.** The rail that is supposed to be "the only path for user-visible mutations" authenticates no approver, does not bind approval to the thing approved, cannot actually roll back, grows without bound, and gates untrusted code execution with a static filter the code's own comments call "not a security boundary."

This is not a system that is broken today so much as one whose **safety-critical guarantees are asserted but not yet real**. The 471 passing tests are misleading on exactly this point: several of the security controls they "prove" are wired to nothing (see `dotnet-tests:SEC-600`, `dotnet-tests:SEC-601`).

**Bottom line:** the foundations to build a safe self-evolving OS exist *in this repository* — but on the INO side, not the neuron side. The highest-leverage work is not new construction; it is **lifting the self-evolution rail onto the trust model the INO runtime already implements**, and deleting the weaker duplicate. Incremental correction is both cheaper and safer than a rewrite; the correct primitives already exist to copy.

## Strongest foundations (keep, and make everything else look like these)

1. **INO effect-plan runtime** (`Runtime/InoEffectPlanNeuron`, `InoEffectPlanAuthority`, `InoOperationWorkerGrain`, `PlanInoToolGateway`): signed plans, lease fencing, outcome-unknown handling, fail-closed default (`ClosedInoToolGateway` denies all unless `DigitalBrain:Tools:Enabled`). Reference quality (`kernel-runtime` positive notes; `dotnet-tests` counterweight).
2. **V2 UI transport** (`UiGrpcService`): session-token auth on every RPC, capability-scoped action tokens with surface-revision binding, principal-bound approvals, identity-injection input filter, bootstrap secret forbidden in Production.
3. **Connector auth/token layer**: per-value DataProtection encryption at rest, strict app/user scope isolation, owner-bound OAuth state with SHA-256 fingerprints and fixed-time compares, Salesforce S256 PKCE, least-privilege Google scopes (`gmail.readonly` + `gmail.send`). Salesforce's preview→apply→verify pipeline (`connectors`) is the model every mutation should follow.
4. **Test culture on the INO side**: ~4,400 lines of adversarial client tests plus encrypted-state/lease/replay coverage that assert negative properties (token non-leakage, scope isolation).

## Largest architectural risks

1. **Duplicated authority / two trust models** (`core:ARCH-001/002`, `kernel-hosting:ARCH-101`, `kernel-runtime` thesis, `flutter-runtime:ARCH-700`). Identity, sessions, gateways, and approval evidence each exist in a strong V2 form *and* a weak legacy form, and the weak one still defines security-relevant behavior (user identity, mutation approval). This is the root cause behind most Critical/High findings.
2. **Self-evolution rail is ungoverned in substance** (`kernel-runtime:SEC-050`, `core:PROD-001`, `kernel-runtime:SEC-051/052`). Unauthenticated approver, approval not bound to content, `RequiresHumanApproval` never read, proposer-supplied risk trusted.
3. **Foundry code execution is unsafe by construction** (`foundry:SEC-302/301/304/308/300`). In-process full-trust execution; the real out-of-process sandbox is dead code; the static gate is bypassable and absent on the Deploy tier; executor grains are directly fireable.
4. **Durability/rollback is not real** (`kernel-runtime:ARCH-051`, `REL-101/103`, `PERF-100`). "Checkpoint/restore" appends instead of restoring; journals grow unbounded; failed applies wedge half-done; replay is non-deterministic (`core:REL-001`).
5. **Preview/alpha production stack** (`mcp-hosts-build:PROD-500`, `kernel-runtime:FRAME-050`): `net11.0` preview, `aspnet:11.0-preview`, CI `dotnet-quality: preview`, Orleans preview + Journaling **alpha** — the durability substrate has no servicing guarantee — plus an unpinned remote-npm build step (`mcp-hosts-build:SEC-500`).

## Largest product risks

1. **The demo-able journeys don't hold up** where they touch the neuron side: server-driven UI can forward attacker-shaped actions (`flutter-ui:SEC-800`), the legacy client rail silently no-ops (`flutter-runtime:ARCH-700`), and the Android/macOS clients can't even reach the kernel as configured (`platform-and-skills:PROD-1000/1001`).
2. **~25% of the Core vocabulary is unimplemented promise** (`core:CLEAN-001`, `core:PROD-001`) — the type system advertises capabilities that don't exist, which will mislead both users and future contributors.
3. **"Connect to anything" is not achievable with the current `IConnector`** (`connectors:ARCH-400`): it is an auth-lifecycle interface only; every capability is hand-written per provider with hardcoded provider names, and grant checks fail *open* for unlisted tools (`connectors:ARCH-401`).
4. **Auth is the weakest security surface a user touches**: config-enabled `admin/admin` in any environment (`kernel-hosting:SEC-100`), fail-open first-user provisioning to admin (`kernel-hosting:SEC-101`), plaintext passwords in the durable journal (`core:SEC-001`).

## Top recommendations (detail in [12-implementation-plan.md](12-implementation-plan.md))

1. **Freeze self-evolution apply behind the INO evidence model.** Make `SelfEvolutionDecision` carry a principal-bound, content-hashed, single-use decision the grain *verifies* (copy `DurableInoContracts`). Enforce `RequiresHumanApproval`. Reject proposer-supplied risk; classify server-side. (`kernel-runtime:SEC-050/051/052`, `core:PROD-001`)
2. **Make Foundry execution safe or disable it.** Route *all* generated-code execution (Run **and** Deploy, scripts included) through the out-of-process sandbox with timeouts and resource caps; stop registering `InProcessAlcExecutor`; make executor grains reachable only via the approval rail. Until then, gate the whole Foundry loop off by default. (`foundry:SEC-301/302/304/308`, `REL-300`)
3. **Delete the weaker duplicate stratum.** Remove the legacy gateway proto + Dart stubs, the `UserSessionNeuron`/`DevAuth` second auth authority, the ~30 dead Core types, and the dead kernel components (`IngressNeuron`, `SignalEgressBus`, `ChatNeuron`, `TabularDataParser`, `SqliteSchemaInspector`, `SyncManifest`). One identity model, one session authority, one gateway. (`core:CLEAN-001`, `kernel-hosting:CLEAN-200`, `flutter-runtime:ARCH-700`)
4. **Make durability real.** Replace additive checkpoint/restore with a true state snapshot+restore; bound journals with compaction/archival; make approved applies retriable and idempotent; give synapses deterministic ids. (`kernel-runtime:ARCH-051/REL-101/103`, `core:REL-001`)
5. **Fix auth fail-open defaults and secret hygiene.** Environment-gate `DevAutoLogin`, make first-user provisioning require an out-of-band bootstrap secret, remove plaintext passwords from journaled vocabulary, encrypt the DataProtection key ring (`ProtectKeysWith*`). (`kernel-hosting:SEC-100/101/200`, `core:SEC-001`)
6. **Pin the toolchain and the build.** Move off preview/alpha for the trusted core where possible (or explicitly accept and document the risk with a servicing plan); pin the `codegraph` npm build step. (`mcp-hosts-build:PROD-500/SEC-500`, `kernel-runtime:FRAME-050`)

## Ten most important findings

See [findings-register.md](findings-register.md) for all 337. The ten that should drive the first remediation phase:

| # | Ref | Sev | One line |
|---|---|---|---|
| 1 | `foundry:SEC-302` | Critical | Only real sandbox is dead code; generated code runs in-process at full trust. |
| 2 | `kernel-runtime:SEC-050` | Critical | Self-evolution approvals are unauthenticated free strings — forgeable. |
| 3 | `foundry:SEC-308` | Critical | Deploy tier compiles generated code into the kernel with no capability gate. |
| 4 | `foundry:SEC-301` | Critical | Automation `ScriptRunner` gates against a zero-reference compilation → gate is a no-op. |
| 5 | `foundry:SEC-304` | Critical | Executor grains are directly fireable, bypassing the approval rail entirely. |
| 6 | `core:SEC-001` | High | Plaintext passwords are written into the durable, replayable journal. |
| 7 | `kernel-hosting:SEC-100` | High | Config flag enables `admin/admin` in any environment and bypasses passwords. |
| 8 | `kernel-runtime:ARCH-051` | High | "Rollback" appends the snapshot to the journal instead of restoring state. |
| 9 | `connectors:ARCH-400` | High | `IConnector` is auth-only; "connect to anything" needs kernel edits per provider. |
| 10 | `mcp-hosts-build:PROD-500` | High | Entire production stack is preview/alpha (incl. an unpinned remote-npm build step). |

## What the evidence does *not* support

- It does **not** support calling the architecture secure or production-ready.
- It does **not** support a ground-up rewrite: the target trust model already exists in-tree (INO runtime) and should be extended, not reinvented.
- It does **not** support treating green CI as evidence of safety for self-evolution or sandboxing (`dotnet-tests:SEC-600/601`).
- Several framework-usage judgments are **doc-gap-limited**: Context7's quota was exhausted mid-assessment and the alpha Orleans Journaling API is undocumented publicly; those findings rest on source + Microsoft Learn and are labeled accordingly ([08-framework-and-dependency-audit.md](08-framework-and-dependency-audit.md)).
