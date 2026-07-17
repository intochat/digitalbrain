# DigitalBrain v2 — Slice 6: Behaviors — The Self-Evolution Rail

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Checkbox steps.

**Goal:** The self-evolution rail from spec v2 §5: a `behavior` lifecycle kind (hash + journal — propose → human approve → enabled; rollback = re-enable a prior hash), a generic kernel grant-issuance mechanism that binds grants to a **content-hash identity**, the dev-script vs installed-behavior trust model plumbed through Brain.Client, and a Roslyn compile gate for authoring. Proven live: a real single-file behavior compiles → proposes → is approved on the rail → runs as a process **under its `behavior/{hash}` identity** → posts to chat (a granted contract), is refused an ungranted contract (`grant.missing`), and can only **propose** an effect (never execute it) — and its activity now appears in the feed (closes the Slice-3 behavior-feed carry).

**Architecture:** Grants have a real issuer for the first time: two kernel-built-in contracts, `neuron.grant.v1 {granteeKey, contract}` and `neuron.revoke.v1`, handled in `NeuronGrain` before kind dispatch (like the effect gate), owner-only AND **refused to behavior-space callers** (no self-escalation). `BehaviorKind` is pure governance: its journal is the lifecycle; on `behavior.approve.v1` (human, via the rail) it grain-calls `neuron.grant.v1` on each requested target, binding the grant to `{owner}|behavior/{hash}|behavior/{hash}`. The running behavior is an ordinary cluster-client process (spec §5.1 — no new runtime), but `BrainCluster.Connect` now reads a caller identity from `BRAIN_CALLER`, so the process speaks as its behavior identity and is grant-gated by the kernel exactly like any foreign caller. `BehaviorCompiler` (Roslyn) is the authoring/BDD gate: it proves a source compiles against the brain API before a human ever sees the proposal.

**Deliberate boundaries / carries:** The LLM self-healing authoring loop (generate → compile → feed diagnostics back → regenerate, IAW CodeOrchestrator) is modules/ai sugar on top of the compile gate — DEFERRED. An automatic BehaviorRunner grain that watches enabled behaviors and launches them is DEFERRED (the live proof launches the process by hand, exactly as behaviors/smoke runs today). In-proc assembly loading is NOT used — behaviors are separate processes (honors "no plugin framework"). Grant issuance persists no secrets; the journal-encryption gate from Slice 5 still governs durable storage.

## Global Constraints

Zero comments · CPM · net11.0 · v1 untouched · framework primitives (Roslyn = Microsoft.CodeAnalysis.CSharp; process launch = same pattern as behaviors/smoke) · grants issuable ONLY by same-owner non-behavior callers · behaviors can ONLY propose effects (execute paths already require ApprovedEffectProof — unchanged) · root suite green zero skips pristine per commit · camelCase.

### Task 1: Generic grant issuance in the kernel (commit 1)

**Files:** `kernel/Brain.Kernel/NeuronGrain.cs` (intercept before kind dispatch), `kernel/Brain.Contracts/BrainErrors.cs` (add `GrantDenied = "grant.denied"`); Test `tests/Brain.KernelTests/GrantIssueTests.cs`.

- In `NeuronGrain.InvokeAsync`, AFTER the replay check + caller parse but BEFORE the existing grant check / kind dispatch, intercept two built-in contracts (any kind, any address):
  - `neuron.grant.v1` input `{granteeKey, contract}` — issuer must be same-owner AND `caller.SpaceId` must NOT start with `"behavior/"` (else `BrainException(BrainErrors.GrantDenied, …)`, zero state). Appends a `SynapseRecord(SynapseRelation.Grants, granteeKey, contract, Revision)` to `state.Synapses`; idempotent (no duplicate if an identical Grants synapse exists); output `{"granted":true}`; persists.
  - `neuron.revoke.v1` input `{granteeKey, contract}` — same issuer rule; removes matching Grants synapses; output `{"revoked":n}`.
- These do NOT go through `RequireKind()` (they work even on kinds that don't exist yet — e.g. granting on a not-yet-activated neuron), and they are NOT listed in any kind's `Contracts` (they're kernel-universal; catalog need not list them this slice).
- Tests: owner issues grant on chat/main for a foreign caller X + contract chat.post.v1 → X (different owner OR behavior space) can now invoke chat.post.v1 where it previously got grant.missing; a behavior-space caller attempting neuron.grant.v1 → grant.denied zero-state; revoke removes access; grant is idempotent (twice → one synapse); non-owner issuer → grant.missing (existing check) or grant.denied.

### Task 2: BehaviorKind — lifecycle + grant binding (commit 2)

**Files:** `modules/Brain.Modules.Behaviors/Brain.Modules.Behaviors.csproj` (refs Brain.Contracts), `BehaviorKind.cs`, `IBehavior.cs`; Test `tests/Brain.KernelTests/BehaviorKindTests.cs` + `BehaviorsKindsConfigurator`; conformance class.

- `BehaviorKind(IGrainFactory grainFactory)` (factory-registered for the grain-call): kind `behavior`; contracts:
  - `behavior.propose.v1` input `{source, sourceHash, bddPassed, grants:[{address,contract}]}` — validate sourceHash == sha256(source) (else input.invalid), source ≤ 128 KB, grants ≤ 32; event `behavior.proposed` {sourceHash, grantsJson, bddPassed}; output `{status:"proposed", sourceHash}`. Reject if bddPassed is false → `input.invalid` ("bdd gate failed") zero-state (the compile/BDD gate must pass before proposal).
  - `behavior.approve.v1` input `{sourceHash}` — only valid when the latest proposal has that hash and current state is proposed (else input.invalid); for each recorded grant, grain-call `neuron.grant.v1` on `{owner}|{grant.addressSpace}|{grant.addressNeuron}` (parse the target address) with granteeKey `{owner}|behavior/{sourceHash}|behavior/{sourceHash}` and the grant contract, caller = the behavior neuron's own key (context.Address.ToGrainKey(), which is same-owner non-behavior space); event `behavior.enabled` {sourceHash, grantsJson}; output `{status:"enabled", identity:"…behavior/{hash}…"}`.
  - `behavior.decline.v1` {sourceHash, reason} → event `behavior.declined`; output status declined.
  - `behavior.rollback.v1` {sourceHash} → only to a previously enabled hash present in the journal; re-issues that hash's grants; event `behavior.rolledback` → enabled at prior hash.
- Projection `status` (default): fold → `{state, activeHash, identity, grants:[…], history:[{hash,state}…]}`.
- `IBehavior` typed interface (propose/approve/decline shapes; camelCase records).
- Trust model (documented via test names + assertions, no comments): the behavior NEURON (kind behavior, an owner actor-space governance object) is distinct from the behavior IDENTITY (`behavior/{hash}`, the untrusted running script). Approve binds grants to the identity, not the neuron.
- Tests: propose (good hash) → proposed; propose with wrong sourceHash → input.invalid; propose bddPassed=false → input.invalid; approve → enabled AND the granted target neuron now admits the behavior identity (invoke the granted contract as the behavior identity → succeeds; an UNgranted contract as that identity → grant.missing); a second identity (different hash) has no grants; rollback re-enables a prior hash's grants; approve of a non-proposed hash → input.invalid. Conformance class (SampleContract behavior.propose.v1 with a valid payload).

### Task 3: Behavior identity plumbing + compile gate + wiring (commit 3)

**Files:** `kernel/Brain.Client/BrainCluster.cs` (caller identity), `modules/Brain.Modules.Behaviors/BehaviorCompiler.cs` + `IBehaviorApi` marker, `BehaviorsHosting.cs`, host `Program.cs`; Tests `tests/Brain.KernelTests/BehaviorCompilerTests.cs`.

- `BrainCluster.Connect(args)`: read caller identity from config/env `BRAIN_CALLER`; default the current `local-owner|actor/script|session/{guid}`. `Get<T>` uses that identity (store on the instance). Add `Connect(string[] args, string callerKey)` overload too. Behaviors/smoke unaffected (env unset → default).
- `BehaviorCompiler` (Roslyn `CSharpCompilation`): `CompileResult Check(string source)` → compiles the single-file source as a script/program referencing Brain.Client + Brain.Contracts + module contract assemblies (resolve their MetadataReferences via `typeof(BrainCluster).Assembly.Location` etc.); returns `{success, diagnostics:[…]}`. This is the authoring/BDD gate: the caller runs Check before proposing and sets bddPassed. Package `Microsoft.CodeAnalysis.CSharp` (add PackageVersion — latest matching).
- `BehaviorsHosting.AddBrainBehaviors(this ISiloBuilder)`: `AddBrainKind("behavior", sp => new BehaviorKind(sp.GetRequiredService<IGrainFactory>()))`. Host wires it.
- Tests: compiler accepts a valid behavior source (uses BrainCluster.Connect + Get<IChat>.PostAsync); compiler rejects source with a type error (diagnostics non-empty, success false); a source referencing a non-existent contract member → rejected.

### Task 4: Live proof (controller)

Write a real behavior `behaviors/inbox-brief/Behavior.cs` (single file: connect, read nothing external, post a chat message via the granted contract, then attempt an ungranted `web.fetch.v1`, then `gmail.propose-send.v1`). Steps:
1. Silo + gateway up. Compile the behavior via a tiny `dotnet run` of the compiler (or a test) → success, bddPassed true. Compute sourceHash.
2. `POST /ui/invoke` behavior.propose.v1 on `local-owner|actor/dev|behavior/inbox-brief` {source, sourceHash, bddPassed:true, grants:[{chat/main, chat.post.v1}]} → proposed.
3. `POST /ui/invoke` behavior.approve.v1 {sourceHash} → enabled; status projection shows identity + the chat grant.
4. Run the compiled behavior as a process with `BRAIN_CALLER=local-owner|behavior/{hash}|behavior/{hash}`: it posts to chat (200), the ungranted web.fetch returns grant.missing (behavior prints it and continues), and gmail.propose-send yields an effectKey (proposed, NOT executed).
5. `GET /ui/read` chat/main → the behavior's message present; feed/main recent → shows the behavior's chat post (behavior activity in the feed — Slice-3 carry closed). effect/… still pending (unapproved).
6. Root suite green zero skips.

## Self-review

§5.1 scripting-as-cluster-client ✓ (BRAIN_CALLER identity), §5.2 trust model ✓ (neuron vs identity; grants bound to hash; dev bypass impossible — the process is grant-gated), §5.3 hash+journal lifecycle + rollback ✓, §5.4 authoring compile gate ✓ (LLM loop deferred, honest carry). Generic grant issuance is a net-new kernel capability that also unblocks any future non-behavior grant. Behaviors propose-only is inherited (execute needs ApprovedEffectProof, untouched).
